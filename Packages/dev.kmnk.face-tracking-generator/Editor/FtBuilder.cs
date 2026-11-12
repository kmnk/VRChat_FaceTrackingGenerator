using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Adds the FT layers to the FX controller (design.md "FT の Animator の組み方"):
    //   FT/Smoothing  smooths inputs that the table smooths (Direct BlendTree feeding back into itself)
    //   FT/Gate       FT/Active and FT/MouthActive from FaceTrack, AFK, Voice and UserFaceTracking
    //   FT/Blink      with stopAutoEyes: blinks (FT/Blink) while FT is off, in place of Eye Look's eyelids
    //   FT/Switch/*   one layer per switch; each state writes FT/Sw/<switch>/<state>
    //   FT/Face       the only layer that writes blendshapes: one Direct BlendTree
    public sealed class FtBuilder
    {
        public const string One = "FT/One";
        public const string Active = "FT/Active";
        public const string MouthActive = "FT/MouthActive";
        public const string Blink = "FT/Blink";
        const float BlinkInterval = 4f;   // seconds between blinks while FT is off
        const float BlinkLength = 0.15f;  // seconds the eyes take to close and open again
        public const float TalkThreshold = 0.1f;
        const float ConstantModeFps = 60;

        readonly TableModel m;
        readonly AnimUtil u;
        readonly List<AfkGroup> afkGroups;
        public readonly List<AnimatorControllerLayer> Layers = new List<AnimatorControllerLayer>();

        public FtBuilder(TableModel model, AnimatorController fx, List<AfkGroup> afkGroups)
        {
            m = model;
            u = new AnimUtil(fx);
            this.afkGroups = afkGroups;
        }

        public static string SmoothParam(string input) => "FT/Smooth/" + input;
        public static string DerivedParam(string input) => "FT/In/" + input;
        public static string SwitchParam(string sw, string state) => "FT/Sw/" + sw + "/" + state;
        // Names in the FT/Face Direct BlendTree: a region group ("Active" or "Mouth"), and a tree's motion in that group.
        public static readonly string[] FaceGroups = { "Active", "Mouth" };  // regions that keep moving while talking, and the others
        public static string FaceMotion(string group) => "FT/Face/" + group;
        public static string FaceMotion(string group, string tree) => FaceMotion(group) + "/" + tree;

        // The parameter that BlendTrees and conditions read for an input.
        public string InputParam(string name)
        {
            var i = m.Input(name) ?? throw new InvalidOperationException("unknown input " + name);
            if (i.IsDerived) return DerivedParam(name);
            return i.Smoothing != null ? SmoothParam(name) : i.Parameter;
        }

        public const string DeltaTime = "FT/DeltaTime";

        // VRChat's built-in parameter: true while a face tracking source (Selfie Expression, OSC face tracking) is active.
        // FT runs only while it is true (R8).
        public const string SourceParameter = "UserFaceTracking";

        // The condition on the face tracking source, in one place so that it can be changed alone.
        void WhenSource(AnimatorStateTransition t, bool present) => u.When(t, SourceParameter, present);

        public void Build()
        {
            Parameters();
            FrameTime();
            Smoothing();
            Gate();
            if (Blinks) BlinkLayer();
            foreach (var sw in m.Switches) Switch(sw);
            Face();
        }

        void Parameters()
        {
            u.EnsureParameter(One, AnimatorControllerParameterType.Float, 1);
            u.EnsureParameter(Active, AnimatorControllerParameterType.Float);
            u.EnsureParameter(MouthActive, AnimatorControllerParameterType.Float);
            if (Blinks) u.EnsureParameter(Blink, AnimatorControllerParameterType.Float);
            u.EnsureParameter(m.FaceTrackParameter, AnimatorControllerParameterType.Bool, 1);
            u.EnsureParameter("IsLocal", AnimatorControllerParameterType.Bool);
            u.EnsureParameter("Voice", AnimatorControllerParameterType.Float);
            u.EnsureParameter(SourceParameter, AnimatorControllerParameterType.Bool);
            foreach (var p in afkGroups.SelectMany(g => g.Parameters))
                u.EnsureParameter(p, p == AfkBuilder.Playing ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool);
            foreach (var i in m.Inputs)
            {
                if (i.IsDerived) { u.EnsureParameter(DerivedParam(i.Name), AnimatorControllerParameterType.Float); continue; }
                u.EnsureParameter(i.Parameter, AnimatorControllerParameterType.Float);
                if (i.Smoothing != null) u.EnsureParameter(SmoothParam(i.Name), AnimatorControllerParameterType.Float);
            }
            foreach (var s in m.Smoothings)
            {
                u.EnsureParameter(LocalS(s), AnimatorControllerParameterType.Float, SFromTau(s.LocalTau));
                u.EnsureParameter(RemoteS(s), AnimatorControllerParameterType.Float, SFromTau(s.RemoteTau));
            }
            foreach (var sw in m.Switches)
                for (int k = 0; k < sw.States.Count; k++)
                    u.EnsureParameter(SwitchParam(sw.Name, sw.States[k]), AnimatorControllerParameterType.Float, k == 0 ? 1 : 0);
        }

        static string LocalS(SmoothingDef s) => "FT/S/" + s.Name + "/Local";
        static string RemoteS(SmoothingDef s) => "FT/S/" + s.Name + "/Remote";

        // s per frame at 60 fps for a time constant. Used as the default value until FT/FrameTime writes s.
        public static float SFromTau(float tau) => tau <= 0 ? 0 : Mathf.Exp(-1f / ConstantModeFps / tau);

        // ---- FT/FrameTime: a clock clip runs all the time (FT/Clock, looping every 1000 s). Parameters that
        // AAP writes are read one frame late inside the BlendTree, so FT/ClockPrev gets the previous value and
        // FT/DeltaTime = FT/Clock - FT/ClockPrev is the frame time (one frame late).

        public const string Clock = "FT/Clock";
        public const string ClockPrev = "FT/ClockPrev";
        const float ClockLength = 1000f;

        void FrameTime()
        {
            u.EnsureParameter(DeltaTime, AnimatorControllerParameterType.Float);
            u.EnsureParameter(Clock, AnimatorControllerParameterType.Float);
            u.EnsureParameter(ClockPrev, AnimatorControllerParameterType.Float);
            var layer = u.NewLayer("FT/FrameTime");
            Layers.Add(layer);
            var root = u.NewTree("FT/FrameTime", BlendTreeType.Direct);
            AnimUtil.AddDirectChild(root, u.RampClip("FT/FrameTime/Clock", Clock, ClockLength, true), One);
            var copy = u.NewTree("FT/FrameTime/Copy", BlendTreeType.Simple1D, Clock);
            copy.AddChild(u.ParamClip("FT/FrameTime/Copy/0", new[] { (ClockPrev, 0f) }), 0);
            copy.AddChild(u.ParamClip("FT/FrameTime/Copy/End", new[] { (ClockPrev, ClockLength) }), ClockLength);
            AnimUtil.AddDirectChild(root, copy, One);
            AnimUtil.AddDirectChild(root, u.ParamClip("FT/FrameTime/Plus", new[] { (DeltaTime, 1f) }), Clock);
            AnimUtil.AddDirectChild(root, u.ParamClip("FT/FrameTime/Minus", new[] { (DeltaTime, -1f) }), ClockPrev);
            // s = exp(-dt / tau) for each smoothing, local and remote, as a piecewise linear 1D BlendTree on dt.
            foreach (var sd in m.Smoothings)
                foreach (var (param, tau, label) in new[] { (LocalS(sd), sd.LocalTau, "Local"), (RemoteS(sd), sd.RemoteTau, "Remote") })
                {
                    var bt = u.NewTree($"FT/FrameTime/S/{sd.Name}/{label}", BlendTreeType.Simple1D, DeltaTime);
                    foreach (var dt in SGrid)
                        bt.AddChild(u.ParamClip($"FT/FrameTime/S/{sd.Name}/{label}/{dt.ToString("0.###", CultureInfo.InvariantCulture)}",
                            new[] { (param, tau <= 0 ? 0f : Mathf.Exp(-dt / tau)) }), dt);
                    AnimUtil.AddDirectChild(root, bt, One);
                }
            AnimUtil.State(layer.stateMachine, "Measure", root, new Vector3(300, 0));
        }

        // Frame times (seconds) where s is computed exactly; dense below 20 ms, where tau of a few ms changes s fast.
        static readonly float[] SGrid =
        {
            0f, 0.001f, 0.002f, 0.003f, 0.004f, 0.005f, 0.006f, 0.007f, 0.008f, 0.009f, 0.010f, 0.011f, 0.012f, 0.013f,
            0.014f, 0.015f, 0.016f, 0.017f, 0.018f, 0.019f, 0.020f, 0.025f, 0.030f, 0.040f, 0.050f, 0.070f, 0.100f,
        };

        // ---- FT/Smoothing

        void Smoothing()
        {
            var layer = u.NewLayer("FT/Smoothing");
            Layers.Add(layer);
            var sm = layer.stateMachine;
            var local = AnimUtil.State(sm, "Local", SmoothingTree("Local", true), new Vector3(300, 0));
            var remote = AnimUtil.State(sm, "Remote", SmoothingTree("Remote", false), new Vector3(300, 100));
            sm.defaultState = remote;
            u.When(AnimUtil.Transition(remote, local), "IsLocal", true);
            u.When(AnimUtil.Transition(local, remote), "IsLocal", false);
        }

        BlendTree SmoothingTree(string mode, bool local)
        {
            var root = u.NewTree("FT/Smoothing/" + mode, BlendTreeType.Direct);
            foreach (var i in m.Inputs.Where(x => !x.IsDerived && x.Smoothing != null))
            {
                var s = m.Smoothings.First(x => x.Name == i.Smoothing);
                var target = SmoothParam(i.Name);
                var lo = u.ParamClip($"FT/Smoothing/{mode}/{i.Name}/-1", new[] { (target, -1f) });
                var hi = u.ParamClip($"FT/Smoothing/{mode}/{i.Name}/1", new[] { (target, 1f) });
                var fromInput = u.NewTree($"FT/Smoothing/{mode}/{i.Name}/Input", BlendTreeType.Simple1D, i.Parameter);
                fromInput.AddChild(lo, -1);
                fromInput.AddChild(hi, 1);
                var feedback = u.NewTree($"FT/Smoothing/{mode}/{i.Name}/Feedback", BlendTreeType.Simple1D, target);
                feedback.AddChild(lo, -1);
                feedback.AddChild(hi, 1);
                // next = (1 - s) * input + s * current
                var mix = u.NewTree($"FT/Smoothing/{mode}/{i.Name}", BlendTreeType.Simple1D, local ? LocalS(s) : RemoteS(s));
                mix.AddChild(fromInput, 0);
                mix.AddChild(feedback, 1);
                AnimUtil.AddDirectChild(root, mix, One);
            }
            // Derived inputs: average of sources = sum of (source / n). Read from the smoothed values.
            foreach (var d in m.Inputs.Where(x => x.IsDerived))
            {
                var n = d.Average.Count;
                var target = DerivedParam(d.Name);
                for (int k = 0; k < n; k++)
                {
                    var src = d.Average[k];
                    var lo = u.ParamClip($"FT/Smoothing/{mode}/{d.Name}/{src}/-1", new[] { (target, -1f / n) });
                    var hi = u.ParamClip($"FT/Smoothing/{mode}/{d.Name}/{src}/1", new[] { (target, 1f / n) });
                    var t = u.NewTree($"FT/Smoothing/{mode}/{d.Name}/{src}", BlendTreeType.Simple1D, InputParam(src));
                    t.AddChild(lo, -1);
                    t.AddChild(hi, 1);
                    AnimUtil.AddDirectChild(root, t, One);
                }
            }
            return root;
        }

        // ---- FT/Gate

        void Gate()
        {
            var layer = u.NewLayer("FT/Gate");
            Layers.Add(layer);
            var sm = layer.stateMachine;
            var ft = m.FaceTrackParameter;
            var allAfk = afkGroups.SelectMany(g => g.Parameters).Distinct().ToList();

            var off = AnimUtil.State(sm, "Off", u.ParamClip("FT/Gate/Off", new[] { (Active, 0f), (MouthActive, 0f) }), new Vector3(300, 0));
            var on = AnimUtil.State(sm, "On", u.ParamClip("FT/Gate/On", new[] { (Active, 1f), (MouthActive, 1f) }), new Vector3(600, 0));
            var talking = AnimUtil.State(sm, "Talking", u.ParamClip("FT/Gate/Talking", new[] { (Active, 1f), (MouthActive, 0f) }), new Vector3(900, 0));
            sm.defaultState = off;
            // No Tracking Control on the eyes, whatever stopAutoEyes says: with Eyes & Eyelid on Animation, VRChat
            // forwards the face tracking eye keys as 0, and the eye inputs stop.
            var mute = m.Layers.TryGetValue("fx", out var fxAdj) ? fxAdj.MuteWhileFaceTrack : new List<string>();
            LayerWeights(on, mute, true);
            LayerWeights(off, mute, false);

            // Into AFK: longer waits first, so the stricter group wins when several are on.
            var groups = afkGroups.Select((g, i) => (g, i)).OrderByDescending(x => x.g.Wait).ThenBy(x => x.i).ToList();
            var y = 150;
            var stricter = new List<string>();
            foreach (var (g, _) in groups)
            {
                var label = W(g.Wait);
                var afkOff = AnimUtil.State(sm, "AfkOff/" + label, u.ParamClip("FT/Gate/AfkOff/" + label, new[] { (Active, 0f), (MouthActive, 0f) }), new Vector3(300, y));
                LayerWeights(afkOff, mute, false);
                foreach (var p in g.Parameters)
                {
                    var t = AnimUtil.AnyTransition(sm, afkOff);
                    u.When(t, p, true);
                    // Only when no group with a longer wait is on; otherwise the two AfkOff states would flip.
                    foreach (var q in stricter) u.When(t, q, false);
                }
                stricter.AddRange(g.Parameters.Where(p => !stricter.Contains(p)));

                AnimatorStateTransition Leave(AnimatorState to)
                {
                    var t = AnimUtil.Transition(afkOff, to);
                    foreach (var p in g.Parameters) u.When(t, p, false);
                    return t;
                }
                if (g.Wait <= 0)
                {
                    var toOn = Leave(on);
                    u.When(toOn, ft, true);
                    WhenSource(toOn, true);
                    u.When(Leave(off), ft, false);
                    WhenSource(Leave(off), false);
                }
                else
                {
                    var wait = AnimUtil.State(sm, "Wait/" + label, u.ParamClip("FT/Gate/Wait/" + label, new[] { (Active, 0f), (MouthActive, 0f) }, g.Wait), new Vector3(600, y));
                    Leave(wait);
                    AnimatorStateTransition After(AnimatorState to)
                    {
                        var t = AnimUtil.Transition(wait, to);
                        t.hasExitTime = true;
                        t.exitTime = 1;
                        return t;
                    }
                    var toOn = After(on);
                    u.When(toOn, ft, true);
                    WhenSource(toOn, true);
                    u.When(After(off), ft, false);
                    WhenSource(After(off), false);
                }
                y += 100;
            }

            // FaceTrack off (and no AFK): from anywhere, without waiting (R16)
            var toOff = AnimUtil.AnyTransition(sm, off);
            u.When(toOff, ft, false);
            foreach (var p in allAfk) u.When(toOff, p, false);
            // No face tracking source (and no AFK): the same, from anywhere (R8)
            var noSource = AnimUtil.AnyTransition(sm, off);
            WhenSource(noSource, false);
            foreach (var p in allAfk) u.When(noSource, p, false);

            var offOn = AnimUtil.Transition(off, on);
            u.When(offOn, ft, true);
            WhenSource(offOn, true);
            foreach (var p in allAfk) u.When(offOn, p, false);

            AnimUtil.Transition(on, talking).AddCondition(AnimatorConditionMode.Greater, TalkThreshold, "Voice");
            AnimUtil.Transition(talking, on).AddCondition(AnimatorConditionMode.Less, TalkThreshold, "Voice");
        }

        // Animator Layer Control on the FX layers in `names`: weight 0 when muted, else their default weight.
        void LayerWeights(AnimatorState s, List<string> names, bool muted)
        {
            var layers = u.Controller.layers;
            foreach (var name in names)
            {
                var i = Array.FindIndex(layers, l => l.name == name);
                if (i < 0) throw new TableLoader.TableException($"layer '{name}' to mute while FT is on is not in {u.Controller.name}");
                var lc = s.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                lc.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                lc.layer = i;
                lc.goalWeight = muted ? 0 : layers[i].defaultWeight;
                lc.blendDuration = 0;
            }
        }

        static string W(float seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- FT/Blink

        // The generated avatar's Eye Look has no eyelids with stopAutoEyes (Generator.Assign), so the generator blinks by itself
        // while FT is off. Needs both eye-closing parts.
        bool Blinks => m.StopAutoEyes && m.Parts.ContainsKey("eye.close_L") && m.Parts.ContainsKey("eye.close_R");

        void BlinkLayer()
        {
            var layer = u.NewLayer("FT/Blink");
            Layers.Add(layer);
            var sm = layer.stateMachine;
            var open = AnimUtil.State(sm, "Open", u.CurveClip("FT/Blink/Open", Blink, true, (0, 0), (BlinkInterval, 0)), new Vector3(300, 0));
            var close = AnimUtil.State(sm, "Close", u.CurveClip("FT/Blink/Close", Blink, false, (0, 0), (BlinkLength * 0.4f, 1), (BlinkLength, 0)), new Vector3(600, 0));
            sm.defaultState = open;
            // Exit time below 1 on a looping clip: checked again on every loop while FT is on.
            var toClose = AnimUtil.Transition(open, close);
            toClose.hasExitTime = true;
            toClose.exitTime = 0.99f;
            toClose.AddCondition(AnimatorConditionMode.Less, 0.5f, Active);
            var back = AnimUtil.Transition(close, open);
            back.hasExitTime = true;
            back.exitTime = 1;
        }

        // ---- FT/Switch/*

        void Switch(SwitchDef sw)
        {
            var layer = u.NewLayer("FT/Switch/" + sw.Name);
            Layers.Add(layer);
            var sm = layer.stateMachine;
            var states = new Dictionary<string, AnimatorState>();
            for (int k = 0; k < sw.States.Count; k++)
            {
                var name = sw.States[k];
                var values = sw.States.Select(s => (SwitchParam(sw.Name, s), s == name ? 1f : 0f));
                states[name] = AnimUtil.State(sm, name, u.ParamClip($"FT/Switch/{sw.Name}/{name}", values), new Vector3(300 + 250 * k, 0));
            }
            sm.defaultState = states[sw.States[0]];
            foreach (var t in sw.Transitions)
            {
                var tr = AnimUtil.Transition(states[t.From], states[t.To], t.Duration);
                foreach (var c in t.When)
                    tr.AddCondition(c.Op == ">" ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, c.Value, InputParam(c.Input));
            }
        }

        // ---- FT/Face

        void Face()
        {
            var layer = u.NewLayer("FT/Face");
            Layers.Add(layer);
            var talkRegions = new HashSet<string>(m.StopWhileTalking);
            var root = u.NewTree("FT/Face", BlendTreeType.Direct);
            foreach (var (group, weight, inGroup) in new (string, string, Func<string, bool>)[]
                     {
                         (FaceGroups[0], Active, r => !talkRegions.Contains(r)),
                         (FaceGroups[1], MouthActive, r => talkRegions.Contains(r)),
                     })
            {
                var groupTree = u.NewTree(FaceMotion(group), BlendTreeType.Direct);
                var any = false;
                foreach (var tree in m.Trees)
                {
                    var motion = Convert(tree.Root, FaceMotion(group, tree.Name), inGroup);
                    if (motion == null) continue;
                    AnimUtil.AddDirectChild(groupTree, motion, One);
                    any = true;
                }
                if (any) AnimUtil.AddDirectChild(root, groupTree, weight);
            }
            if (Blinks)
            {
                var l = m.Parts["eye.close_L"];
                var r = m.Parts["eye.close_R"];
                AnimUtil.AddDirectChild(root, u.ShapeClip("FT/Face/Blink", new[] { (l.Mesh, l.Shape, 100f * l.Scale), (r.Mesh, r.Shape, 100f * r.Scale) }), Blink);
            }
            AnimUtil.State(layer.stateMachine, "Face", root, new Vector3(300, 0));
        }

        static string Region(string part) => part.Substring(0, part.IndexOf('.'));

        static bool HasParts(Node n, Func<string, bool> inGroup) => n.PartNames().Any(k => inGroup(Region(k)));

        // Converts a node to a motion with only the parts of the region group. Null when nothing is left.
        Motion Convert(Node n, string path, Func<string, bool> inGroup)
        {
            if (!HasParts(n, inGroup)) return null;
            switch (n)
            {
                case PoseNode p:
                    return u.ShapeClip(path, p.Parts.Where(kv => inGroup(Region(kv.Key)))
                        .Select(kv => (m.Parts[kv.Key].Mesh, m.Parts[kv.Key].Shape, kv.Value)));
                case Blend1DNode b:
                {
                    var bt = u.NewTree(path, BlendTreeType.Simple1D, InputParam(b.Input));
                    for (int k = 0; k < b.Children.Count; k++)
                        bt.AddChild(Convert(b.Children[k].node, $"{path}/{k}", inGroup) ?? Empty(path, k), b.Children[k].threshold);
                    return bt;
                }
                case Blend2DNode b:
                {
                    var bt = u.NewTree(path, BlendTreeType.FreeformCartesian2D, InputParam(b.InputX), InputParam(b.InputY));
                    for (int k = 0; k < b.Children.Count; k++)
                        bt.AddChild(Convert(b.Children[k].node, $"{path}/{k}", inGroup) ?? Empty(path, k), new Vector2(b.Children[k].x, b.Children[k].y));
                    return bt;
                }
                case SwitchNode s:
                {
                    var bt = u.NewTree(path, BlendTreeType.Direct);
                    foreach (var c in s.Cases)
                    {
                        var motion = Convert(c.node, $"{path}/{c.state}", inGroup);
                        if (motion != null) AnimUtil.AddDirectChild(bt, motion, SwitchParam(s.Switch, c.state));
                    }
                    return bt;
                }
            }
            throw new InvalidOperationException("unknown node");
        }

        Motion Empty(string path, int k) => u.EmptyClip($"{path}/{k}");
    }
}
