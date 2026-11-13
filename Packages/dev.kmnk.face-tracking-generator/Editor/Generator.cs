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
        }

        public static Result Generate(TableModel m, bool writeVariant = true)
        {
            var r = new Result();
            foreach (var n in m.Notes) r.Log.Add("note: " + n);
            var prefabPath = ResolvePath(m.Prefab, "prefab", r);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var desc = prefab.GetComponent<VRCAvatarDescriptor>() ?? throw new TableLoader.TableException("no VRCAvatarDescriptor on " + prefabPath);
            Directory.CreateDirectory(m.OutputFolder);

            var afk = new AfkBuilder(m);
            var withAfk = afk.CanBuild(out var noAfk);
            r.Log.Add(withAfk ? "AFK: building AFK/Action and AFK/FX" : "AFK: not built: " + noAfk);
            var groups = new List<AfkGroup>(m.AfkGroups);
            if (withAfk) groups.Add(AfkBuilder.Group());

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
                var dest = $"{m.OutputFolder}/{type}.controller";
                AssetCopy.CopyKeepingGuid(src, dest);
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(dest);
                foreach (var line in LayerPatcher.Apply(c, adj, m.FaceTrackParameter)) r.Log.Add($"{type}: {line}");

                var reg = new StableIds.Registry(dest);
                if (isFx)
                {
                    var ft = new FtBuilder(m, c, groups);
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

            var exprPath = $"{m.OutputFolder}/ExpressionParameters.asset";
            var expr = ExpressionParameters(m, desc.expressionParameters, exprPath, r);
            r.Written.Add(exprPath);
            r.Controllers = outputs;
            r.ExpressionParameters = expr;
            r.FaceTracking = NativeFtWriter.Build(m);
            foreach (var path in MenuInstaller.WriteStateMenus(m, r.Log)) r.Written.Add(path);

            if (writeVariant && m.VariantPath != null)
            {
                Variant(prefab, m.VariantPath, r);
                r.Written.Add(m.VariantPath);
            }
            AssetDatabase.SaveAssets();
            return r;
        }

        static string ResolvePath(AssetRef a, string what, Result r)
        {
            if (a == null) throw new TableLoader.TableException(what + " is not set");
            var byGuid = string.IsNullOrEmpty(a.Guid) ? null : AssetDatabase.GUIDToAssetPath(a.Guid);
            if (!string.IsNullOrEmpty(byGuid))
            {
                if (a.Path != null && a.Path != byGuid) r.Log.Add($"warning: {what} path in the table is {a.Path}, but the guid points to {byGuid}");
                return byGuid;
            }
            return a.Path ?? throw new TableLoader.TableException(what + " has neither guid nor path");
        }

        // Copies Expression Parameters without the FT inputs (R4).
        static VRCExpressionParameters ExpressionParameters(TableModel m, VRCExpressionParameters src, string dest, Result r)
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
            // The menu of each state machine with menu states: an Int that the submenu's toggles set (design.md "メニューとパラメータ").
            foreach (var sm in FtBuilder.WithMenu(m))
            {
                var name = FtBuilder.MenuParam(sm.Name);
                list.RemoveAll(x => x.name == name);
                list.Add(new VRCExpressionParameters.Parameter { name = name, valueType = VRCExpressionParameters.ValueType.Int, defaultValue = 0, saved = false, networkSynced = true });
                r.Log.Add("expression parameters: added " + name + " (Int, synced)");
            }
            p.parameters = list.ToArray();
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            r.Log.Add($"expression parameters: {p.CalcTotalCost()} bits");
            return p;
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
            var prefabPath = !string.IsNullOrEmpty(m.Prefab?.Guid) ? AssetDatabase.GUIDToAssetPath(m.Prefab.Guid) : m.Prefab?.Path;
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
