using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // Asset output (design.md "生成物の出し先"): copies the avatar's controllers and Expression Parameters
    // to the output folder, applies LayerPatcher and FtBuilder to the copies, and points a Prefab Variant
    // of the avatar at them. The originals are never changed.
    public static class Generator
    {
        public sealed class Result
        {
            public List<string> Log = new List<string>();
            public List<string> Written = new List<string>();
            public Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController> Controllers = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>();
            public VRCExpressionParameters ExpressionParameters;
            // The copy of the avatar's menu with the FT toggles and the state submenus (MenuBuilder); null: not changed
            public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu Menu;
            // What NativeFtWriter writes into the Face Tracking section
            public NativeFtWriter.Plan FaceTracking = new NativeFtWriter.Plan();
        }

        // Points an Avatar Descriptor at the generated controllers and Expression Parameters, and writes the inputs
        // into its Face Tracking section. Both the Prefab Variant and the build-time copy go through here.
        public static void Assign(VRCAvatarDescriptor d, Result r)
        {
            r.Log.AddRange(NativeFtWriter.Apply(d, r.FaceTracking));
            d.customizeAnimationLayers = true;
            var layers = d.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
                if (r.Controllers.TryGetValue(layers[i].type, out var c))
                {
                    layers[i].isDefault = false;
                    layers[i].isEnabled = true;
                    layers[i].animatorController = c;
                }
            d.baseAnimationLayers = layers;
            d.customExpressions = true;
            d.expressionParameters = r.ExpressionParameters;
            if (r.Menu != null) d.expressionsMenu = r.Menu;
        }

        // What the table writes per device: transitions for one device, sets per device and the rows and states that
        // differ. Nothing for a table that writes nothing per device (the output is then the same as before).
        public static List<string> SideLog(TableModel m, List<NativeSets.SetDef> sets)
        {
            var log = new List<string>();
            var all = m.StateMachines.SelectMany(sm => sm.Transitions).ToList();
            var vr = all.Count(t => t.Device == Device.Vr);
            var desktop = all.Count(t => t.Device == Device.Desktop);
            if (vr + desktop > 0)
                log.Add($"devices: transitions {all.Count - vr - desktop} for both, {vr} for VR only, {desktop} for desktop only (VRMode)");
            var perDevice = sets.Count(x => x.Side == Device.Desktop);
            if (perDevice > 0)
            {
                var rows = m.Neutral.Values.SelectMany(r => r).Concat(m.StateMachines.SelectMany(sm => sm.States).SelectMany(st => st.Regions.Values.SelectMany(rg => rg.Rows))).Count(r => r.StrengthByDevice);
                log.Add($"devices: Blendshapes Sets per device ({perDevice} for VR, {perDevice} for desktop, VRMode); rows with another strength on desktop: {rows}");
            }
            var stops = m.StateMachines.SelectMany(sm => sm.States.Where(st => st.StopVisemesByDevice).Select(st => $"{sm.Name}/{st.Name} ({(st.StopVisemesVr ? "VR" : "desktop")} only)")).ToList();
            if (stops.Count > 0) log.Add($"devices: stopVisemes on one device: {string.Join(", ", stops)}");
            if (m.Measurements.Any)
                log.Add($"measurements: VR {m.Measurements.VrSide?.Files.Count ?? 0} files, desktop {m.Measurements.DesktopSide?.Files.Count ?? 0} files (shown in the settings editor; the output does not use them)");
            return log;
        }

        public static Result Generate(TableModel m, bool writeVariant = true)
        {
            var r = new Result();
            foreach (var n in m.Notes) r.Log.Add("note: " + n);
            // The table's warnings, so that every entry point (the window, NDMF, the command line) shows them.
            foreach (var w in m.Warnings) r.Log.Add("warning: " + w);
            var prefabPath = ResolvePath(m.Prefab, "prefab", r);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var desc = prefab.GetComponent<VRCAvatarDescriptor>() ?? throw new TableLoader.TableException("no VRCAvatarDescriptor on " + prefabPath);
            OutputFolderIsOwn(m, prefabPath, desc);
            Directory.CreateDirectory(m.OutputFolder);

            var afk = new AfkBuilder(m);
            var withAfk = afk.CanBuild(out var noAfk);
            r.Log.Add(withAfk ? "AFK: building AFK/Action and AFK/FX" : "AFK: not built: " + noAfk);
            var groups = new List<AfkGroup>(m.AfkGroups);
            if (withAfk) groups.Add(AfkBuilder.Group());

            var visemes = VisemeStop.Build(m, desc, r.Log);
            PropertyParts.Resolve(m, prefab);

            var outputs = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AnimatorController>();
            foreach (var (type, key) in new[] { (VRCAvatarDescriptor.AnimLayerType.FX, "fx"), (VRCAvatarDescriptor.AnimLayerType.Gesture, "gesture"), (VRCAvatarDescriptor.AnimLayerType.Action, "action") })
            {
                var isFx = type == VRCAvatarDescriptor.AnimLayerType.FX;
                var isAction = type == VRCAvatarDescriptor.AnimLayerType.Action;
                m.Layers.TryGetValue(key, out var adj);
                if (!isFx && adj == null && !(isAction && withAfk)) continue;
                var layer = desc.baseAnimationLayers.FirstOrDefault(l => l.type == type);
                if (layer.isDefault || layer.animatorController == null)
                    throw new TableLoader.TableException($"{prefabPath} has no {type} controller");
                var src = AssetDatabase.GetAssetPath(layer.animatorController);
                var dest = FtNames.Controller(m.OutputFolder, type.ToString());
                AssetCopy.CopyKeepingGuid(src, dest);
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(dest);
                foreach (var line in LayerPatcher.Apply(c, adj, m.FaceTrackParameter))
                    r.Log.Add(line.StartsWith("notice: ") ? $"notice: {type}: {line.Substring(8)}" : $"{type}: {line}");

                var reg = new StableIds.Registry(dest);
                if (isFx)
                {
                    var ft = new FtBuilder(m, c, groups, visemes);
                    ft.Build();
                    foreach (var l in ft.Layers) reg.AddLayer(l);
                    r.Log.Add($"{type}: added {ft.Layers.Count} FT layers");
                    if (withAfk)
                    {
                        afk.BuildFx(c);
                        foreach (var l in afk.FxLayers) reg.AddLayer(l);
                        r.Log.Add($"{type}: added AFK/FX");
                    }
                }
                if (isAction && withAfk)
                {
                    afk.BuildAction(c);
                    foreach (var l in afk.ActionLayers) reg.AddLayer(l);
                    r.Log.Add($"{type}: added AFK/Action");
                }
                var orphans = LayerPatcher.RemoveOrphanParameters(c);
                if (orphans.Count > 0) r.Log.Add($"{type}: removed {orphans.Count} parameters only the removed layers used");
                var removed = StableIds.RemoveUnreachable(c);
                if (removed > 0) r.Log.Add($"{type}: removed {removed} unreachable objects");
                EditorUtility.SetDirty(c);
                AssetDatabase.SaveAssets();
                StableIds.Apply(dest, reg);
                outputs[type] = AssetDatabase.LoadAssetAtPath<AnimatorController>(dest);
                r.Written.Add(dest);
            }

            var exprPath = FtNames.ExpressionParameters(m.OutputFolder);
            // The menu decides which toggles and submenus go in, and each takes its parameter from the budget: a toggle
            // is added only with its parameter, and a parameter only for a control the menu has (one place decides both).
            var budget = ExpressionParameters(m, desc.expressionParameters, exprPath, r);
            var budgetLogAt = r.Log.Count;
            r.Written.Add(exprPath);
            r.Controllers = outputs;
            r.FaceTracking = NativeFtWriter.Build(m);
            r.FaceTracking.Visemes = visemes;
            r.Log.AddRange(SideLog(m, r.FaceTracking.Sets));
            foreach (var path in MenuInstaller.WriteStateMenus(m, r.Log)) r.Written.Add(path);
            r.Menu = MenuBuilder.Build(m, desc.expressionsMenu, r.Log, r.Written, budget, withAfk);
            r.ExpressionParameters = budget.Write(m);
            r.Log.InsertRange(budgetLogAt, budget.Log);

            if (writeVariant && m.VariantPath != null)
            {
                Variant(prefab, m.VariantPath, r);
                r.Written.Add(m.VariantPath);
            }
            AssetDatabase.SaveAssets();
            return r;
        }

        // The output folder is the generator's own: it must not hold the avatar's own prefab, controllers, menus or
        // Expression Parameters (generating again writes and removes files there).
        static void OutputFolderIsOwn(TableModel m, string prefabPath, VRCAvatarDescriptor desc)
        {
            var folder = m.OutputFolder.TrimEnd('/') + "/";
            var own = new List<UnityEngine.Object> { desc.expressionParameters };
            own.AddRange(desc.baseAnimationLayers.Select(l => (UnityEngine.Object)l.animatorController));
            if (desc.expressionsMenu != null) own.AddRange(MenuBuilder.AllMenus(desc.expressionsMenu, new HashSet<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>()));
            var paths = own.Where(o => o != null).Select(AssetDatabase.GetAssetPath).Append(prefabPath);
            var inside = paths.FirstOrDefault(p => p != null && p.StartsWith(folder, StringComparison.Ordinal));
            if (inside != null)
                throw new TableLoader.TableException($"outputFolder {m.OutputFolder} holds the avatar's own {inside}; choose a folder for the generator only");
        }

        static string ResolvePath(AssetRef a, string what, Result r)
        {
            if (a == null) throw new TableLoader.TableException(what + " is not set");
            return AssetRefs.Path(a, what, r.Log) ?? throw new TableLoader.TableException(what + " has neither guid nor path");
        }

        // The synced parameters the menu's controls need, added within the 256 bits of Expression Parameters.
        public sealed class ParameterBudget
        {
            readonly VRCExpressionParameters asset;
            readonly List<VRCExpressionParameters.Parameter> list;
            readonly List<VRCExpressionParameters.Parameter> added = new List<VRCExpressionParameters.Parameter>();
            readonly Dictionary<string, string> notices = new Dictionary<string, string>();
            public readonly List<string> Log = new List<string>();

            public ParameterBudget(VRCExpressionParameters asset, List<VRCExpressionParameters.Parameter> list) { this.asset = asset; this.list = list; }

            // Whether the parameter is there (already, or added now). False when adding it would go over the limit.
            public bool Take(string name, VRCExpressionParameters.ValueType type, float def)
            {
                if (list.Any(x => x.name == name) || added.Any(x => x.name == name)) return true;
                var p = new VRCExpressionParameters.Parameter { name = name, valueType = type, defaultValue = def, saved = false, networkSynced = true };
                var cost = Cost(list.Concat(added).Concat(new[] { p }));
                if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                {
                    notices[name] = $"notice: expression parameters: {name} not added; synced parameters would use {cost} bits (max {VRCExpressionParameters.MAX_PARAMETER_COST})";
                    return false;
                }
                added.Add(p);
                return true;
            }

            static int Cost(IEnumerable<VRCExpressionParameters.Parameter> ps)
            {
                var probe = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                probe.parameters = ps.ToArray();
                var cost = probe.CalcTotalCost();
                UnityEngine.Object.DestroyImmediate(probe);
                return cost;
            }

            // Writes the asset: the avatar's parameters, then the added ones in a fixed order (state machine menus,
            // the FT toggle, the AFK toggle), so that the file does not depend on the order the menu took them.
            public VRCExpressionParameters Write(TableModel m)
            {
                var order = FtBuilder.WithMenu(m).Select(sm => FtNames.MenuParam(sm.Name)).Concat(new[] { m.FaceTrackParameter, m.AfkPlayParameter }).ToList();
                foreach (var name in order)
                {
                    var p = added.FirstOrDefault(x => x.name == name);
                    if (p != null)
                    {
                        list.Add(p);
                        Log.Add($"expression parameters: added {name} ({(p.valueType == VRCExpressionParameters.ValueType.Int ? "Int" : "Bool")}, synced)");
                    }
                    else if (notices.TryGetValue(name, out var n)) Log.Add(n);
                }
                asset.parameters = list.ToArray();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                Log.Add($"expression parameters: {asset.CalcTotalCost()} bits");
                return asset;
            }
        }

        // Copies Expression Parameters without the FT inputs (R4). The FT parameters are added by the budget it returns.
        static ParameterBudget ExpressionParameters(TableModel m, VRCExpressionParameters src, string dest, Result r)
        {
            if (src == null) throw new TableLoader.TableException("the avatar has no Expression Parameters");
            AssetCopy.CopyKeepingGuid(AssetDatabase.GetAssetPath(src), dest);
            var p = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(dest);
            var list = p.parameters.ToList();
            // Inputs come from the Face Tracking section and every client computes them, so none is synced (R4).
            // The input layer's keys and parameters, and v2/ parameters (VRCFaceTracking's), are taken out.
            var known = new HashSet<string>(m.Inputs.Where(i => !i.IsTrack).Select(i => i.Key));
            known.UnionWith(m.Inputs.Where(i => !i.IsTrack).Select(i => i.Parameter));
            foreach (var x in list.ToList())
            {
                var isFtKey = x.name != null && x.name.StartsWith("v2/");
                if (known.Contains(x.name) || isFtKey)
                {
                    list.Remove(x);
                    r.Log.Add("expression parameters: removed " + x.name);
                }
            }
            // The menu of each state machine is written again by the budget (an Int that the submenu's toggles set).
            foreach (var sm in FtBuilder.WithMenu(m)) list.RemoveAll(x => x.name == FtNames.MenuParam(sm.Name));
            return new ParameterBudget(p, list);
        }

        static void Variant(GameObject prefab, string path, Result r)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                inst.name = Path.GetFileNameWithoutExtension(path);
                Assign(inst.GetComponent<VRCAvatarDescriptor>(), r);
                PrefabUtility.SaveAsPrefabAsset(inst, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // A Prefab Variant for the build-time output: the avatar's own controllers, plus FtGeneratorSettings.
        public static string BuildVariant(TableModel m, string path)
        {
            var prefabPath = AssetRefs.Path(m.Prefab, "prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                inst.name = Path.GetFileNameWithoutExtension(path);
                var s = inst.GetComponent<FtGeneratorSettings>() ?? inst.AddComponent<FtGeneratorSettings>();
                s.avatarTablePath = m.AvatarTablePath;
                s.avatarTableGuid = AssetDatabase.AssetPathToGUID(m.AvatarTablePath);
                PrefabUtility.SaveAsPrefabAsset(inst, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
            return path;
        }
    }
}
