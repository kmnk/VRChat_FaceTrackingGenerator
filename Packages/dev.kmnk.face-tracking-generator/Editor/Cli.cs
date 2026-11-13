using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Command line entry points. Each calls the same functions as the editor window and exits Unity
    // with 0 on success, 1 when checks find problems, 2 on errors.
    //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.Cli.<Method> -ftTable <avatar table> [...]
    public static class Cli
    {
        // Writes the resolved model as JSON (-ftOut, or the log when omitted).
        public static void Load() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var json = ModelJson.ToJson(model).ToString(Formatting.Indented);
            var outPath = CliArgs.Get("-ftOut");
            if (outPath != null) File.WriteAllText(outPath, json + "\n");
            else Debug.Log(json);
            Debug.Log($"[FTG] load: inputs={model.Inputs.Count(i => !i.IsTrack)} keys={model.Inputs.Count(i => i.IsTrack)} neutral={model.Neutral.Sum(kv => kv.Value.Count)} stateMachines={model.StateMachines.Count} states={model.StateMachines.Sum(x => x.States.Count)} transitions={model.StateMachines.Sum(x => x.Transitions.Count)} parts={model.Parts.Count} dropped={model.Dropped.Count} warnings={model.Warnings.Count}");
            foreach (var d in model.Dropped) Debug.Log("[FTG] dropped: " + d);
            foreach (var w in model.Warnings) Debug.Log("[FTG] warning: " + w);
            foreach (var n in model.Notes) Debug.Log("[FTG] note: " + n);
            return 0;
        });

        // Generates the asset output (copied controllers, Expression Parameters, Prefab Variant).
        public static void Generate() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            foreach (var w in model.Warnings) Debug.Log("[FTG] warning: " + w);
            var r = Generator.Generate(model);
            foreach (var l in r.Log) Debug.Log("[FTG] " + l);
            foreach (var w in r.Written) Debug.Log("[FTG] wrote " + w);
            return 0;
        });

        // Static checks (R24) of the tables and the generated FX controller. -ftFx overrides the controller path.
        public static void Check() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var fxPath = CliArgs.Get("-ftFx", model.OutputFolder + "/FX.controller");
            var fx = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(fxPath);
            if (fx == null) Debug.Log("[FTG] check: no generated FX controller at " + fxPath + " (table checks only)");
            if (fx != null) Debug.Log($"[FTG] check: controller {AssetDatabase.GetAssetPath(fx)} ({fx.layers.Length} layers, {AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(fx)).Length} objects)");
            var findings = StaticChecker.Check(model, fx);
            var variant = model.VariantPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(model.VariantPath) : null;
            var desc = variant != null ? variant.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() : null;
            if (desc == null) Debug.Log("[FTG] check: no generated Prefab Variant at " + model.VariantPath + " (Face Tracking section not checked)");
            else
            {
                var ft = desc.faceTrackingParameterBehaviors;
                Debug.Log($"[FTG] check: face tracking {model.VariantPath}: enabled={desc.enableFaceTracking} allKeys={desc.useAllTrackingKeys} behaviors={(ft == null ? 0 : ft.Count)}");
                findings.AddRange(StaticChecker.FaceTracking(model, desc, model.VariantPath));
                Debug.Log($"[FTG] check: eye look gaze={desc.customEyeLookSettings.eyeGazeMode} eyelids={desc.customEyeLookSettings.eyelidType} meshes={(desc.faceTrackingMeshes == null ? 0 : desc.faceTrackingMeshes.Count)} operations={StaticChecker.Operations(desc)}");
                foreach (var s in desc.faceTrackingBlendshapesSetList ?? new System.Collections.Generic.List<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
                {
                    var names = s.faceTrackingBlendshapeNames ?? new string[0];
                    var rows = System.Linq.Enumerable.Range(0, names.Length).Where(i => !string.IsNullOrEmpty(names[i])).ToList();
                    var visemes = (s.visemeBlendshapeNames ?? new string[0]).Count(v => !string.IsNullOrEmpty(v));
                    Debug.Log($"[FTG] check: face tracking set {s.setName} weight={s.setWeight} rows={rows.Count} visemes={visemes}");
                    foreach (var i in rows) Debug.Log($"[FTG] check: face tracking row {s.setName} {(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingKey)i} -> {names[i]} x{s.faceTrackingBlendshapeMultipliers[i]}");
                }
                var src = !string.IsNullOrEmpty(model.Prefab?.Guid) ? AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab?.Path;
                var orig = AssetDatabase.LoadAssetAtPath<GameObject>(src)?.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (orig != null)
                {
                    var os = orig.faceTrackingBlendshapesSetList ?? new System.Collections.Generic.List<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet>();
                    Debug.Log($"[FTG] check: original prefab face tracking sets={os.Count} rows={os.Sum(s => (s.faceTrackingBlendshapeNames ?? new string[0]).Count(v => !string.IsNullOrEmpty(v)))} visemes={os.Sum(s => (s.visemeBlendshapeNames ?? new string[0]).Count(v => !string.IsNullOrEmpty(v)))} gaze={orig.customEyeLookSettings.eyeGazeMode}");
                }
            }
            foreach (var x in findings) Debug.Log("[FTG] finding: " + x);
            Debug.Log($"[FTG] check: findings={findings.Count}");
            return findings.Count == 0 ? 0 : 1;
        });

        // Adds the FT toggle and the AFK flag to the avatar's Expression Parameters and menu (R28).
        public static void InstallMenu() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            foreach (var l in MenuInstaller.Install(model)) Debug.Log("[FTG] menu: " + l);
            return 0;
        });

        // Face values (R15) by FaceEval. -ftQueries: JSON Lines of {"label", "set": {key or "key:<key>": value},
        // "state": {state machine: state}}; or -ftSet "Key=value,..." and -ftStates "StateMachine=state,..." for one
        // query. Writes JSON Lines of {"label", "shapes": {"mesh/shape": weight}} to -ftOut, or the log without it.
        public static void Curve() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var lines = new System.Collections.Generic.List<string>();
            System.Collections.Generic.Dictionary<string, float> Keys(System.Collections.Generic.IEnumerable<(string, float)> pairs) =>
                pairs.ToDictionary(p => p.Item1.StartsWith(InputDef.TrackPrefix) || NativeKeys.Index(p.Item1) < 0 ? p.Item1 : InputDef.TrackPrefix + p.Item1, p => p.Item2);
            void Add(object label, System.Collections.Generic.Dictionary<string, float> set, System.Collections.Generic.Dictionary<string, string> st)
            {
                var shapes = FaceEval.Evaluate(model, set, st);
                lines.Add(new JObject { ["label"] = JToken.FromObject(label ?? ""), ["states"] = new JObject(FaceEval.States(model, st).Select(kv => new JProperty(kv.Key, kv.Value))), ["shapes"] = new JObject(shapes.Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1)))) }.ToString(Formatting.None));
            }
            var q = CliArgs.Get("-ftQueries");
            if (q != null)
                foreach (var raw in File.ReadAllLines(q))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var o = JObject.Parse(raw);
                    Add((string)o["label"], Keys(((JObject)o["set"] ?? new JObject()).Properties().Select(p => (p.Name, p.Value.Value<float>()))),
                        ((JObject)o["state"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (string)p.Value));
                }
            else
            {
                System.Collections.Generic.IEnumerable<string[]> Pairs(string arg) => (CliArgs.Get(arg) ?? "").Split(',').Where(x => x.Contains('=')).Select(x => x.Split('='));
                Add("query", Keys(Pairs("-ftSet").Select(p => (p[0].Trim(), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))),
                    Pairs("-ftStates").ToDictionary(p => p[0].Trim(), p => p[1].Trim()));
            }
            var outPath = CliArgs.Get("-ftOut");
            if (outPath != null) File.WriteAllText(outPath, string.Join("\n", lines) + "\n");
            foreach (var l in lines) Debug.Log("[FTG] curve: " + l);
            return 0;
        });

        // The window's "Save AFK clips to the table": -ftClips is six asset paths separated by commas
        // (action intro, loop, outro, fx intro, loop, outro); an empty item means none.
        public static void SetAfkClips() => Run(() =>
        {
            var table = CliArgs.Require("-ftTable");
            var paths = CliArgs.Require("-ftClips").Split(',');
            if (paths.Length != 6) throw new ArgumentException("-ftClips needs six items");
            var clips = paths.Select(p => p == "" ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(p)).ToArray();
            TableWriter.SetAfkClips(table, clips);
            Debug.Log("[FTG] afk clips: saved to " + table);
            return 0;
        });

        // The window's "Create Settings/<name>".
        // -ftFrom <folder>: the input and expression tables come from a folder written by ExportTables (Share).
        public static void CreateSettings() => Run(() =>
        {
            var from = CliArgs.Get("-ftFrom");
            var dest = from != null ? TableShare.CreateFromFolder(CliArgs.Require("-ftName"), from) : TableWriter.CreateFromTemplates(CliArgs.Require("-ftName"));
            Debug.Log("[FTG] settings: created " + dest);
            return 0;
        });

        // The settings editor's Share > Export: -ftTable <avatar.jsonc> -ftDir <folder>.
        public static void ExportTables() => Run(() =>
        {
            var dir = CliArgs.Require("-ftDir");
            var existing = TableShare.Existing(dir);
            TableShare.Export(CliArgs.Require("-ftTable"), dir);
            Debug.Log($"[FTG] share: exported {TableShare.InputFile}, {TableShare.ExpressionFile} to {dir} (overwrote: {(existing.Count > 0 ? string.Join(", ", existing) : "none")})");
            return 0;
        });

        // The settings editor's Share > Import: -ftTable <avatar.jsonc> -ftDir <folder>; -ftApply 1 overwrites.
        public static void ImportTables() => Run(() =>
        {
            var table = CliArgs.Require("-ftTable");
            var dir = CliArgs.Require("-ftDir");
            var changes = TableShare.Changes(table, dir);
            Debug.Log("[FTG] share: changes=" + (changes.Count > 0 ? string.Join(",", changes) : "none"));
            if (CliArgs.Get("-ftApply") == "1" && changes.Count > 0) Debug.Log("[FTG] share: imported " + string.Join(",", TableShare.Import(table, dir)));
            return 0;
        });

        // Exports a .unitypackage. -ftList: a file of asset paths (files and their parent folders), one per line.
        public static void ExportPackage() => Run(() =>
        {
            var paths = File.ReadAllLines(CliArgs.Require("-ftList")).Select(l => l.Trim()).Where(l => l != "").ToArray();
            foreach (var p in paths)
                if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException("not in the project: " + p);
            var outPath = CliArgs.Require("-ftOut");
            AssetDatabase.ExportPackage(paths, outPath, ExportPackageOptions.Default);
            Debug.Log($"[FTG] package: exported {paths.Length} paths to {outPath}");
            return File.Exists(outPath) ? 0 : 2;
        });

        // Writes a Prefab Variant for the build-time output (-ftOut <prefab path>): the avatar with FtGeneratorSettings.
        public static void BuildVariant() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            Debug.Log("[FTG] build variant: wrote " + Generator.BuildVariant(model, CliArgs.Require("-ftOut")));
            return 0;
        });

        // The window's "Fill prefab, mesh and menu from it": -ftAvatar is a prefab asset path.
        public static void FillFromAvatar() => Run(() =>
        {
            var table = CliArgs.Require("-ftTable");
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(CliArgs.Require("-ftAvatar"));
            foreach (var l in AvatarSetup.FillTable(table, go)) Debug.Log("[FTG] fill: " + l);
            return 0;
        });

        // What AfkFinder finds in a controller (-ftController "<path>;..."), as Fill from avatar uses it.
        public static void FindAfk() => Run(() =>
        {
            foreach (var path in CliArgs.Require("-ftController").Split(';').Where(x => x != ""))
            {
                var c = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
                if (c == null) throw new ArgumentException("not a controller: " + path);
                var found = AfkFinder.Find(c);
                foreach (var f in found)
                    Debug.Log($"[FTG] afk found {c.name}/{f.Layer}: intro={P(f.Intro)} loop={P(f.Loop)} outro={P(f.Outro)}");
                Debug.Log($"[FTG] afk {c.name}: layers={found.Count}");
            }
            return 0;
        });

        static string P(UnityEngine.Object o) => o == null ? "-" : AssetDatabase.GetAssetPath(o);

        internal static void Run(Func<int> body)
        {
            int code;
            try { code = body(); }
            catch (TableLoader.TableException e) { Debug.Log("[FTG] error: " + e.Message); code = 2; }
            catch (Exception e) { Debug.LogException(e); Debug.Log("[FTG] error: " + e.Message); code = 2; }
            EditorApplication.Exit(code);
        }
    }

    public static class ModelJson
    {
        static JObject Row(TrackRow r, TableModel m) => new JObject
        {
            ["key"] = r.Key, ["part"] = r.Part, ["strength"] = r.Strength,
            ["shape"] = r.Part != null && m.Parts.TryGetValue(r.Part, out var t) ? t.Shape : null,
        };

        public static JObject ToJson(TableModel m)
        {
            return new JObject
            {
                ["avatar"] = m.AvatarName,
                ["prefab"] = m.Prefab?.Path,
                ["inputs"] = new JArray(m.Inputs.Select(i => new JObject { ["name"] = i.Name, ["key"] = i.Key, ["parameter"] = i.Parameter, ["expression"] = i.Expression, ["expressionFrom"] = i.IsTrack ? "tracking key" : i.ExpressionFromTable ? "table" : "built-in" })),
                ["neutral"] = new JObject(m.Neutral.Select(kv => new JProperty(kv.Key, new JArray(kv.Value.Select(r => Row(r, m)))))),
                ["stateMachines"] = new JArray(m.StateMachines.Select(sm => new JObject
                {
                    ["name"] = sm.Name,
                    ["states"] = new JArray(sm.States.Select(st => new JObject
                    {
                        ["name"] = st.Name,
                        ["menu"] = st.Menu ? sm.MenuValue(st.Name) : 0,
                        ["regions"] = new JObject(st.Regions.Select(kv => new JProperty(kv.Key, kv.Value.Off ? (JToken)TableLoader.Off : new JArray(kv.Value.Rows.Select(r => Row(r, m)))))),
                        ["fixed"] = new JObject(st.Fixed.Select(kv => new JProperty(kv.Key, kv.Value))),
                    })),
                    ["transitions"] = new JArray(sm.Transitions.Select(t => new JObject
                    {
                        ["from"] = t.From, ["to"] = t.To, ["duration"] = t.Duration,
                        ["when"] = new JArray(t.When.Select(c => new JArray(c.Input, c.Op, c.Value))),
                    })),
                })),
                ["stopWhileTalking"] = new JArray(m.StopWhileTalking),
                ["dropped"] = new JArray(m.Dropped),
                ["warnings"] = new JArray(m.Warnings),
                ["notes"] = new JArray(m.Notes),
            };
        }
    }
}
