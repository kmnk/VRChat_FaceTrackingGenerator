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
        public const int MaxSyncedBits = 256;

        public static List<string> Check(TableModel m, AnimatorController fx)
        {
            var f = new List<string>();
            f.AddRange(m.Warnings.Select(w => "table: " + w));
            Asymmetry(m, f);
            MissingShapes(m, f);
            Unused(m, f);
            Sums(m, f);
            SyncedBits(m, f);
            if (fx != null)
            {
                WriteDefaults(fx, f);
                UnusedClips(fx, f);
            }
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
            Pairs(m.Switches, s => s.Name, SwitchJson, "switch");
            Pairs(m.Trees, t => t.Name, t => TreeJson(t.Root), "tree");
            Pairs(m.Inputs.Where(i => !i.IsDerived), i => i.Name, i => new JObject { ["key"] = i.Key, ["smoothing"] = i.Smoothing, ["sync"] = i.Sync }, "input");
        }

        static string FirstDiff(string a, string b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n');
            for (int i = 0; i < Math.Min(la.Length, lb.Length); i++)
                if (la[i] != lb[i]) return la[i].Trim() + " vs " + lb[i].Trim();
            return "different length";
        }

        static JToken SwitchJson(SwitchDef s) => new JObject
        {
            ["states"] = new JArray(s.States),
            ["transitions"] = new JArray(s.Transitions.Select(t => new JArray(t.From, t.To, t.Duration,
                new JArray(t.When.Select(c => new JArray(c.Input, c.Op, c.Value)))))),
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
            foreach (var t in m.Trees) CollectParts(t.Root, used);
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

        static void CollectParts(Node n, HashSet<string> parts)
        {
            switch (n)
            {
                case PoseNode p: foreach (var k in p.Parts.Keys) parts.Add(k); break;
                case Blend1DNode b: foreach (var c in b.Children) CollectParts(c.node, parts); break;
                case Blend2DNode b: foreach (var c in b.Children) CollectParts(c.node, parts); break;
                case SwitchNode s: foreach (var c in s.Cases) CollectParts(c.node, parts); break;
            }
        }

        // ---- 3. smoothing and inputs that nothing uses (D1)

        static void Unused(TableModel m, List<string> f)
        {
            var usedInputs = new HashSet<string>();
            void Walk(Node n)
            {
                switch (n)
                {
                    case Blend1DNode b: usedInputs.Add(b.Input); foreach (var c in b.Children) Walk(c.node); break;
                    case Blend2DNode b: usedInputs.Add(b.InputX); usedInputs.Add(b.InputY); foreach (var c in b.Children) Walk(c.node); break;
                    case SwitchNode s: foreach (var c in s.Cases) Walk(c.node); break;
                }
            }
            foreach (var t in m.Trees) Walk(t.Root);
            foreach (var s in m.Switches) foreach (var t in s.Transitions) foreach (var c in t.When) usedInputs.Add(c.Input);
            foreach (var d in m.Inputs.Where(i => i.IsDerived && usedInputs.Contains(i.Name))) foreach (var src in d.Average) usedInputs.Add(src);
            foreach (var i in m.Inputs.Where(i => i.Smoothing != null && !usedInputs.Contains(i.Name)))
                f.Add($"unused: input {i.Name} is smoothed but no tree or switch uses it");
            foreach (var s in m.Smoothings.Where(s => m.Inputs.All(i => i.Smoothing != s.Name)))
                f.Add($"unused: smoothing {s.Name} is not used by any input");
            var usedSwitches = new HashSet<string>();
            void WalkSw(Node n)
            {
                switch (n)
                {
                    case Blend1DNode b: foreach (var c in b.Children) WalkSw(c.node); break;
                    case Blend2DNode b: foreach (var c in b.Children) WalkSw(c.node); break;
                    case SwitchNode s: usedSwitches.Add(s.Switch); foreach (var c in s.Cases) WalkSw(c.node); break;
                }
            }
            foreach (var t in m.Trees) WalkSw(t.Root);
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

        // In 1D and 2D BlendTrees the weights add up to 1, so the largest value is the largest leaf value.
        static void MaxValues(Node n, Dictionary<string, float> max)
        {
            void Merge(Dictionary<string, float> d) { foreach (var kv in d) max[kv.Key] = Math.Max(max.TryGetValue(kv.Key, out var v) ? v : 0, kv.Value); }
            switch (n)
            {
                case PoseNode p: Merge(p.Parts.ToDictionary(kv => kv.Key, kv => kv.Value)); break;
                case Blend1DNode b: foreach (var c in b.Children) { var d = new Dictionary<string, float>(); MaxValues(c.node, d); Merge(d); } break;
                case Blend2DNode b: foreach (var c in b.Children) { var d = new Dictionary<string, float>(); MaxValues(c.node, d); Merge(d); } break;
                case SwitchNode s: foreach (var c in s.Cases) { var d = new Dictionary<string, float>(); MaxValues(c.node, d); Merge(d); } break;
            }
        }

        // ---- 5. synced bits of the whole avatar

        static void SyncedBits(TableModel m, List<string> f)
        {
            var path = !string.IsNullOrEmpty(m.Prefab?.Guid) ? AssetDatabase.GUIDToAssetPath(m.Prefab.Guid) : m.Prefab?.Path;
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var expr = prefab != null ? prefab.GetComponent<VRCAvatarDescriptor>()?.expressionParameters : null;
            if (expr == null) return;
            var ftKeys = new HashSet<string>(m.Inputs.Where(i => !i.IsDerived).Select(i => i.Key));
            var bits = expr.parameters.Where(p => p.networkSynced && !string.IsNullOrEmpty(p.name) && !ftKeys.Contains(p.name) && !p.name.StartsWith("v2/"))
                .Sum(p => VRCExpressionParameters.TypeCost(p.valueType));
            bits += m.Inputs.Count(i => !i.IsDerived && i.Sync == "float8") * 8;
            if (bits > MaxSyncedBits) f.Add($"sync: synced parameters use {bits} bits (limit {MaxSyncedBits})");
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
