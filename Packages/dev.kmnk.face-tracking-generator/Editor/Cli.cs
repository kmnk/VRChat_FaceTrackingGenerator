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
            Debug.Log($"[FTG] load: inputs={model.Inputs.Count} switches={model.Switches.Count} trees={model.Trees.Count} parts={model.Parts.Count} dropped={model.Dropped.Count} warnings={model.Warnings.Count}");
            foreach (var d in model.Dropped) Debug.Log("[FTG] dropped: " + d);
            foreach (var w in model.Warnings) Debug.Log("[FTG] warning: " + w);
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

        // Response curves (R26). -ftQueries: JSON Lines of {"label", "set": {input: value}, "state": {switch: state}};
        // writes JSON Lines of {"label", "shapes": {"mesh/shape": weight}} to -ftOut. Without -ftQueries, sweeps
        // -ftInput over its range in 20 steps.
        public static void Curve() => Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var outPath = CliArgs.Require("-ftOut");
            var lines = new System.Collections.Generic.List<string>();
            var q = CliArgs.Get("-ftQueries");
            if (q != null)
            {
                foreach (var raw in File.ReadAllLines(q))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var o = JObject.Parse(raw);
                    var set = ((JObject)o["set"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => p.Value.Value<float>());
                    var st = ((JObject)o["state"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
                    var shapes = CurveEval.Evaluate(model, set, st);
                    lines.Add(new JObject { ["label"] = o["label"], ["shapes"] = new JObject(shapes.Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1)))) }.ToString(Formatting.None));
                }
            }
            else
            {
                var input = CliArgs.Require("-ftInput");
                var (lo, hi) = CurveEval.Range(model, input);
                for (int i = 0; i <= 20; i++)
                {
                    var x = lo + (hi - lo) * i / 20f;
                    var shapes = CurveEval.Evaluate(model, new System.Collections.Generic.Dictionary<string, float> { [input] = x });
                    lines.Add(new JObject { ["label"] = $"{input}={x:0.###}", ["shapes"] = new JObject(shapes.Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1)))) }.ToString(Formatting.None));
                }
            }
            File.WriteAllText(outPath, string.Join("\n", lines) + "\n");
            Debug.Log($"[FTG] curve: wrote {lines.Count} points to {outPath}");
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
        public static void CreateSettings() => Run(() =>
        {
            var dest = TableWriter.CreateFromTemplates(CliArgs.Require("-ftName"));
            Debug.Log("[FTG] settings: created " + dest);
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
        public static JObject ToJson(TableModel m)
        {
            return new JObject
            {
                ["avatar"] = m.AvatarName,
                ["prefab"] = m.Prefab?.Path,
                ["inputs"] = new JArray(m.Inputs.Select(i => i.IsDerived
                    ? new JObject { ["name"] = i.Name, ["average"] = new JArray(i.Average) }
                    : new JObject { ["name"] = i.Name, ["key"] = i.Key, ["smoothing"] = i.Smoothing, ["sync"] = i.Sync })),
                ["smoothing"] = new JArray(m.Smoothings.Select(s => new JObject { ["name"] = s.Name, ["localTau"] = s.LocalTau, ["remoteTau"] = s.RemoteTau })),
                ["switches"] = new JArray(m.Switches.Select(s => new JObject
                {
                    ["name"] = s.Name,
                    ["states"] = new JArray(s.States),
                    ["transitions"] = new JArray(s.Transitions.Select(t => new JObject
                    {
                        ["from"] = t.From, ["to"] = t.To, ["duration"] = t.Duration,
                        ["when"] = new JArray(t.When.Select(c => new JArray(c.Input, c.Op, c.Value))),
                    })),
                })),
                ["trees"] = new JArray(m.Trees.Select(t => new JObject { ["name"] = t.Name, ["tree"] = Node(t.Root, m) })),
                ["stopWhileTalking"] = new JArray(m.StopWhileTalking),
                ["dropped"] = new JArray(m.Dropped),
                ["warnings"] = new JArray(m.Warnings),
            };
        }

        static JToken Node(Node n, TableModel m)
        {
            switch (n)
            {
                case PoseNode p:
                    var o = new JObject();
                    foreach (var kv in p.Parts)
                        o[kv.Key] = new JObject { ["value"] = kv.Value, ["mesh"] = m.Parts[kv.Key].Mesh, ["shape"] = m.Parts[kv.Key].Shape };
                    return o;
                case Blend1DNode b:
                    return new JObject { ["1d"] = b.Input, ["children"] = new JArray(b.Children.Select(c => new JArray(c.threshold, Node(c.node, m)))) };
                case Blend2DNode b:
                    return new JObject { ["2d"] = new JArray(b.InputX, b.InputY), ["children"] = new JArray(b.Children.Select(c => new JArray(new JArray(c.x, c.y), Node(c.node, m)))) };
                case SwitchNode s:
                    var cases = new JObject();
                    foreach (var c in s.Cases) cases[c.state] = Node(c.node, m);
                    return new JObject { ["switch"] = s.Switch, ["cases"] = cases };
            }
            return JValue.CreateNull();
        }
    }
}
