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
        string message = "", loadError;
        Vector2 scroll;
        string newName = "MyAvatar";
        int curveInput;
        // Unity imports .txt as TextAsset, so the field accepts any asset (a .txt file is expected).
        UnityEngine.Object scenario;
        // The avatar of step 4, remembered per settings file and scene (ProjectSelection), shared with the settings editor.
        GameObject avatar;
        string avatarSource = "none";  // saved, prefab, selected, chosen or none (for the checks)

        internal const string ManualUrl = "https://github.com/kmnk/VRChat_FaceTrackingGenerator#use";

        [MenuItem("Tools/Kmnk/Face Tracking Generator", false, 0)]
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
            ClearMessage();
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

        // For TableEditorChecks.Capture: what steps 1 and 4 show.
        internal string SelectionState()
        {
            string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
            var a = avatar != null ? HierarchyPath(avatar.transform) : "none";
            var can = (model != null && avatar != null).ToString().ToLower();
            return $"table={TablePath ?? "none"} avatar={a} source={avatarSource} attach={can} name={newName}";
        }

        string TablePath => table != null ? AssetDatabase.GetAssetPath(table) : null;

        void Reload()
        {
            model = null;
            loadError = null;
            tableTime = expressionTime = default;
            if (TablePath == null) return;
            tableTime = File.Exists(TablePath) ? File.GetLastWriteTimeUtc(TablePath) : default;
            var ep = TableLoader.ExpressionPath(TablePath);
            expressionTime = ep != null && File.Exists(ep) ? File.GetLastWriteTimeUtc(ep) : default;
            try { model = TableLoader.Load(TablePath); }
            catch (Exception e) { loadError = e.Message; }
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

        // Steps in the order of the README's Use section. The buttons call the same code as the command line.
        bool showCurves, showAdvanced;
        // The result of the last button, shown under the step that has the button (1 to 4, 5 Generate, 6 Scenario).
        int messageStep;
        MessageType messageType = MessageType.None;
        DateTime tableTime, expressionTime;

        void Step(string titleKey, string helpKey)
        {
            EditorGUILayout.Space(4);
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));
            EditorGUILayout.LabelField(EditorText.Heading(titleKey), EditorStyles.boldLabel);
            var help = helpKey != null ? EditorText.Help(helpKey) : null;
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

        // The settings files changed on disk (saved by the settings editor, or edited by hand): read them again, so that
        // Check and the other buttons always see the saved settings.
        void ReloadWhenChanged()
        {
            if (TablePath == null) return;
            var t = File.Exists(TablePath) ? File.GetLastWriteTimeUtc(TablePath) : default;
            var ep = TableLoader.ExpressionPath(TablePath);
            var e = ep != null && File.Exists(ep) ? File.GetLastWriteTimeUtc(ep) : default;
            if (t == tableTime && e == expressionTime) return;
            Reload();
        }

        // Settings of the typed name are there already (Create does not overwrite them).
        bool SettingsExist() => !string.IsNullOrWhiteSpace(newName) && Directory.Exists($"{Paths.SettingsRoot}/{newName}");
        (string, MessageType) Exists() => (string.Format(EditorText.Get("gen.exists"), newName), MessageType.Warning);

        void ClearMessage() { message = ""; messageStep = 0; messageType = MessageType.None; }

        void ShowMessage(int step)
        {
            if (messageStep == step && !string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
        }

        void OnGUI()
        {
            ReloadWhenChanged();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            table = (DefaultAsset)EditorGUILayout.ObjectField(EditorText.Label("Settings file", "tip.table"), table, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) ChooseTable(table);
            using (new EditorGUILayout.HorizontalScope())
            {
                // The manual (README) has the details the steps leave out.
                if (EditorGUILayout.LinkButton("Manual")) Application.OpenURL(ManualUrl);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Help", GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                var lang = EditorGUILayout.Popup((int)EditorText.Current, EditorText.Choices, GUILayout.Width(80));
                if (EditorGUI.EndChangeCheck()) EditorText.Current = (EditorText.Language)lang;
            }
            if (TablePath == null) EditorGUILayout.HelpBox(EditorText.Get("gen.noTable"), MessageType.Warning);
            else if (model == null && loadError != null) EditorGUILayout.HelpBox(string.Format(EditorText.Get("gen.tableError"), loadError), MessageType.Warning);

            Step("gen.step1", null);
            // The buttons go under the name when the window is too narrow for one row.
            var oneRow = position.width >= EditorGUIUtility.labelWidth + 120 + 70 + 150 + 30;
            if (oneRow) EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var typed = EditorGUILayout.TextField("Avatar name", newName);
            if (EditorGUI.EndChangeCheck()) SetAvatarName(typed);
            if (!oneRow) { EditorGUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); }
            if (GUILayout.Button("Create", GUILayout.Width(70)))
                Do(1, () => SettingsExist() ? Exists() : (SelectCreated(TableWriter.CreateFromTemplates(newName)), MessageType.Info));
            if (GUILayout.Button("Create from Folder…", GUILayout.Width(150)))
            {
                ClearMessage();
                // Settings of that name are never overwritten: say so before asking for the folder.
                if (SettingsExist()) { Do(1, Exists); EditorGUILayout.EndHorizontal(); GUIUtility.ExitGUI(); }
                var folder = EditorUtility.OpenFolderPanel("Create from Folder", "", "");
                if (!string.IsNullOrEmpty(folder)) Do(1, () => (SelectCreated(TableShare.CreateFromFolder(newName, folder)), MessageType.Info));
                EditorGUILayout.EndHorizontal();
                GUIUtility.ExitGUI();  // the folder panel was modal; start the layout again
            }
            EditorGUILayout.EndHorizontal();
            ShowMessage(1);

            using (new EditorGUI.DisabledScope(model == null))
            {
                Step("gen.step2", null);
                if (GUILayout.Button("Open Settings Editor", GUILayout.Width(180))) { ClearMessage(); TableEditorWindow.OpenTable(TablePath); }

                Step("gen.step3", "gen.help3");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Check")) Do(3, Check);
                    var curves = GUILayout.Toggle(showCurves, "Response curves", "Button");
                    if (curves != showCurves) { ClearMessage(); showCurves = curves; }
                }
                ShowMessage(3);
                if (showCurves && model != null) Curves();

                Step("gen.step4", null);
                var sel = avatar;
                EditorGUI.BeginChangeCheck();
                var picked = (GameObject)EditorGUILayout.ObjectField("Avatar in scene", avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck() && (picked == null || picked.scene.IsValid())) { ChooseAvatar(picked); sel = picked; }
                using (new EditorGUI.DisabledScope(sel == null))
                    if (GUILayout.Button("Add Settings Component")) Do(4, () =>
                    {
                        AvatarSetup.AddSettings(sel, TablePath);
                        return (string.Format(EditorText.Get("gen.attached"), sel.name), MessageType.Info);
                    });
                ShowMessage(4);

                EditorGUILayout.Space(6);
                showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced: Generate files, Scenario check", true);
                if (showAdvanced)
                {
                    if (GUILayout.Button("Generate")) Do(5, () =>
                    {
                        Reload();
                        var r = Generator.Generate(model);
                        var warn = r.Log.Any(l => l.StartsWith("warning:") || l.StartsWith("notice:"));
                        return (string.Format(EditorText.Get("gen.generated"), string.Join("\n", r.Log.Concat(r.Written.Select(w => "wrote " + w)))), warn ? MessageType.Warning : MessageType.Info);
                    });
                    ShowMessage(5);
                    EditorGUILayout.LabelField("Scenario check (Play Mode)", EditorStyles.boldLabel);
                    scenario = EditorGUILayout.ObjectField("scenario .txt", scenario, typeof(UnityEngine.Object), false);
                    if (scenario != null && !AssetDatabase.GetAssetPath(scenario).EndsWith(".txt")) EditorGUILayout.HelpBox(EditorText.Get("gen.scenarioTxt"), MessageType.Warning);
                    using (new EditorGUI.DisabledScope(scenario == null || !AssetDatabase.GetAssetPath(scenario).EndsWith(".txt") || model?.VariantPath == null || model?.DefaultMesh == null))
                        if (GUILayout.Button("Run scenario on the generated prefab")) Do(6, () =>
                        {
                            var sp = AssetDatabase.GetAssetPath(scenario);
                            var outPath = Path.ChangeExtension(sp, null) + ".last.jsonl";
                            ScenarioRunner.RunInEditor(sp, outPath, model.VariantPath, model.DefaultMesh);
                            return (string.Format(EditorText.Get("gen.running"), sp, outPath), MessageType.Info);
                        });
                    ShowMessage(6);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // Check: the saved settings (read again first) and the generated FX.
        (string, MessageType) Check()
        {
            Reload();
            if (model == null) throw new TableLoader.TableException(loadError ?? "no settings file");
            var fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(FtNames.Controller(model.OutputFolder, "FX"));
            var f = StaticChecker.Check(model, fx);
            var refs = AssetRefs.Warnings(model);
            var text = f.Count == 0 ? EditorText.Get("gen.checkNone") : string.Format(EditorText.Get("gen.checkFindings"), f.Count, string.Join("\n", f));
            if (refs.Count > 0) text += "\n" + string.Join("\n", refs);
            // Face movements still Not set: one line first, not counted as findings.
            if (model.UnsetParts.Count > 0) text = string.Format(EditorText.Get("gen.unset"), model.UnsetParts.Count) + "\n" + text;
            return (text, f.Count == 0 && refs.Count == 0 ? MessageType.Info : MessageType.Warning);
        }

        // For the checks: press Check as the button does, and what the result shows.
        internal void PressCheckForCapture() => Do(3, Check);
        internal void PressCreateForCapture() => Do(1, () => SettingsExist() ? Exists() : (SelectCreated(TableWriter.CreateFromTemplates(newName)), MessageType.Info));
        internal string MessageState() => $"message type={(messageType == MessageType.Error ? "error" : messageType == MessageType.Warning ? "warning" : messageType == MessageType.Info ? "info" : "none")} step={messageStep}";

        // After Create: the new settings become the chosen ones, as if picked in Settings file.
        string SelectCreated(string dest)
        {
            table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(dest);
            ProjectSelection.SetTable(dest);
            Reload();
            RestoreAvatar();
            return string.Format(EditorText.Get("gen.created"), dest);
        }

        void Do(int step, Func<(string text, MessageType type)> action)
        {
            messageStep = step;
            try { (message, messageType) = action(); }
            catch (Exception e) { message = string.Format(EditorText.Get("gen.failed"), e.Message); messageType = MessageType.Error; Debug.LogException(e); }
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
            EditorGUILayout.HelpBox(EditorText.Get("gen.curvesNote"), MessageType.None);
        }
    }
}
