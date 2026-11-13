using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Tools/Kmnk/Face Tracking Generator. Every button calls the same functions as the command line (Cli).
    public sealed class GeneratorWindow : EditorWindow
    {
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
        string menuFace, menuAfk, menuSub;
        // The avatar of steps 2 and 7, remembered per settings file and scene (ProjectSelection), shared with the settings editor.
        GameObject avatar;
        string avatarSource = "none";  // saved, prefab, selected, chosen or none (for the checks)

        [MenuItem("Tools/Kmnk/Face Tracking Generator")]
        static void Open() => GetWindow<GeneratorWindow>("Face Tracking Generator");

        void OnEnable()
        {
            var path = ProjectSelection.TablePath;
            if (path != null) table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Reload();
            RestoreAvatar();
            var savedName = ProjectSelection.AvatarName;
            if (savedName != "") newName = savedName;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        void OnDisable() => EditorSceneManager.sceneOpened -= OnSceneOpened;

        static bool IsSceneAvatar(GameObject go) =>
            go != null && go.scene.IsValid() && go.GetComponent<VRCAvatarDescriptor>() != null;

        GameObject Prefab()
        {
            var p = model?.Prefab;
            if (p == null) return null;
            var path = !string.IsNullOrEmpty(p.Guid) ? AssetDatabase.GUIDToAssetPath(p.Guid) : null;
            if (string.IsNullOrEmpty(path)) path = p.Path;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // Fills the avatar field from what this project chose before for the settings file in the open scenes, else
        // from the only avatar made from the settings file's prefab, else from the selection.
        void RestoreAvatar()
        {
            avatar = ProjectSelection.FindAvatar(TablePath, Prefab(), out avatarSource);
            if (avatar != null) { if (avatarSource == "prefab") ProjectSelection.SetAvatar(TablePath, avatar); }
            else PickSelectedAvatar();
        }

        // An avatar selected in the Hierarchy replaces the field, as the window used the selection before.
        void PickSelectedAvatar()
        {
            var sel = Selection.activeGameObject;
            if (!IsSceneAvatar(sel) || sel == avatar) return;
            avatar = sel;
            avatarSource = "selected";
            ProjectSelection.SetAvatar(TablePath, sel);
        }

        void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            if (avatar != null) return;
            RestoreAvatar();
            Repaint();
        }

        // The settings file field: opens the file, records the choice for this project and brings back its avatar.
        void ChooseTable(DefaultAsset picked)
        {
            table = picked;
            ProjectSelection.SetTable(TablePath);
            Reload();
            RestoreAvatar();
        }

        // The "Avatar in scene" field (none forgets the choice for the settings file in the open scenes).
        internal void ChooseAvatar(GameObject go)
        {
            avatar = go;
            avatarSource = go != null ? "chosen" : "none";
            ProjectSelection.SetAvatar(TablePath, go);
        }

        // The "Avatar name" field: remembered for this project.
        internal void SetAvatarName(string typed)
        {
            newName = typed;
            ProjectSelection.AvatarName = typed;
        }

        // For TableEditorChecks.Capture: what steps 1, 2 and 7 show.
        internal string SelectionState()
        {
            string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
            var a = avatar != null ? HierarchyPath(avatar.transform) : "none";
            var can = (model != null && avatar != null).ToString().ToLower();
            return $"table={TablePath ?? "none"} avatar={a} source={avatarSource} fill={can} attach={can} name={newName}";
        }

        string TablePath => table != null ? AssetDatabase.GetAssetPath(table) : null;

        void Reload()
        {
            model = null;
            menuFace = menuAfk = menuSub = null;
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

        void OnSelectionChange()
        {
            if (model != null) PickSelectedAvatar();
            Repaint();
        }

        // For TableEditorChecks.Capture: open a table without changing the saved choice.
        internal void SetTableForCapture(string path)
        {
            table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Reload();
        }

        // Steps in the order of the README's Use section. The buttons call the same code as before.
        bool showCurves, showAdvanced;

        void Step(string titleKey, string helpKey)
        {
            EditorGUILayout.Space(4);
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));
            EditorGUILayout.LabelField(EditorText.Heading(titleKey), EditorStyles.boldLabel);
            var help = EditorText.Help(helpKey);
            if (help != null) EditorGUILayout.LabelField(help, HelpStyle);
        }

        static GUIStyle helpStyle;
        static GUIStyle HelpStyle
        {
            get
            {
                if (helpStyle == null)
                {
                    helpStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                    helpStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
                }
                return helpStyle;
            }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                table = (DefaultAsset)EditorGUILayout.ObjectField("Settings file", table, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck()) ChooseTable(table);
                if (GUILayout.Button("Reload", GUILayout.Width(70))) Reload();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Help", GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                var lang = EditorGUILayout.Popup((int)EditorText.Current, EditorText.Choices, GUILayout.Width(80));
                if (EditorGUI.EndChangeCheck()) EditorText.Current = (EditorText.Language)lang;
            }
            if (model != null)
                EditorGUILayout.LabelField($"{model.AvatarName}: inputs {model.Inputs.Count(i => !i.IsTrack)}, neutral rows {model.Neutral.Sum(kv => kv.Value.Count)}, state machines {model.StateMachines.Count}, movements {model.Parts.Count}, dropped {model.Dropped.Count}, warnings {model.Warnings.Count}", EditorStyles.miniLabel);

            Step("gen.step1", "gen.help1");
            var createLabel = "Create Settings/" + newName;
            var fromLabel = createLabel + " from a folder…";
            // Both buttons get the width of the longer label, right-aligned one above the other.
            var createWidth = Mathf.Max(GUI.skin.button.CalcSize(new GUIContent(createLabel)).x, GUI.skin.button.CalcSize(new GUIContent(fromLabel)).x);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var typed = EditorGUILayout.TextField("Avatar name", newName);
                if (EditorGUI.EndChangeCheck()) SetAvatarName(typed);
                if (GUILayout.Button(createLabel, GUILayout.Width(createWidth))) Do(() => SelectCreated(TableWriter.CreateFromTemplates(newName)));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(fromLabel, GUILayout.Width(createWidth)))
                {
                    var folder = EditorUtility.OpenFolderPanel(fromLabel, "", "");
                    if (!string.IsNullOrEmpty(folder)) Do(() => SelectCreated(TableShare.CreateFromFolder(newName, folder)));
                    GUIUtility.ExitGUI();  // the folder panel was modal; start the layout again
                }
            }

            using (new EditorGUI.DisabledScope(model == null))
            {
                Step("gen.step2", "gen.help2");
                var sel = avatar;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var picked = (GameObject)EditorGUILayout.ObjectField("Avatar in scene", avatar, typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck() && (picked == null || picked.scene.IsValid())) { ChooseAvatar(picked); sel = picked; }
                    using (new EditorGUI.DisabledScope(sel == null))
                        if (GUILayout.Button("Fill prefab, mesh and menu from it")) Do(() =>
                        {
                            var log = AvatarSetup.FillTable(TablePath, sel);
                            Reload();
                            return "Filled the avatar table\n" + string.Join("\n", log);
                        });
                }

                Step("gen.step3", "gen.help3");
                if (GUILayout.Button("Open Settings Editor", GUILayout.Width(180))) TableEditorWindow.OpenTable(TablePath);

                Step("gen.step4", "gen.help4");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Check")) Do(() =>
                    {
                        var fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(model.OutputFolder + "/FX.controller");
                        var f = StaticChecker.Check(model, fx);
                        return f.Count == 0 ? "Check: no findings" : "Check: " + f.Count + " findings\n" + string.Join("\n", f);
                    });
                    showCurves = GUILayout.Toggle(showCurves, "Response curves", "Button");
                }
                if (showCurves && model != null) Curves();

                Step("gen.step5", "gen.help5");
                if (model != null)
                {
                    menuFace = EditorGUILayout.TextField("FT toggle name", menuFace ?? model.FaceTrackLabel);
                    menuAfk = EditorGUILayout.TextField("AFK toggle name", menuAfk ?? model.AfkPlayLabel);
                    menuSub = EditorGUILayout.TextField("Submenu (empty: none)", menuSub ?? model.SubMenu ?? "");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save names")) Do(() =>
                        {
                            TableWriter.SetTopLevel(TablePath, "faceTrackLabel", TableWriter.StringJson(menuFace));
                            TableWriter.SetTopLevel(TablePath, "afkPlayLabel", TableWriter.StringJson(menuAfk));
                            TableWriter.SetTopLevel(TablePath, "subMenu", string.IsNullOrWhiteSpace(menuSub) ? "null" : TableWriter.StringJson(menuSub));
                            Reload();
                            return "Saved toggle names. Toggles already in the menu keep their names; rename them in the menu asset.";
                        });
                        if (GUILayout.Button("Add toggles to menu")) Do(() => "Menu\n" + string.Join("\n", MenuInstaller.Install(model)));
                    }
                }

                Step("gen.step6", "gen.help6");
                for (int i = 0; i < 6; i++) afk[i] = (AnimationClip)EditorGUILayout.ObjectField(AfkLabels[i], afk[i], typeof(AnimationClip), false);
                if (GUILayout.Button("Save AFK clips")) Do(() =>
                {
                    TableWriter.SetAfkClips(TablePath, afk);
                    Reload();
                    return "Saved AFK clips to " + TablePath;
                });

                Step("gen.step7", "gen.help7");
                using (new EditorGUI.DisabledScope(sel == null))
                    if (GUILayout.Button("Add settings component (build-time FT)")) Do(() =>
                    {
                        AvatarSetup.AddSettings(sel, TablePath);
                        return $"Added Face Tracking Generator Settings to {sel.name}. FT is generated on Play and upload.";
                    });

                EditorGUILayout.Space(6);
                showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced: Generate files, Scenario check", true);
                if (showAdvanced)
                {
                    var adv = EditorText.Help("gen.helpAdvanced");
                    if (adv != null) EditorGUILayout.LabelField(adv, HelpStyle);
                    if (GUILayout.Button("Generate")) Do(() =>
                    {
                        var r = Generator.Generate(model);
                        return "Generated\n" + string.Join("\n", r.Log.Concat(r.Written.Select(w => "wrote " + w)));
                    });
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
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // After Create Settings: the new settings become the chosen ones, as if picked in Settings file.
        string SelectCreated(string dest)
        {
            table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(dest);
            ProjectSelection.SetTable(dest);
            Reload();
            RestoreAvatar();
            return "Created " + dest;
        }

        void Do(Func<string> action)
        {
            try { message = action(); }
            catch (Exception e) { message = "Error: " + e.Message; Debug.LogException(e); }
        }

        static readonly Color[] Colors = { Color.red, Color.green, Color.cyan, Color.yellow, Color.magenta, new Color(1, 0.5f, 0), Color.white, new Color(0.6f, 0.6f, 1) };

        void Curves()
        {
            var names = model.Inputs.Where(i => i.IsTrack).Select(i => i.Name).ToArray();
            if (names.Length == 0) return;
            curveInput = Mathf.Clamp(EditorGUILayout.Popup("Input", curveInput, names), 0, names.Length - 1);
            var input = names[curveInput];
            var (lo, hi) = FaceEval.Range(model, input);
            const int steps = 60;
            var samples = new List<SortedDictionary<string, float>>();
            for (int i = 0; i <= steps; i++)
                samples.Add(FaceEval.Evaluate(model, new Dictionary<string, float> { [input] = lo + (hi - lo) * i / steps }));
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
            EditorGUILayout.HelpBox("Other keys are 0 and the state machines are in their default state.", MessageType.None);
        }
    }

    // Small text edits of the tables that keep comments and the other lines.
    public static class TableWriter
    {
        static readonly string[] AfkKeys = { "actionIntro", "actionLoop", "actionOutro", "fxIntro", "fxLoop", "fxOutro" };

        public static void SetAfkClips(string avatarTable, AnimationClip[] clips)
        {
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < 6; i++)
            {
                var c = clips[i];
                sb.Append($"    \"{AfkKeys[i]}\": {(c == null ? "null" : AssetRefJson(c))}{(i < 5 ? "," : "")}\n");
            }
            sb.Append("  }");
            SetTopLevel(avatarTable, "afkClips", sb.ToString());
        }

        public static string AssetRefJson(UnityEngine.Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            return $"{{\"guid\": \"{AssetDatabase.AssetPathToGUID(path)}\", \"path\": \"{path}\"}}";
        }

        public static string StringJson(string s) => Newtonsoft.Json.JsonConvert.ToString(s);

        // Replaces the value of a top-level key ("  \"key\": value" at two spaces), keeping comments and the other
        // lines. Adds the key at the end when it is missing. valueJson must be valid JSON.
        public static void SetTopLevel(string avatarTable, string key, string valueJson)
        {
            var text = File.ReadAllText(avatarTable);
            var m = System.Text.RegularExpressions.Regex.Match(text, "(?m)^  \"" + System.Text.RegularExpressions.Regex.Escape(key) + "\":[ \\t]*");
            if (m.Success)
            {
                var start = m.Index + m.Length;
                var end = ValueEnd(text, start);
                text = text.Substring(0, start) + valueJson + text.Substring(end);
            }
            else
            {
                var last = text.LastIndexOf('}');
                text = text.Substring(0, last).TrimEnd() + ",\n  \"" + key + "\": " + valueJson + "\n}\n";
            }
            File.WriteAllText(avatarTable, text);
            TableLoader.Read(avatarTable);  // still valid JSON
            AssetDatabase.ImportAsset(avatarTable);
        }

        // End index (exclusive) of the JSON value that starts at i.
        static int ValueEnd(string t, int i)
        {
            if (t[i] == '{' || t[i] == '[')
            {
                int depth = 0; var inStr = false;
                for (int k = i; k < t.Length; k++)
                {
                    var c = t[k];
                    if (inStr) { if (c == '\\') k++; else if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return k + 1;
                }
                throw new FormatException("unbalanced value");
            }
            if (t[i] == '"')
            {
                for (int k = i + 1; k < t.Length; k++)
                {
                    if (t[k] == '\\') { k++; continue; }
                    if (t[k] == '"') return k + 1;
                }
                throw new FormatException("unterminated string");
            }
            int e = i;
            while (e < t.Length && t[e] != ',' && t[e] != '\n' && t[e] != '}') e++;
            return e;
        }

        // Copies the two templates from the package to Settings/<name>/ and returns the avatar table path.
        public static string CreateFromTemplates(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("invalid name: " + name);
            var dir = $"{Paths.SettingsRoot}/{name}";
            if (Directory.Exists(dir)) throw new IOException(dir + " already exists");
            Directory.CreateDirectory(dir);
            foreach (var t in new[] { "expression", "avatar" })
            {
                var text = File.ReadAllText($"{Paths.Templates}/{t}.template.jsonc");
                if (t == "avatar") text = text.Replace("\"name\": \"MyAvatar\"", $"\"name\": \"{name}\"");
                File.WriteAllText($"{dir}/{t}.jsonc", text);
            }
            AssetDatabase.Refresh();
            return dir + "/avatar.jsonc";
        }
    }
}
