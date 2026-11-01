using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Tools/Kmnk/Face Tracking Generator. Every button calls the same functions as the command line (Cli).
    public sealed class GeneratorWindow : EditorWindow
    {
        const string PrefKey = "Kmnk.FaceTrackingGenerator.AvatarTable";
        const string Root = "Assets/Kmnk/FaceTrackingGenerator";

        DefaultAsset table;
        TableModel model;
        string message = "";
        Vector2 scroll;
        readonly AnimationClip[] afk = new AnimationClip[6];
        static readonly string[] AfkLabels = { "Action Intro", "Action Loop", "Action Outro", "FX Intro", "FX Loop", "FX Outro" };
        string newName = "MyAvatar";
        int curveInput;
        // Unity imports .txt as TextAsset, so the field accepts any asset (a .txt file is expected).
        UnityEngine.Object scenario;

        [MenuItem("Tools/Kmnk/Face Tracking Generator")]
        static void Open() => GetWindow<GeneratorWindow>("Face Tracking Generator");

        void OnEnable()
        {
            var path = EditorPrefs.GetString(PrefKey, "");
            if (path != "") table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Reload();
            RemovedFiles.PromptIfAny();
        }

        string TablePath => table != null ? AssetDatabase.GetAssetPath(table) : null;

        void Reload()
        {
            model = null;
            if (TablePath == null) return;
            try
            {
                model = TableLoader.Load(TablePath);
                var c = model.AfkClips;
                var refs = new[] { c.ActionIntro, c.ActionLoop, c.ActionOutro, c.FxIntro, c.FxLoop, c.FxOutro };
                for (int i = 0; i < 6; i++) afk[i] = AfkBuilder.Load(refs[i]);
            }
            catch (Exception e) { message = "Table error: " + e.Message; }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Avatar table", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            table = (DefaultAsset)EditorGUILayout.ObjectField("avatar.jsonc", table, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) { EditorPrefs.SetString(PrefKey, TablePath ?? ""); Reload(); }
            if (GUILayout.Button("Reload tables")) Reload();
            if (model != null)
                EditorGUILayout.HelpBox($"{model.AvatarName}: inputs {model.Inputs.Count}, switches {model.Switches.Count}, trees {model.Trees.Count}, parts {model.Parts.Count}, dropped {model.Dropped.Count}, warnings {model.Warnings.Count}", MessageType.None);

            using (new EditorGUI.DisabledScope(model == null))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generate and check", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Check")) Do(() =>
                    {
                        var fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(model.OutputFolder + "/FX.controller");
                        var f = StaticChecker.Check(model, fx);
                        return f.Count == 0 ? "Check: no findings" : "Check: " + f.Count + " findings\n" + string.Join("\n", f);
                    });
                    if (GUILayout.Button("Generate")) Do(() =>
                    {
                        var r = Generator.Generate(model);
                        return "Generated\n" + string.Join("\n", r.Log.Concat(r.Written.Select(w => "wrote " + w)));
                    });
                    if (GUILayout.Button("Add toggles to menu")) Do(() => "Menu\n" + string.Join("\n", MenuInstaller.Install(model)));
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("AFK clips (saved in the avatar table)", EditorStyles.boldLabel);
                for (int i = 0; i < 6; i++) afk[i] = (AnimationClip)EditorGUILayout.ObjectField(AfkLabels[i], afk[i], typeof(AnimationClip), false);
                if (GUILayout.Button("Save AFK clips to the table")) Do(() =>
                {
                    TableWriter.SetAfkClips(TablePath, afk);
                    Reload();
                    return "Saved AFK clips to " + TablePath;
                });

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Response curves (no generation needed)", EditorStyles.boldLabel);
                if (model != null) Curves();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Scenario check (Play Mode)", EditorStyles.boldLabel);
                scenario = EditorGUILayout.ObjectField("scenario .txt", scenario, typeof(UnityEngine.Object), false);
                if (scenario != null && !AssetDatabase.GetAssetPath(scenario).EndsWith(".txt")) EditorGUILayout.HelpBox("Choose a scenario .txt file.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(scenario == null || !AssetDatabase.GetAssetPath(scenario).EndsWith(".txt") || model?.VariantPath == null || model?.DefaultMesh == null))
                    if (GUILayout.Button("Run scenario on the generated prefab")) Do(() =>
                    {
                        var sp = AssetDatabase.GetAssetPath(scenario);
                        var outPath = Path.ChangeExtension(sp, null) + ".last.jsonl";
                        ScenarioRunner.RunInEditor(sp, outPath, model.VariantPath, model.DefaultMesh);
                        return "Running " + sp + " -> " + outPath;
                    });
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New settings from templates", EditorStyles.boldLabel);
            newName = EditorGUILayout.TextField("Avatar name", newName);
            if (GUILayout.Button("Create Settings/" + newName)) Do(() =>
            {
                var dest = TableWriter.CreateFromTemplates(Root, newName);
                table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(dest);
                EditorPrefs.SetString(PrefKey, dest);
                Reload();
                return "Created " + dest;
            });

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void Do(Func<string> action)
        {
            try { message = action(); }
            catch (Exception e) { message = "Error: " + e.Message; Debug.LogException(e); }
        }

        static readonly Color[] Colors = { Color.red, Color.green, Color.cyan, Color.yellow, Color.magenta, new Color(1, 0.5f, 0), Color.white, new Color(0.6f, 0.6f, 1) };

        void Curves()
        {
            var names = model.Inputs.Select(i => i.Name).ToArray();
            if (names.Length == 0) return;
            curveInput = Mathf.Clamp(EditorGUILayout.Popup("Input", curveInput, names), 0, names.Length - 1);
            var input = names[curveInput];
            var (lo, hi) = CurveEval.Range(model, input);
            const int steps = 60;
            var samples = new List<SortedDictionary<string, float>>();
            for (int i = 0; i <= steps; i++)
                samples.Add(CurveEval.Evaluate(model, new Dictionary<string, float> { [input] = lo + (hi - lo) * i / steps }));
            var shapes = samples.SelectMany(s => s.Keys).Distinct().Where(k => samples.Any(s => s.TryGetValue(k, out var v) && Math.Abs(v) > 0.05f)).ToList();
            var rect = GUILayoutUtility.GetRect(300, 180, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            Handles.color = Color.gray;
            Handles.DrawLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax));
            for (int k = 0; k < shapes.Count; k++)
            {
                var pts = new Vector3[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    var v = samples[i].TryGetValue(shapes[k], out var w) ? w : 0;
                    pts[i] = new Vector3(rect.xMin + rect.width * i / steps, rect.yMax - rect.height * Mathf.Clamp(v, 0, 120) / 120f);
                }
                Handles.color = Colors[k % Colors.Length];
                Handles.DrawAAPolyLine(2, pts);
            }
            EditorGUILayout.LabelField($"x: {input} {lo:0.##} .. {hi:0.##}   y: 0 .. 120");
            for (int k = 0; k < shapes.Count; k++)
            {
                var old = GUI.color;
                GUI.color = Colors[k % Colors.Length];
                EditorGUILayout.LabelField("  " + shapes[k] + $"  (max {samples.Max(s => s.TryGetValue(shapes[k], out var v) ? v : 0):0.#})");
                GUI.color = old;
            }
            EditorGUILayout.HelpBox("Other inputs are 0 and switches are in their default state. Values of 2D BlendTrees are an approximation (gradient band interpolation) and may differ from Unity's.", MessageType.None);
        }
    }

    // Small text edits of the tables that keep comments and the other lines.
    public static class TableWriter
    {
        static readonly string[] AfkKeys = { "actionIntro", "actionLoop", "actionOutro", "fxIntro", "fxLoop", "fxOutro" };

        public static void SetAfkClips(string avatarTable, AnimationClip[] clips)
        {
            var text = File.ReadAllText(avatarTable);
            var sb = new StringBuilder("\"afkClips\": {\n");
            for (int i = 0; i < 6; i++)
            {
                var c = clips[i];
                var v = c == null ? "null" : $"{{\"guid\": \"{AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c))}\", \"path\": \"{AssetDatabase.GetAssetPath(c)}\"}}";
                sb.Append($"    \"{AfkKeys[i]}\": {v}{(i < 5 ? "," : "")}\n");
            }
            sb.Append("  }");
            var start = text.IndexOf("\"afkClips\"", StringComparison.Ordinal);
            if (start >= 0)
            {
                var open = text.IndexOf('{', start);
                int depth = 0, end = open;
                for (; end < text.Length; end++)
                {
                    if (text[end] == '{') depth++;
                    else if (text[end] == '}' && --depth == 0) break;
                }
                text = text.Substring(0, start) + sb + text.Substring(end + 1);
            }
            else
            {
                var last = text.LastIndexOf('}');
                text = text.Substring(0, last).TrimEnd() + ",\n  " + sb + "\n}\n";
            }
            File.WriteAllText(avatarTable, text);
            TableLoader.Read(avatarTable);  // still valid JSON
            AssetDatabase.ImportAsset(avatarTable);
        }

        // Copies the three templates to Settings/<name>/ and returns the avatar table path.
        public static string CreateFromTemplates(string root, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("invalid name: " + name);
            var dir = $"{root}/Settings/{name}";
            if (Directory.Exists(dir)) throw new IOException(dir + " already exists");
            Directory.CreateDirectory(dir);
            foreach (var t in new[] { "input", "expression", "avatar" })
            {
                var text = File.ReadAllText($"{root}/Templates/{t}.template.jsonc");
                if (t == "avatar") text = text.Replace("\"name\": \"MyAvatar\"", $"\"name\": \"{name}\"");
                File.WriteAllText($"{dir}/{t}.jsonc", text);
            }
            AssetDatabase.Refresh();
            return dir + "/avatar.jsonc";
        }
    }
}
