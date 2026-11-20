using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Makes the avatars and tables of the characterization cases G1 to G8 (the behaviour baseline compared before and
    // after refactoring). Run once; the results are committed in the development project, so their GUIDs stay the same.
    //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.CharacterizationSetup.Make \
    //     -ftPrefab <development avatar prefab> -ftTable <its avatar table> -ftOut Assets/FTGCharacterization \
    //     -ftRemoveLayer <an FX layer of the prefab to remove> -ftWeightLayer <an FX layer to give weight 0.5>
    // Tables go to Settings/CharG<n>/. Prints [FTG] characterization setup: ... per case.
    public static class CharacterizationSetup
    {
        const string SettingsRoot = "Assets/Kmnk/FaceTrackingGenerator/Settings";
        static readonly JsonLoadSettings Jsonc = new JsonLoadSettings { CommentHandling = CommentHandling.Ignore };

        public static void Make() => Cli.Run(() =>
        {
            var basePath = CliArgs.Require("-ftPrefab");
            var outDir = CliArgs.Require("-ftOut");
            var devTable = CliArgs.Require("-ftTable");
            Directory.CreateDirectory(outDir);

            // G1, G2: the perfect sync avatars (without and with its own Blendshapes Set).
            Call(() => TableEditorChecks.MakePerfectSyncAvatar(), "-ftPrefab", basePath, "-ftOut", outDir);
            var plain = outDir + "/PerfectSyncAvatar.prefab";
            var own = outDir + "/PerfectSyncAvatarOwnFT.prefab";

            // G1: settings from the templates, filled from the perfect sync avatar.
            var g1 = Fresh("CharG1");
            Call(() => Cli.CreateSettings(), "-ftName", "CharG1");
            Call(() => Cli.FillFromAvatar(), "-ftTable", g1, "-ftAvatar", plain);
            Log("G1", g1, plain);

            // G2: G1's tables on the avatar with its own set. G3: G2 overwriting it, eye gaze Both.
            var g2 = CopyTables(g1, "CharG2", a => SetPrefab(a, own));
            Log("G2", g2, own);
            var g3 = CopyTables(g2, "CharG3", a => { a["overwriteFaceTracking"] = true; a["eyeGazeMode"] = "Both"; });
            Log("G3", g3, own);

            // G7: G1 on a copy whose Expression Parameters are filled near the limit.
            var g7Avatar = Variant(plain, outDir + "/G7Avatar.prefab", d =>
            {
                var p = CopyAsset(d.expressionParameters, outDir + "/G7Parameters.asset");
                var list = p.parameters.ToList();
                for (int i = 0; p.CalcTotalCost() < VRCExpressionParameters.MAX_PARAMETER_COST - 2 && i < 400; i++)
                {
                    list.Add(new VRCExpressionParameters.Parameter { name = "Fill" + i, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true });
                    p.parameters = list.ToArray();
                }
                EditorUtility.SetDirty(p);
                d.expressionParameters = p;
            });
            var g7 = CopyTables(g1, "CharG7", a => SetPrefab(a, g7Avatar));
            Log("G7", g7, g7Avatar);

            // G4: the development avatar's tables with every branch packed in.
            var dev = AssetDatabase.LoadAssetAtPath<GameObject>(basePath).GetComponent<VRCAvatarDescriptor>();
            var secondAfk = dev.expressionParameters.parameters.FirstOrDefault(x => x.valueType == VRCExpressionParameters.ValueType.Bool && x.name != "AFK" && x.name != "AFKPlay")?.name;
            var g4 = CopyTables(devTable, "CharG4", a =>
            {
                var parts = (JObject)a["parts"];
                var smileL = (string)parts["lip.mouthSmileLeft"];
                parts["lip.mouthSmileLeft"] = new JObject { ["shape"] = smileL, ["scale"] = 0.7 };
                parts["cheek.joy.shadow"] = new JObject { ["property"] = "_ShadowStrength" };
                a["missingParts"] = new JArray("nose.noseSneerLeft", "nose.noseSneerRight");
                a["subMenu"] = "FTG";
                var groups = new JArray(new JObject { ["parameters"] = new JArray("AFK"), ["wait"] = 6 });
                if (secondAfk != null) groups.Add(new JObject { ["parameters"] = new JArray(secondAfk), ["wait"] = 2 });
                a["afkGroups"] = groups;
                a["layers"] = new JObject
                {
                    ["fx"] = new JObject
                    {
                        ["remove"] = new JArray(CliArgs.Require("-ftRemoveLayer")),
                        ["weights"] = new JObject { [CliArgs.Require("-ftWeightLayer")] = 0.5 },
                        ["stopWhileFaceTrack"] = new JArray("Left Hand", "Right Hand"),
                    },
                };
            }, e =>
            {
                ((JObject)e["neutral"]["jaw"])["JawOpen"] = new JObject { ["part"] = "jaw.jawOpen", ["strength"] = new JObject { ["vr"] = 1, ["desktop"] = 0.6 } };
                var mouth = new JObject
                {
                    ["states"] = new JObject
                    {
                        ["normal"] = new JObject { ["position"] = new JArray(0, 0) },
                        ["pout"] = new JObject { ["menu"] = true, ["position"] = new JArray(300, -100), ["stopVisemes"] = "vr", ["fixed"] = new JObject { ["lip.mouthSmileLeft"] = 30 } },
                        ["wide"] = new JObject { ["menu"] = true, ["position"] = new JArray(300, 100), ["stopVisemes"] = true, ["regions"] = new JObject { ["jaw"] = "off" }, ["fixed"] = new JObject { ["cheek.joy.shadow"] = 80 } },
                    },
                    ["transitions"] = new JArray(
                        new JObject { ["from"] = "normal", ["to"] = "pout", ["when"] = new JArray((object)new JArray("MouthPressLeft", ">", 0.5)), ["duration"] = 0.1, ["device"] = "desktop" },
                        new JObject { ["from"] = "pout", ["to"] = "normal", ["when"] = new JArray((object)new JArray("MouthPressLeft", "<", 0.4)), ["duration"] = 0.1, ["device"] = "desktop" },
                        new JObject { ["from"] = "normal", ["to"] = "wide", ["when"] = new JArray((object)new JArray("JawOpen", ">", 0.8)), ["duration"] = 0.1, ["device"] = "vr" },
                        new JObject { ["from"] = "wide", ["to"] = "normal", ["when"] = new JArray((object)new JArray("JawOpen", "<", 0.7)), ["duration"] = 0.1 }),
                };
                ((JObject)e["stateMachines"])["Mouth"] = mouth;
            });
            Log("G4", g4, basePath);

            // G5, G6: G4 on copies with another Lip Sync.
            var g5Avatar = Variant(basePath, outDir + "/G5Avatar.prefab", d => d.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking);
            var g5 = CopyTables(g4, "CharG5", a => SetPrefab(a, g5Avatar));
            Log("G5", g5, g5Avatar);
            var g6Avatar = Variant(basePath, outDir + "/G6Avatar.prefab", d => d.lipSync = VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape);
            var g6 = CopyTables(g4, "CharG6", a => SetPrefab(a, g6Avatar));
            Log("G6", g6, g6Avatar);

            // G8: G4's tables built at build time: the development avatar with the settings component.
            var g8 = CopyTables(g4, "CharG8", a => { });
            var g8Avatar = Variant(basePath, outDir + "/G8Avatar.prefab", d =>
            {
                var s = d.gameObject.AddComponent<FtGeneratorSettings>();
                s.avatarTablePath = g8;
                s.avatarTableGuid = AssetDatabase.AssetPathToGUID(g8);
            });
            Log("G8", g8, g8Avatar);
            AssetDatabase.SaveAssets();
            return 0;
        });

        // G11 and G12, added after the 0.18.0 baseline (Make is not run again: it would make new GUIDs).
        //   G11: G4's tables on the development avatar with Expression Parameters filled near the limit (the menu has
        //        room, the parameters do not)
        //   G12: G4's tables without AFK clips (AFK is not built)
        //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.CharacterizationSetup.MakeMore \
        //     -ftOut Assets/FTGCharacterization
        public static void MakeMore() => Cli.Run(() =>
        {
            var outDir = CliArgs.Require("-ftOut");
            var g4 = SettingsRoot + "/CharG4/avatar.jsonc";
            var basePath = (string)JObject.Parse(File.ReadAllText(g4), Jsonc)["prefab"]["path"];
            var g11Avatar = Variant(basePath, outDir + "/G11Avatar.prefab", d =>
            {
                var p = CopyAsset(d.expressionParameters, outDir + "/G11Parameters.asset");
                var list = p.parameters.ToList();
                for (int i = 0; p.CalcTotalCost() < VRCExpressionParameters.MAX_PARAMETER_COST - 9 && i < 400; i++)
                {
                    list.Add(new VRCExpressionParameters.Parameter { name = "Fill" + i, valueType = VRCExpressionParameters.ValueType.Bool, saved = false, networkSynced = true });
                    p.parameters = list.ToArray();
                }
                EditorUtility.SetDirty(p);
                d.expressionParameters = p;
            });
            var g11 = CopyTables(g4, "CharG11", a => SetPrefab(a, g11Avatar));
            Log("G11", g11, g11Avatar);
            var g12 = CopyTables(g4, "CharG12", a => a["afkClips"] = new JObject
            {
                ["actionIntro"] = null, ["actionLoop"] = null, ["actionOutro"] = null, ["fxIntro"] = null, ["fxLoop"] = null, ["fxOutro"] = null,
            });
            Log("G12", g12, basePath);
            AssetDatabase.SaveAssets();
            return 0;
        });

        static void Call(System.Action cli, params string[] args)
        {
            var keep = Cli.KeepRunning;
            CliArgs.Override = new[] { "Unity" }.Concat(args).ToArray();
            Cli.KeepRunning = true;
            try { cli(); }
            finally { CliArgs.Override = null; Cli.KeepRunning = keep; }
            if (Cli.LastExitCode != 0) throw new TableLoader.TableException($"{string.Join(" ", args)}: exit {Cli.LastExitCode}");
        }

        static void Log(string id, string table, string avatar) => Debug.Log($"[FTG] characterization setup: {id} table={table} avatar={avatar}");

        static string Fresh(string name)
        {
            var dir = SettingsRoot + "/" + name;
            AssetDatabase.DeleteAsset(dir);
            return dir + "/avatar.jsonc";
        }

        static JObject Ref(string path) => new JObject { ["guid"] = AssetDatabase.AssetPathToGUID(path), ["path"] = path };

        static void SetPrefab(JObject a, string path) => a["prefab"] = Ref(path);

        // Copies a set of tables to Settings/<name>/ with "name" changed, and edits the avatar and expression tables.
        static string CopyTables(string avatarTable, string name, System.Action<JObject> editAvatar, System.Action<JObject> editExpression = null)
        {
            var dest = Fresh(name);
            var srcDir = Path.GetDirectoryName(avatarTable);
            var destDir = Path.GetDirectoryName(dest);
            Directory.CreateDirectory(destDir);
            var a = JObject.Parse(File.ReadAllText(avatarTable), Jsonc);
            a["name"] = name;
            var exprFile = (string)a["tables"]?["expression"] ?? "expression.jsonc";
            var e = JObject.Parse(File.ReadAllText(Path.Combine(srcDir, exprFile)), Jsonc);
            editAvatar(a);
            editExpression?.Invoke(e);
            File.WriteAllText(dest, a.ToString(Formatting.Indented) + "\n");
            File.WriteAllText(Path.Combine(destDir, exprFile), e.ToString(Formatting.Indented) + "\n");
            AssetDatabase.ImportAsset(destDir, ImportAssetOptions.ImportRecursive);
            return dest;
        }

        static T CopyAsset<T>(T asset, string dest) where T : Object
        {
            AssetDatabase.DeleteAsset(dest);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(asset), dest);
            return AssetDatabase.LoadAssetAtPath<T>(dest);
        }

        // A Prefab Variant of a prefab, with its descriptor edited.
        static string Variant(string basePrefab, string dest, System.Action<VRCAvatarDescriptor> edit)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefab));
            try
            {
                edit(inst.GetComponent<VRCAvatarDescriptor>());
                AssetDatabase.DeleteAsset(dest);
                PrefabUtility.SaveAsPrefabAsset(inst, dest);
                return dest;
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
