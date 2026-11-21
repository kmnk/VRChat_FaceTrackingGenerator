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
            MissingLayers(m, f);
            PropertyRows(m, f);
            Unused(m, f);
            States(m, f);
            f.AddRange(FtCost.Rough(NativeSets.Build(m)).Select(x => $"curve: {x.set} {x.key} ({x.part}): the expression differs from the curve by up to {x.err.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} (one expression of a blendshape is full)"));
            f.AddRange(Cost(m).findings);
            if (fx != null)
            {
                WriteDefaults(fx, f);
                UnusedClips(fx, f);
            }
            return f;
        }

        // What the Face Tracking section the tables make would cost, written into the table's prefab as generating
        // writes it (null when the prefab cannot be read: generating reports that), and the findings: over VRChat's
        // limits (generating stops) or over Excellent in Face Tracking Operations.
        public static (FtCost.Result cost, List<string> findings) Cost(TableModel m)
        {
            var f = new List<string>();
            var path = AssetRefs.Path(m.Prefab, "prefab");
            var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            var desc = prefab != null ? prefab.GetComponent<VRCAvatarDescriptor>() : null;
            if (desc == null) return (null, f);
            var plan = NativeFtWriter.Build(m);
            plan.Visemes = VisemeStop.Build(m, desc, new List<string>());
            var cost = FtCost.Predict(prefab, plan);
            var over = FtCost.Over(cost, plan);
            if (over != null) f.Add("face tracking: " + over);
            if (cost.OverExcellent) f.Add($"face tracking: {cost.Operations} Face Tracking Operations: the avatar is {cost.Rank}, not Excellent, in Face Tracking Operations");
            return (cost, f);
        }

        // ---- the Face Tracking section of a generated avatar (R2): one Forward to Animator behavior per input with
        // its expression, and no warnings from the SDK's check. `what` names the avatar in the findings.
        public static List<string> FaceTracking(TableModel m, VRCAvatarDescriptor d, string what)
        {
            var f = new List<string>();
            if (!d.enableFaceTracking || !d.useAllTrackingKeys)
                f.Add($"face tracking: {what}: Face Tracking or Use All Tracking Keys is off (generate again)");
            var behaviors = d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
            var used = NativeFtWriter.AnimatorInputs(m);
            foreach (var i in m.Inputs.Where(x => !x.IsTrack && used.Contains(x.Name)))
            {
                var b = behaviors.Where(x => x != null && x.outputParameterName == i.Parameter).ToList();
                if (b.Count != 1 || b[0].targetMode != VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                    || b[0].combineMode != VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom || b[0].customExpression != i.Expression)
                    f.Add($"face tracking: {what}: no Forward to Animator behavior for {i.Parameter} with \"{i.Expression}\" (generate again)");
            }
            Limits(d, what, f);
            foreach (var w in NativeFtWriter.Check(d)) f.Add($"face tracking: {what}: SDK: " + w.Replace("\n", " "));
            return f;
        }

        // ---- menus: every control's parameters are in the avatar's Expression Parameters (what the SDK's build panel
        // reports as "VRCExpressionMenu uses a parameter that is not defined"). `what` names the avatar.
        public static List<string> MenuParameters(VRCAvatarDescriptor d, string what)
        {
            var f = new List<string>();
            var defined = new HashSet<string>((d.expressionParameters?.parameters ?? new VRCExpressionParameters.Parameter[0]).Select(p => p.name));
            foreach (var (menu, c) in MenuControls(d.expressionsMenu))
                foreach (var p in new[] { c.parameter }.Concat(c.subParameters ?? new VRCExpressionsMenu.Control.Parameter[0]))
                    if (!string.IsNullOrEmpty(p?.name) && !defined.Contains(p.name))
                        f.Add($"menu: {what}: {menu.name}/{c.name} uses {p.name}, which is not in the Expression Parameters");
            return f;
        }

        // The menus of the avatar and what they hold, one line per menu.
        public static List<string> MenuSummary(VRCAvatarDescriptor d) =>
            MenuControls(d.expressionsMenu).GroupBy(x => x.menu).Select(g => $"{AssetDatabase.GetAssetPath(g.Key)}: " +
                string.Join(", ", g.Select(x => x.c.type == VRCExpressionsMenu.Control.ControlType.SubMenu ? $"[{x.c.name}]" : $"{x.c.name} ({x.c.parameter?.name})"))).ToList();

        static List<(VRCExpressionsMenu menu, VRCExpressionsMenu.Control c)> MenuControls(VRCExpressionsMenu root)
        {
            var r = new List<(VRCExpressionsMenu, VRCExpressionsMenu.Control)>();
            var seen = new HashSet<VRCExpressionsMenu>();
            void Walk(VRCExpressionsMenu m)
            {
                if (m == null || !seen.Add(m)) return;
                foreach (var c in m.controls) { r.Add((m, c)); if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu) Walk(c.subMenu); }
            }
            Walk(root);
            return r;
        }

        public const int MaxSets = 128, MaxBehaviors = 128, ExcellentOperations = 2000;

        // VRChat's limits and the Face Tracking Operations estimate (design.md "検査").
        public static void Limits(VRCAvatarDescriptor d, string what, List<string> f)
        {
            var sets = (d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>()).Where(x => x != null).ToList();
            var behaviors = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Where(x => x != null).ToList();
            if (sets.Count > MaxSets) f.Add($"face tracking: {what}: {sets.Count} Blendshapes Sets: over VRChat's limit of {MaxSets}");
            if (behaviors.Count > MaxBehaviors) f.Add($"face tracking: {what}: {behaviors.Count} Parameter Behaviors: over VRChat's limit of {MaxBehaviors}");
            var ops = Operations(d);
            if (ops > ExcellentOperations) f.Add($"face tracking: {what}: about {ops} Face Tracking Operations: over {ExcellentOperations}, the avatar is no longer Excellent");
            var forwarded = behaviors.Count(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator);
            if (forwarded > NativeFtWriter.ExcellentForwarded) f.Add($"face tracking: {what}: {forwarded} Forward to Animator behaviors: over {NativeFtWriter.ExcellentForwarded}, the avatar is no longer Excellent in Forwarded Parameters");
        }

        // Each tracking or viseme row counts once per Face Tracking Mesh that has its blendshape; each input of a
        // Parameter Behavior counts once (custom expressions: their inputs and operators are not counted here).
        public static int Operations(VRCAvatarDescriptor d)
        {
            var meshes = (d.faceTrackingMeshes ?? new List<UnityEngine.SkinnedMeshRenderer>()).Where(x => x != null && x.sharedMesh != null).ToList();
            int On(string shape) => string.IsNullOrEmpty(shape) ? 0 : meshes.Count(x => x.sharedMesh.GetBlendShapeIndex(shape) >= 0);
            var n = 0;
            foreach (var s in d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
            {
                if (s == null) continue;
                foreach (var x in s.faceTrackingBlendshapeNames ?? new string[0]) n += On(x);
                foreach (var x in s.visemeBlendshapeNames ?? new string[0]) n += On(x);
            }
            foreach (var b in d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>())
                if (b != null) n += (b.inputTrackingKeys?.Length ?? 0);
            return n;
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
            // Neutral rows: the row of a ...Left key and of its ...Right key, with the side words swapped.
            foreach (var kv in m.Neutral)
                Pairs(kv.Value, r => kv.Key + " " + r.Key, r => { var o = new JObject { ["part"] = r.Part, ["strength"] = ModelJson.Strength(r) }; var c = ModelJson.Curve(r); if (c != null) o["curve"] = c; return o; }, "neutral row");
        }

        static string FirstDiff(string a, string b)
        {
            var la = a.Split('\n'); var lb = b.Split('\n');
            for (int i = 0; i < Math.Min(la.Length, lb.Length); i++)
                if (la[i] != lb[i]) return la[i].Trim() + " vs " + lb[i].Trim();
            return "different length";
        }

        // ---- 2. blendshapes the avatar mesh does not have

        static void MissingShapes(TableModel m, List<string> f)
        {
            // A stale GUID is a warning of the generation log, not a finding: generation uses the path.
            var path = AssetRefs.Path(m.Prefab, "prefab");
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { f.Add("asset: prefab not found: " + path); return; }
            foreach (var part in m.UsedParts().OrderBy(x => x, StringComparer.Ordinal))
            {
                var target = m.Parts[part];
                var tr = prefab.transform.Find(target.Mesh);
                var smr = tr != null ? tr.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null || smr.sharedMesh == null) { f.Add($"avatar: part {part}: no SkinnedMeshRenderer at '{target.Mesh}'"); continue; }
                if (target.IsProperty)
                {
                    if (PropertyParts.Find(smr, target.Property) == null)
                        f.Add($"avatar: part {part}: renderer {target.Mesh} has no material property {target.Property}");
                    continue;
                }
                if (smr.sharedMesh.GetBlendShapeIndex(target.Shape) < 0)
                    f.Add($"avatar: part {part}: mesh '{target.Mesh}' has no blendshape '{target.Shape}'");
            }
        }

        // ---- 2a. layer names in the table's "layers" that the avatar's controllers do not have (generation leaves them out)
        static void MissingLayers(TableModel m, List<string> f)
        {
            var path = AssetRefs.Path(m.Prefab, "prefab");
            var desc = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<VRCAvatarDescriptor>();
            if (desc == null) return;   // MissingShapes reports a missing prefab
            foreach (var (type, key) in new[] { (VRCAvatarDescriptor.AnimLayerType.FX, "fx"), (VRCAvatarDescriptor.AnimLayerType.Gesture, "gesture"), (VRCAvatarDescriptor.AnimLayerType.Action, "action") })
            {
                if (!m.Layers.TryGetValue(key, out var adj)) continue;
                var layer = desc.baseAnimationLayers.FirstOrDefault(l => l.type == type);
                if (!(layer.animatorController is AnimatorController c)) continue;
                foreach (var (list, name) in LayerPatcher.MissingIn(c, adj))
                    f.Add($"avatar: layers.{key}.{list}: layer '{name}' is not in {type} ({c.name}); generation leaves it out");
            }
        }

        // ---- 2b. material property parts in neutral or override rows (the loader leaves those rows out)

        static void PropertyRows(TableModel m, List<string> f)
        {
            foreach (var part in m.PropertyPartsInRows)
                f.Add($"avatar: part {part}: material property parts only move in a state's fixed amounts");
        }

        // ---- 3. inputs that nothing uses

        static void Unused(TableModel m, List<string> f)
        {
            var used = NativeFtWriter.AnimatorInputs(m);
            foreach (var i in m.Inputs.Where(i => !i.IsTrack && !used.Contains(i.Name)))
                f.Add($"unused: input {i.Name} is not used by any transition");
        }

        // ---- 6. state machines (design.md "検査"): menu states over a menu's 8 controls, a fixed amount on a
        // blendshape that a Blendshapes Set also moves in that state, and one region taken by two state machines.

        public const int MaxMenuStates = 8;

        static void States(TableModel m, List<string> f)
        {
            // One side's sets: the desktop copies would repeat the same findings.
            var sets = NativeSets.Build(m).Where(x => NativeSets.On(x, false)).ToList();
            foreach (var sm in m.StateMachines)
            {
                var n = sm.MenuStates.Count();
                if (n > MaxMenuStates) f.Add($"menu: state machine {sm.Name} has {n} menu states: over the {MaxMenuStates} controls of one menu");
                foreach (var st in sm.States)
                {
                    // The sets that can have weight 1 in this state: its own overrides, and neutral of the regions it keeps.
                    var live = sets.Where(x => x.StateMachine == sm.Name && x.State == st.Name
                                            || x.StateMachine == null && !st.Regions.ContainsKey(x.Region)).ToList();
                    foreach (var part in st.Fixed.Keys)
                    {
                        var target = m.Parts[part];
                        if (target.IsProperty) continue;   // the sets move blendshapes only
                        foreach (var set in live)
                        {
                            foreach (var r in set.Rows.Where(r => r.Mesh == target.Mesh && r.Shape == target.Shape))
                                f.Add($"fixed: {sm.Name}/{st.Name}: fixed {part} and Blendshapes Set {set.Name} ({r.Key}) move the same blendshape {target.Shape}");
                            foreach (var c in set.Curves.Where(c => c.Shape == target.Shape && c.Meshes.Contains(target.Mesh)))
                                f.Add($"fixed: {sm.Name}/{st.Name}: fixed {part} and the curve of Blendshapes Set {set.Name} ({string.Join(", ", c.Lines.Select(l => l.Key))}) move the same blendshape {target.Shape}");
                        }
                    }
                }
            }
            for (int a = 0; a < m.StateMachines.Count; a++)
                for (int b = a + 1; b < m.StateMachines.Count; b++)
                {
                    var ra = new HashSet<string>(m.StateMachines[a].States.SelectMany(st => st.Regions.Keys));
                    foreach (var r in m.StateMachines[b].States.SelectMany(st => st.Regions.Keys).Distinct().Where(ra.Contains))
                        f.Add($"region: {r} is overridden or turned off by both state machines {m.StateMachines[a].Name} and {m.StateMachines[b].Name}; when both are in such states the region's sets can both be on or both be off" +
                              (m.CurvedRows().Any(x => PartNames.Region(x.Part) == r) ? " (and two Parameter Behaviors of its curves move one blendshape at once; how VRChat combines them is not known)" : ""));
                }
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
