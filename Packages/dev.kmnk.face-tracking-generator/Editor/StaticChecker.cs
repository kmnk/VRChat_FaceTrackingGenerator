using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // Static checks of the tables and the generated controller (spec R24).
    public static class StaticChecker
    {
        public static List<string> Check(TableModel m, AnimatorController fx)
        {
            var f = new List<string>();
            f.AddRange(m.Warnings.Select(w => "table: " + w));
            Asymmetry(m, f);
            MissingShapes(m, f);
            Unused(m, f);
            Sums(m, f);
            if (fx != null)
            {
                WriteDefaults(fx, f);
                UnusedClips(fx, f);
                Stops(m, fx, f);
            }
            return f;
        }

        // ---- the Face Tracking section of a generated avatar (R2): one Forward to Animator behavior per input with
        // its expression, and no warnings from the SDK's check. `what` names the avatar in the findings.
        public static List<string> FaceTracking(TableModel m, VRCAvatarDescriptor d, string what)
        {
            var f = new List<string>();
            if (!d.enableFaceTracking || !d.useAllTrackingKeys)
                f.Add($"face tracking: {what}: Face Tracking or Use All Tracking Keys is off (generate again)");
            var behaviors = d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
            foreach (var i in m.Inputs.Where(x => !x.IsDerived))
            {
                var b = behaviors.Where(x => x != null && x.outputParameterName == i.Parameter).ToList();
                if (b.Count != 1 || b[0].targetMode != VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                    || b[0].combineMode != VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom || b[0].customExpression != i.Expression)
                    f.Add($"face tracking: {what}: no Forward to Animator behavior for {i.Parameter} with \"{i.Expression}\" (generate again)");
            }
            foreach (var w in NativeFtWriter.Check(d)) f.Add($"face tracking: {what}: SDK: " + w.Replace("\n", " "));
            return f;
        }

        // ---- 1. left/right pairs: the same structure and values once the side words are swapped

        static readonly (string left, string right, string mark)[] SideWords = { ("Left", "Right", "{SIDE}"), ("_L", "_R", "_{S}") };

        static string Normalize(string s, bool isLeft)
        {
            foreach (var (l, r, mark) in SideWords) s = s.Replace(isLeft ? l : r, mark);
            return s;
        }

        static void Asymmetry(TableModel m, List<string> f)
        {
            void Pairs<T>(IEnumerable<T> items, Func<T, string> name, Func<T, JToken> json, string what)
            {
                var list = items.ToList();
                foreach (var a in list)
                {
                    var an = name(a);
                    if (!an.Contains("Left")) continue;
                    var b = list.FirstOrDefault(x => name(x) == an.Replace("Left", "Right"));
                    if (b == null) { f.Add($"left/right: {what} {an} has no right side"); continue; }
                    var ja = Normalize(json(a).ToString(), true);
                    var jb = Normalize(json(b).ToString(), false);
                    if (ja != jb) f.Add($"left/right: {what} {an} and {name(b)} differ ({FirstDiff(ja, jb)})");
                }
            }
            // Trees are compared as written (before StopWrap); the stops are compared with their switch.
            Pairs(m.Switches, s => s.Name, s => SwitchJson(s, m), "switch");
            Pairs(m.Trees, t => t.Name, t => TreeJson(t.BaseRoot ?? t.Root), "tree");
            Pairs(m.Inputs.Where(i => !i.IsDerived), i => i.Name, i => new JObject { ["key"] = i.Key, ["smoothing"] = i.Smoothing }, "input");
        }

        static string FirstDiff(string a, string b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n');
            for (int i = 0; i < Math.Min(la.Length, lb.Length); i++)
                if (la[i] != lb[i]) return la[i].Trim() + " vs " + lb[i].Trim();
            return "different length";
        }

        static JToken SwitchJson(SwitchDef s, TableModel m) => new JObject
        {
            ["states"] = new JArray(s.States),
            ["transitions"] = new JArray(s.Transitions.Select(t => new JArray(t.From, t.To, t.Duration,
                new JArray(t.When.Select(c => new JArray(c.Input, c.Op, c.Value)))))),
            ["stops"] = new JArray(m.Stops.Where(x => x.Switch == s.Name).Select(x => new JArray(x.State, x.Tree))),
        };

        static JToken TreeJson(Node n)
        {
            switch (n)
            {
                case PoseNode p: return new JObject(p.Parts.Select(kv => new JProperty(kv.Key, kv.Value)));
                case Blend1DNode b: return new JObject { ["1d"] = b.Input, ["c"] = new JArray(b.Children.Select(c => new JArray(c.threshold, TreeJson(c.node)))) };
                case Blend2DNode b: return new JObject { ["2d"] = new JArray(b.InputX, b.InputY), ["c"] = new JArray(b.Children.Select(c => new JArray(c.x, c.y, TreeJson(c.node)))) };
                case SwitchNode s: return new JObject { ["switch"] = s.Switch, ["c"] = new JArray(s.Cases.Select(c => new JArray(c.state, TreeJson(c.node)))) };
            }
            return JValue.CreateNull();
        }

        // ---- 2. blendshapes the avatar mesh does not have

        static void MissingShapes(TableModel m, List<string> f)
        {
            var path = !string.IsNullOrEmpty(m.Prefab?.Guid) ? AssetDatabase.GUIDToAssetPath(m.Prefab.Guid) : m.Prefab?.Path;
            if (!string.IsNullOrEmpty(m.Prefab?.Guid) && m.Prefab.Path != null && path != m.Prefab.Path)
                f.Add($"asset: prefab path in the table is {m.Prefab.Path}, but the guid points to {path}");
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { f.Add("asset: prefab not found: " + path); return; }
            var used = new HashSet<string>();
            foreach (var t in m.Trees) used.UnionWith(t.Root.PartNames());
            foreach (var part in used.OrderBy(x => x, StringComparer.Ordinal))
            {
                var target = m.Parts[part];
                var tr = prefab.transform.Find(target.Mesh);
                var smr = tr != null ? tr.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null || smr.sharedMesh == null) { f.Add($"avatar: part {part}: no SkinnedMeshRenderer at '{target.Mesh}'"); continue; }
                if (smr.sharedMesh.GetBlendShapeIndex(target.Shape) < 0)
                    f.Add($"avatar: part {part}: mesh '{target.Mesh}' has no blendshape '{target.Shape}'");
            }
        }

        // ---- 3. smoothing and inputs that nothing uses (D1)

        static void Unused(TableModel m, List<string> f)
        {
            var usedInputs = new HashSet<string>();
            foreach (var n in m.Trees.SelectMany(t => t.Root.SelfAndDescendants()))
            {
                if (n is Blend1DNode b1) usedInputs.Add(b1.Input);
                else if (n is Blend2DNode b2) { usedInputs.Add(b2.InputX); usedInputs.Add(b2.InputY); }
            }
            foreach (var s in m.Switches) foreach (var t in s.Transitions) foreach (var c in t.When) usedInputs.Add(c.Input);
            foreach (var d in m.Inputs.Where(i => i.IsDerived && usedInputs.Contains(i.Name))) foreach (var src in d.Average) usedInputs.Add(src);
            foreach (var i in m.Inputs.Where(i => i.Smoothing != null && !usedInputs.Contains(i.Name)))
                f.Add($"unused: input {i.Name} is smoothed but no tree or switch uses it");
            foreach (var s in m.Smoothings.Where(s => m.Inputs.All(i => i.Smoothing != s.Name)))
                f.Add($"unused: smoothing {s.Name} is not used by any input");
            var usedSwitches = new HashSet<string>(m.Trees.SelectMany(t => t.Root.SelfAndDescendants()).OfType<SwitchNode>().Select(s => s.Switch));
            foreach (var s in m.Switches.Where(s => !usedSwitches.Contains(s.Name)))
                f.Add($"unused: switch {s.Name} is not used by any tree");
        }

        // ---- 6. sums over 100: the largest value each tree can give a blendshape, added over trees

        static void Sums(TableModel m, List<string> f)
        {
            var total = new SortedDictionary<string, float>(StringComparer.Ordinal);
            var from = new Dictionary<string, List<string>>();
            foreach (var t in m.Trees)
            {
                var max = new Dictionary<string, float>();
                MaxValues(t.Root, max);
                foreach (var kv in max)
                {
                    var shape = m.Parts[kv.Key].Mesh + "/" + m.Parts[kv.Key].Shape;
                    total[shape] = (total.TryGetValue(shape, out var v) ? v : 0) + kv.Value;
                    if (!from.ContainsKey(shape)) from[shape] = new List<string>();
                    from[shape].Add(t.Name);
                }
            }
            foreach (var kv in total.Where(kv => kv.Value > 100 + 1e-3f))
                f.Add($"sum: blendshape {kv.Key} can reach {kv.Value:0.#} (trees: {string.Join(", ", from[kv.Key])})");
        }

        // In 1D and 2D BlendTrees the weights add up to 1 and a switch is in one state, so the largest value is the
        // largest leaf value.
        static void MaxValues(Node n, Dictionary<string, float> max)
        {
            foreach (var p in n.SelfAndDescendants().OfType<PoseNode>())
                foreach (var kv in p.Parts) max[kv.Key] = Math.Max(max.TryGetValue(kv.Key, out var v) ? v : 0, kv.Value);
        }

        // ---- 4. Write Defaults mixed in FX

        static void WriteDefaults(AnimatorController c, List<string> f)
        {
            var on = new List<string>(); var off = new List<string>();
            void Walk(AnimatorStateMachine sm, string layer)
            {
                foreach (var cs in sm.states) (cs.state.writeDefaultValues ? on : off).Add(layer + "/" + cs.state.name);
                foreach (var csm in sm.stateMachines) Walk(csm.stateMachine, layer);
            }
            foreach (var l in c.layers) Walk(l.stateMachine, l.name);
            if (on.Count > 0 && off.Count > 0)
                f.Add($"wd: {c.name} mixes Write Defaults: {on.Count} states on, {off.Count} off (off: {string.Join(", ", off.Take(5))}{(off.Count > 5 ? ", ..." : "")})");
        }

        // ---- 7. stops (the "stops" of a switch): in the FX, no clip of the tree's motion is reached while the switch
        // is in the stopping state. A child weighted by another state of that switch blocks the way; other weights do not.

        static void Stops(TableModel m, AnimatorController fx, List<string> f)
        {
            var live = m.Stops.Where(s => m.Switch(s.Switch)?.States.Contains(s.State) == true && m.Trees.Any(t => t.Name == s.Tree)).ToList();
            if (live.Count == 0) return;
            var face = fx.layers.FirstOrDefault(l => l.name == "FT/Face");
            var motions = new Dictionary<string, Motion>();
            void Collect(Motion mo)
            {
                if (mo == null) return;
                motions[mo.name] = mo;
                if (mo is BlendTree bt) foreach (var ch in bt.children) Collect(ch.motion);
            }
            if (face != null) foreach (var cs in face.stateMachine.states) Collect(cs.state.motion);
            bool Leaks(Motion mo, string sw, string state)
            {
                if (mo is BlendTree bt)
                {
                    var prefix = FtBuilder.SwitchParam(sw, "");
                    foreach (var ch in bt.children)
                    {
                        var p = bt.blendType == BlendTreeType.Direct ? ch.directBlendParameter : null;
                        if (p != null && p.StartsWith(prefix, StringComparison.Ordinal) && p != FtBuilder.SwitchParam(sw, state)) continue;
                        if (Leaks(ch.motion, sw, state)) return true;
                    }
                    return false;
                }
                return mo is AnimationClip clip && AnimationUtility.GetCurveBindings(clip).Length > 0;
            }
            foreach (var s in live)
            {
                var found = false;
                foreach (var group in FtBuilder.FaceGroups)
                {
                    if (!motions.TryGetValue(FtBuilder.FaceMotion(group, s.Tree), out var mo)) continue;
                    found = true;
                    if (Leaks(mo, s.Switch, s.State)) f.Add($"stops: {s.Tree} is not stopped by {s.Switch}/{s.State} in the FX");
                }
                if (!found) f.Add($"stops: {s.Tree} has no motion in the FX ({FtBuilder.FaceMotion("<group>", s.Tree)})");
            }
        }

        // ---- 3. clips stored in the generated controller that nothing reaches

        static void UnusedClips(AnimatorController c, List<string> f)
        {
            var reach = new HashSet<UnityEngine.Object>();
            void Motion(Motion mo)
            {
                if (mo == null || !reach.Add(mo)) return;
                if (mo is BlendTree bt) foreach (var ch in bt.children) Motion(ch.motion);
            }
            void Walk(AnimatorStateMachine sm)
            {
                foreach (var cs in sm.states) Motion(cs.state.motion);
                foreach (var csm in sm.stateMachines) Walk(csm.stateMachine);
            }
            foreach (var l in c.layers) Walk(l.stateMachine);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(c)).OfType<AnimationClip>())
                if (!reach.Contains(o)) f.Add($"unused: clip {o.name} in {c.name} is not used");
        }
    }
}
