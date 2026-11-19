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
    // Adds the FT layers to the FX controller (design.md "状態遷移機の Animator"):
    //   FT/Gate       FT/Active and FT/MouthActive from FaceTrack, AFK, Voice and UserFaceTracking
    //   FT/SM/*       one layer per state machine; each state writes FT/Sw/<state machine>/<state>. Transitions on the
    //                 inputs, and Menu/<state> states entered from the menu (FT/Menu/<state machine>)
    //   FT/Face       the states' fixed amounts: one Direct BlendTree, weighted by FT/Sw/* (the only layer that writes blendshapes)
    //   FT/Sets/*     weights of the Face Tracking section's Blendshapes Sets (NativeSets)
    //   FT/Visemes    weight of the viseme sets, 0 in states with stopVisemes (VisemeStop)
    public sealed class FtBuilder
    {
        public const string One = "FT/One";
        public const string Active = "FT/Active";
        public const string MouthActive = "FT/MouthActive";
        public const float TalkThreshold = 0.1f;

        readonly TableModel m;
        readonly AnimUtil u;
        readonly List<AfkGroup> afkGroups;
        readonly VisemeStop.Plan visemes;
        public readonly List<AnimatorControllerLayer> Layers = new List<AnimatorControllerLayer>();

        public FtBuilder(TableModel model, AnimatorController fx, List<AfkGroup> afkGroups, VisemeStop.Plan visemes = null)
        {
            m = model;
            u = new AnimUtil(fx);
            this.afkGroups = afkGroups;
            this.visemes = visemes;
        }

        public static string SwitchParam(string sm, string state) => "FT/Sw/" + sm + "/" + state;
        // The Int parameter of the menu: 0 when nothing is chosen, else the chosen state's StateMachineDef.MenuValue.
        public static string MenuParam(string sm) => "FT/Menu/" + sm;
        // Names in the FT/Face Direct BlendTree: a group ("Active" or "Mouth"), and a state's fixed amounts in that group.
        public static readonly string[] FaceGroups = { "Active", "Mouth" };  // amounts kept while talking, and those of stopWhileTalking's regions (not for a stopVisemes state)
        public static string FaceMotion(string group) => "FT/Face/" + group;
        public static string FaceMotion(string group, string sm, string state) => FaceMotion(group) + "/" + sm + "/" + state;

        // The parameter that conditions read for an input.
        public string InputParam(string name)
        {
            var i = m.Input(name) ?? throw new InvalidOperationException("unknown input " + name);
            if (i.IsTrack) throw new TableLoader.TableException($"input {name} is a tracking key: the Animator cannot read it (a condition's key is forwarded as FT/In/<key>)");
            return i.Parameter;
        }

        // VRChat's built-in parameter: true while a face tracking source (Selfie Expression, OSC face tracking) is active.
        // FT runs only while it is true (R8).
        public const string SourceParameter = "UserFaceTracking";

        // The condition on the face tracking source, in one place so that it can be changed alone.
        void WhenSource(AnimatorStateTransition t, bool present) => u.When(t, SourceParameter, present);

        // Whether the FX needs VRMode: only when the expression table writes something per device (transitions,
        // strengths, stopVisemes). Without it the FX is the same as before devices could be written.
        public static bool UsesSide(TableModel model) => model.SplitsByDevice;

        // State machines that have states in the menu (they get FT/Menu/<name> and a submenu).
        public static IEnumerable<StateMachineDef> WithMenu(TableModel model) => model.StateMachines.Where(sm => sm.MenuStates.Any());

        public void Build()
        {
            Parameters();
            Gate();
            foreach (var sm in m.StateMachines) StateMachine(sm);
            if (DeviceWeighted) DeviceLayer();
            Face();
            SetLayers();
            VisemeStop.BuildLayer(u, visemes, Layers);
        }

        void Parameters()
        {
            u.EnsureParameter(One, AnimatorControllerParameterType.Float, 1);
            u.EnsureParameter(Active, AnimatorControllerParameterType.Float);
            u.EnsureParameter(MouthActive, AnimatorControllerParameterType.Float);
            u.EnsureParameter(m.FaceTrackParameter, AnimatorControllerParameterType.Bool, 1);
            u.EnsureParameter("IsLocal", AnimatorControllerParameterType.Bool);
            u.EnsureParameter("Voice", AnimatorControllerParameterType.Float);
            u.EnsureParameter(SourceParameter, AnimatorControllerParameterType.Bool);
            foreach (var p in afkGroups.SelectMany(g => g.Parameters))
                u.EnsureParameter(p, p == AfkBuilder.Playing ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool);
            if (UsesSide(m)) u.EnsureParameter(NativeSets.SideParameter, AnimatorControllerParameterType.Int);
            foreach (var i in NativeFtWriter.AnimatorInputs(m))
                u.EnsureParameter(m.Input(i).Parameter, AnimatorControllerParameterType.Float);
            foreach (var sm in m.StateMachines)
            {
                for (int k = 0; k < sm.States.Count; k++)
                    u.EnsureParameter(SwitchParam(sm.Name, sm.States[k].Name), AnimatorControllerParameterType.Float, k == 0 ? 1 : 0);
                if (sm.MenuStates.Any()) u.EnsureParameter(MenuParam(sm.Name), AnimatorControllerParameterType.Int);
            }
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
                if (i < 0) continue;   // not in the controller: LayerPatcher logs a notice
                var lc = s.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                lc.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
                lc.layer = i;
                lc.goalWeight = muted ? 0 : layers[i].defaultWeight;
                lc.blendDuration = 0;
            }
        }

        static string W(float seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- FT/SM/*

        // A state for each state, and a Menu/<state> state for each state in the menu, with the same clip. Transitions
        // on the inputs run only while the menu is 0. Any State goes to Menu/<state> when the menu chooses it, and
        // Menu/<state> goes back to the default state when the menu is 0 again (design.md "状態遷移機の Animator").
        void StateMachine(StateMachineDef sm)
        {
            var layer = u.NewLayer("FT/SM/" + sm.Name);
            Layers.Add(layer);
            var asm = layer.stateMachine;
            var menu = sm.MenuStates.Any();
            var menuParam = MenuParam(sm.Name);
            var clips = new Dictionary<string, Motion>();
            var states = new Dictionary<string, AnimatorState>();
            for (int k = 0; k < sm.States.Count; k++)
            {
                var name = sm.States[k].Name;
                var values = sm.States.Select(s => (SwitchParam(sm.Name, s.Name), s.Name == name ? 1f : 0f));
                clips[name] = u.ParamClip($"FT/SM/{sm.Name}/{name}", values);
                states[name] = AnimUtil.State(asm, name, clips[name], new Vector3(300 + 250 * k, 0));
            }
            asm.defaultState = states[sm.States[0].Name];
            foreach (var t in sm.Transitions)
            {
                // A transition for one device runs only on it: VRMode > 0.5 (VR) or < 0.5 (desktop).
                var tr = AnimUtil.Transition(states[t.From], states[t.To], t.Duration);
                foreach (var c in t.When)
                    tr.AddCondition(c.Op == ">" ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, c.Value, InputParam(c.Input));
                if (t.Device != null) tr.AddCondition(t.Device == Device.Desktop ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, 0.5f, NativeSets.SideParameter);
                if (menu) tr.AddCondition(AnimatorConditionMode.Equals, 0, menuParam);
            }
            if (!menu) return;
            var dur = sm.MaxDuration;
            var x = 300;
            foreach (var st in sm.MenuStates)
            {
                var ms = AnimUtil.State(asm, "Menu/" + st.Name, clips[st.Name], new Vector3(x, 200));
                x += 250;
                var into = AnimUtil.AnyTransition(asm, ms, dur);
                into.canTransitionToSelf = false;
                into.AddCondition(AnimatorConditionMode.Equals, sm.MenuValue(st.Name), menuParam);
                AnimUtil.Transition(ms, asm.defaultState, dur).AddCondition(AnimatorConditionMode.Equals, 0, menuParam);
            }
        }

        // ---- FT/Device: VRMode (an Int) as two Float weights for FT/Face, which a Direct blend tree can use. Only when a
        // state stops the visemes on one device (FT/Visemes stops them on that device only).

        public static string DeviceWeight(bool desktop) => "FT/Device/" + Device.Of(desktop);

        bool DeviceWeighted => visemes != null && visemes.Any && visemes.States.Any(x => x.st.StopVisemesByDevice);

        void DeviceLayer()
        {
            foreach (var desktop in new[] { false, true }) u.EnsureParameter(DeviceWeight(desktop), AnimatorControllerParameterType.Float, desktop ? 0 : 1);
            var layer = u.NewLayer("FT/Device");
            Layers.Add(layer);
            var sm = layer.stateMachine;
            var vr = AnimUtil.State(sm, Device.Vr, u.ParamClip("FT/Device/" + Device.Vr, new[] { (DeviceWeight(false), 1f), (DeviceWeight(true), 0f) }), new Vector3(300, 0));
            var desk = AnimUtil.State(sm, Device.Desktop, u.ParamClip("FT/Device/" + Device.Desktop, new[] { (DeviceWeight(false), 0f), (DeviceWeight(true), 1f) }), new Vector3(600, 0));
            sm.defaultState = vr;
            AnimUtil.Transition(vr, desk).AddCondition(AnimatorConditionMode.Less, 0.5f, NativeSets.SideParameter);
            AnimUtil.Transition(desk, vr).AddCondition(AnimatorConditionMode.Greater, 0.5f, NativeSets.SideParameter);
        }

        // ---- FT/Face

        // The states' fixed amounts (already multiplied by the part's scale), each weighted by its state's FT/Sw/*.
        // A state whose visemes are stopped (stopVisemes, and VisemeStop could stop them for this avatar) keeps all its
        // fixed amounts while talking (the Active group), also those in stopWhileTalking's regions: the mouth keeps the
        // state's shape. When the visemes cannot be stopped, the state is as without stopVisemes. A state that stops them on
        // one device only does so on that device: its stopWhileTalking regions' amounts go into both groups, each under a
        // tree weighted by the device's FT/Device/* (the Active group for the device that stops, Mouth for the other).
        void Face()
        {
            var talkRegions = new HashSet<string>(m.StopWhileTalking);
            var stopping = visemes != null && visemes.Any ? visemes.States.Select(x => x.st).ToList() : new List<StateDef>();
            // null: the state stops on both devices (or on none: not in the list); else the device it stops on.
            bool Stops(StateDef st, bool? desktop) => stopping.Contains(st) && (desktop == null ? !st.StopVisemesByDevice : st.StopVisemes(desktop.Value));
            var byDevice = DeviceWeighted;
            var root = u.NewTree("FT/Face", BlendTreeType.Direct);
            var any = false;
            // inGroup(state, region, device): device null for the amounts of every device.
            foreach (var (group, weight, inGroup) in new (string, string, Func<StateDef, string, bool?, bool>)[]
                     {
                         (FaceGroups[0], Active, (st, r, d) => !talkRegions.Contains(r) ? d == null : d == null ? Stops(st, null) : st.StopVisemesByDevice && Stops(st, d)),
                         (FaceGroups[1], MouthActive, (st, r, d) => talkRegions.Contains(r) && (d == null ? !stopping.Contains(st) : st.StopVisemesByDevice && stopping.Contains(st) && !Stops(st, d))),
                     })
            {
                var groupTree = u.NewTree(FaceMotion(group), BlendTreeType.Direct);
                var groupAny = false;
                foreach (var device in byDevice ? new bool?[] { null, false, true } : new bool?[] { null })
                {
                    var tree = device == null ? groupTree : u.NewTree(FaceMotion(group) + "/" + Device.Of(device.Value), BlendTreeType.Direct);
                    var treeAny = false;
                    foreach (var sm in m.StateMachines)
                        foreach (var st in sm.States)
                        {
                            var parts = st.Fixed.Where(kv => inGroup(st, PartNames.Region(kv.Key), device)).ToList();
                            if (parts.Count == 0) continue;
                            var name = FaceMotion(group, sm.Name, st.Name) + (device == null ? "" : "/" + Device.Of(device.Value));
                            var clip = u.PartClip(name, parts.Select(kv => (m.Parts[kv.Key], kv.Value)));
                            AnimUtil.AddDirectChild(tree, clip, SwitchParam(sm.Name, st.Name));
                            treeAny = true;
                        }
                    if (!treeAny) continue;
                    if (device != null) AnimUtil.AddDirectChild(groupTree, tree, DeviceWeight(device.Value));
                    groupAny = true;
                }
                if (groupAny) { AnimUtil.AddDirectChild(root, groupTree, weight); any = true; }
            }
            if (!any) return;
            var layer = u.NewLayer("FT/Face");
            Layers.Add(layer);
            AnimUtil.State(layer.stateMachine, "Face", root, new Vector3(300, 0));
        }

        // ---- FT/Sets/*: one layer per group of Blendshapes Sets with the same conditions. On sets their weights to 1,
        // Off to 0 (VRC Animator Face Tracking Blendshapes Set). On needs every condition; any condition failing goes Off.

        public const int MaxSetsPerBehavior = 64;

        void SetLayers()
        {
            var groups = NativeSets.Build(m).GroupBy(x => x.Signature).ToList();
            for (int g = 0; g < groups.Count; g++)
            {
                var sets = groups[g].ToList();
                var conds = sets[0].Conditions;
                var layer = u.NewLayer("FT/Sets/" + g);
                Layers.Add(layer);
                var sm = layer.stateMachine;
                var empty = u.EmptyClip($"FT/Sets/{g}/Empty");
                var off = AnimUtil.State(sm, "Off", empty, new Vector3(300, 0));
                var on = AnimUtil.State(sm, "On", empty, new Vector3(600, 0));
                sm.defaultState = off;
                WeightBehaviours(off, sets, 0, sets[0].LerpSpeed);
                WeightBehaviours(on, sets, 1, sets[0].LerpSpeed);
                var toOn = AnimUtil.Transition(off, on);
                foreach (var c in conds) toOn.AddCondition(c.High ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, c.Param);
                foreach (var c in conds)
                    AnimUtil.Transition(on, off).AddCondition(c.High ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, 0.5f, c.Param);
            }
        }

        static void WeightBehaviours(AnimatorState s, List<NativeSets.SetDef> sets, float weight, float lerp)
        {
            for (int k = 0; k < sets.Count; k += MaxSetsPerBehavior)
            {
                var b = s.AddStateMachineBehaviour<VRCAnimatorFaceTrackingBlendshapesSet>();
                b.blendSets = sets.Skip(k).Take(MaxSetsPerBehavior).Select(x => new VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet
                {
                    blendshapesSetName = x.Name,
                    type = VRC_AnimatorFaceTrackingBlendshapesSet.SourceType.Value,
                    weight = weight,
                    lerpSpeed = lerp,
                }).ToList();
            }
        }
    }
}
