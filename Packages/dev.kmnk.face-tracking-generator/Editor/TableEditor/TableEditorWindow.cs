using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // Tools/Kmnk/Face Tracking Settings Editor (the settings editor): edits the avatar table and shows the tables' weights on an avatar in
    // the scene. The tables are edited as JSON trees (TableDocument) and written only on Save.
    public sealed class TableEditorWindow : EditorWindow
    {
        const string TablePrefKey = "Kmnk.FaceTrackingGenerator.AvatarTable";  // shared with GeneratorWindow
        static readonly string[] Tabs = { "Avatar", "Preview", "Tuning", "Inputs" };
        static readonly string[] ControllerLabels = { "FX", "Gesture", "Action" };
        static readonly VRCAvatarDescriptor.AnimLayerType[] ControllerTypes =
            { VRCAvatarDescriptor.AnimLayerType.FX, VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action };
        static readonly Regex NotInAvatar = new Regex(@"part (\S+) is not in the avatar layer");

        DefaultAsset table;
        TableDocument doc;
        TableModel model;
        string error;
        TableEditState state;
        readonly Dictionary<string, JToken> removedParts = new Dictionary<string, JToken>();

        int tab;
        Vector2 scroll;
        string partFilter = "";
        int controller;

        // preview
        GameObject sceneAvatar;
        readonly FacePreview preview = new FacePreview();
        readonly SwitchTracker switches = new SwitchTracker();
        readonly Dictionary<string, float> inputs = new Dictionary<string, float>();
        HashSet<string> notOnAvatar = new HashSet<string>();
        bool nothingApplied;
        string selectedPart;
        bool showUnusedInputs, showSwitches;
        readonly Dictionary<string, float> unusedInputs = new Dictionary<string, float>();
        const string DevicePrefKey = "Kmnk.FaceTrackingGenerator.PreviewDevice";
        string captureDevice;  // set by TableEditorChecks.Capture instead of the saved choice

        // tuning: open folds ("<tree>" and "<tree>/<node>"), try values per node, and the node shown in Face view
        readonly Dictionary<string, bool> tuneOpen = new Dictionary<string, bool>();
        readonly Dictionary<string, float> tuneTry = new Dictionary<string, float>();
        string tuneShown;
        string tuneScrollTo;  // a fold key to scroll to on the next repaint (from the expression graph)

        // inputs tab: the search text, and what Rebuild counted (the avatar's own synced bits, why saving is blocked)
        string inputSearch = "";
        int avatarBits = -1;
        string saveBlocked;

        // The open settings editor, for the expression graph. Changed is raised after the tables change.
        internal static TableEditorWindow Current { get; private set; }
        internal static event Action Changed;

        [MenuItem("Tools/Kmnk/Face Tracking Settings Editor")]
        static void Open() => GetWindow<TableEditorWindow>("FT Settings Editor");

        // Opens the settings editor on a table (the generator window's "Open Settings Editor").
        public static TableEditorWindow OpenTable(string avatarTablePath)
        {
            var w = GetWindow<TableEditorWindow>("FT Settings Editor");
            if (!string.IsNullOrEmpty(avatarTablePath) && w.TablePath != avatarTablePath && w.ConfirmDiscard())
            {
                w.table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(avatarTablePath);
                EditorPrefs.SetString(TablePrefKey, avatarTablePath);
                w.Load();
            }
            return w;
        }

        internal string OpenedTablePath => doc?.AvatarPath;

        void OnEnable()
        {
            titleContent = new GUIContent("FT Settings Editor");
            Current = this;
            state = CreateInstance<TableEditState>();
            state.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayMode;
            BuildGuard.Changed += Repaint;
            var path = EditorPrefs.GetString(TablePrefKey, "");
            if (path != "") table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Load();
            PickSelectedAvatar();
        }

        void OnDisable()
        {
            preview.Stop();
            if (Current == this) { Current = null; Changed?.Invoke(); }
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            BuildGuard.Changed -= Repaint;
            if (state != null) { Undo.ClearUndo(state); DestroyImmediate(state); }
        }

        void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) preview.Stop();
            Repaint();
        }

        void OnSelectionChange()
        {
            if (sceneAvatar == null) PickSelectedAvatar();
            Repaint();
        }

        void PickSelectedAvatar()
        {
            var sel = Selection.activeGameObject;
            if (sel != null && sel.scene.IsValid() && sel.GetComponent<VRCAvatarDescriptor>() != null) sceneAvatar = sel;
        }

        string TablePath => table != null ? AssetDatabase.GetAssetPath(table) : null;

        // ---- loading, editing, saving

        void Load()
        {
            preview.Stop();
            doc = null; model = null; error = null;
            avatarBits = -1; saveBlocked = null;
            pendingParts.Clear();
            removedParts.Clear();
            switches.Reset();
            UpdateDirty();
            if (TablePath == null) return;
            try
            {
                doc = TableDocument.Open(TablePath);
                state.snapshot = doc.Snapshot();
                Rebuild();
            }
            catch (Exception e) { doc = null; error = e.Message; }
            UpdateDirty();
            if (Current == this) Changed?.Invoke();
        }

        void Rebuild()
        {
            try { model = doc.Model(); error = null; }
            catch (Exception e) { model = null; error = e.Message; }
            avatarBits = model != null ? StaticChecker.AvatarSyncedBits(model) : -1;
            saveBlocked = doc.SaveBlocked();
            if (preview.Running) ApplyPreview();
            else if (tab == 2 && tuneShown != null) ShowTuning(tuneShown);
            else ShowSelected();
            if (Current == this) Changed?.Invoke();
        }

        // The tooltip of the input a Tuning node moves with (the first side of a left/right pair).
        string NodeInputTooltip(Tuning.Tree t, string input)
        {
            var name = Tuning.Expanded(t, input).FirstOrDefault();
            return model == null || name == null ? null : ParameterHelp.Tooltip(model, model.Input(name));
        }

        // ---- Inputs tab

        Devices.Device ChosenDevice(List<Devices.Device> devices) =>
            devices.FirstOrDefault(d => d.Id == (captureDevice ?? EditorPrefs.GetString(DevicePrefKey, "")));

        InputChoices.Result InputRows() => InputChoices.Rows(doc.Json(TableDocument.Input), doc.Json(TableDocument.Expression), model,
            ChosenDevice(Devices.Load()), doc.SavedJson(TableDocument.Input), avatarBits);

        static readonly Color OverColor = new Color(0.75f, 0.22f, 0.17f);

        static GUIStyle lockedNameStyle;
        static GUIStyle LockedNameStyle
        {
            get
            {
                if (lockedNameStyle != null) return lockedNameStyle;
                lockedNameStyle = new GUIStyle(EditorStyles.label);
                lockedNameStyle.normal.textColor = EditorStyles.label.normal.textColor * new Color(1, 1, 1, 0.5f);
                return lockedNameStyle;
            }
        }

        void DrawInputsTab()
        {
            EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
            Help("help.inputsTab");

            var devices = Devices.Load();
            var deviceNames = new List<string> { "Settings" };
            deviceNames.AddRange(devices.Select(d => d.Name));
            var device = ChosenDevice(devices);
            var deviceIndex = device == null ? 0 : devices.IndexOf(device) + 1;
            EditorGUI.BeginChangeCheck();
            deviceIndex = EditorGUILayout.Popup("Device", deviceIndex, deviceNames.ToArray());
            if (EditorGUI.EndChangeCheck()) { captureDevice = null; EditorPrefs.SetString(DevicePrefKey, deviceIndex == 0 ? "" : devices[deviceIndex - 1].Id); }

            var r = InputRows();
            EditorGUILayout.Space(4);
            var old = GUI.contentColor;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Synced", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                if (r.Over) GUI.contentColor = new Color(1f, 0.45f, 0.4f);  // lighter than the bar, to read on the dark skin
                GUILayout.Label($"{r.Total} / {StaticChecker.MaxSyncedBits} bits", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                GUI.contentColor = old;
            }
            var bar = GUILayoutUtility.GetRect(100, 8, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bar, new Color(0.165f, 0.165f, 0.165f));
                var fill = bar;
                fill.width = bar.width * Mathf.Clamp01(r.Total / (float)StaticChecker.MaxSyncedBits);
                EditorGUI.DrawRect(fill, r.Over ? OverColor : new Color(0.227f, 0.475f, 0.733f));
            }
            PartsLine(r.AvatarBits >= 0 ? string.Format(EditorText.Get("inputs.breakdown"), r.SavedBits, r.AddedBits, r.AvatarBits)
                : string.Format(EditorText.Get("inputs.breakdownNoAvatar"), r.SavedBits, r.AddedBits));
            if (r.Over) EditorGUILayout.HelpBox(EditorText.Get("inputs.over"), MessageType.Error);
            EditorGUILayout.Space(2);
            inputSearch = EditorGUILayout.TextField(inputSearch, EditorStyles.toolbarSearchField);
            bool Shown(InputChoices.Row row) => string.IsNullOrEmpty(inputSearch) || row.Display.IndexOf(inputSearch, StringComparison.OrdinalIgnoreCase) >= 0;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("In the settings", EditorStyles.boldLabel);
            foreach (var row in r.InTable.Where(Shown)) DrawInputRow(row);
            if (device != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Not in the settings", EditorStyles.boldLabel);
                foreach (var row in r.NotInTable.Where(Shown)) DrawInputRow(row);
            }
        }

        void DrawInputRow(InputChoices.Row row)
        {
            var area = EditorGUILayout.BeginVertical();
            if (row.InTable && row.Added && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0, area.y, area.width + area.x * 2, area.height), new Color(0.17f, 0.29f, 0.4f, 0.8f));
            var note = row.InTable ? InputChoices.Note(row) : null;
            using (new EditorGUILayout.HorizontalScope())
            {
                // The check box is disabled for a locked row; the name stays enabled (gray) so that its tooltip shows.
                var locked = row.InTable && row.Locked;
                using (new EditorGUI.DisabledScope(locked))
                {
                    EditorGUI.BeginChangeCheck();
                    var on = EditorGUILayout.Toggle(row.InTable, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (on && !row.InTable) Edit(TableDocument.Input, "Set FT input", j => InputChoices.Add(j, row, ChosenDevice(Devices.Load())));
                        else if (!on && row.InTable) Edit(TableDocument.Input, "Set FT input", j => InputChoices.Remove(j, row));
                        GUIUtility.ExitGUI();
                    }
                }
                var nameStyle = locked ? LockedNameStyle : EditorStyles.label;
                GUILayout.Label(new GUIContent(row.Display, InputChoices.Tooltip(row)), nameStyle, GUILayout.ExpandWidth(true));
                if (row.InTable)
                {
                    EditorGUI.BeginChangeCheck();
                    var synced = EditorGUILayout.Popup(row.Sync == "float8" ? 0 : 1, new[] { "Synced", "Local" }, GUILayout.Width(80)) == 0;
                    if (EditorGUI.EndChangeCheck()) Edit(TableDocument.Input, "Set FT input", j => InputChoices.SetSync(j, row.Name, synced));
                }
                GUILayout.Label($"{row.Bits} bits", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
            }
            if (note != null) { EditorGUI.indentLevel++; PartsLine(note); EditorGUI.indentLevel--; }
            EditorGUILayout.EndVertical();
        }

        internal bool PreviewRunning => preview.Running;

        internal void StopPreviewForBuild()
        {
            preview.Stop();
            Repaint();
        }

        // ---- the expression graph

        internal JObject ExpressionJson => doc?.Json(TableDocument.Expression);
        internal JObject InputJson => doc?.Json(TableDocument.Input);

        // For the expression graph's lists: the device chosen in Device (null for Settings) and the rows of the Inputs tab.
        internal (Devices.Device device, InputChoices.Result rows) DeviceInputs()
        {
            var device = doc == null ? null : ChosenDevice(Devices.Load());
            return (device, device == null ? null : InputRows());
        }

        // Adds a device's input to the input table as the Inputs tab does. Returns the synced bits after it, or -1.
        internal int AddDeviceInput(string name)
        {
            if (doc == null) return -1;
            var row = InputRows().NotInTable.FirstOrDefault(r => r.Name == name);
            if (row == null) return -1;
            Edit(TableDocument.Input, "Add FT input", j => InputChoices.Add(j, row, ChosenDevice(Devices.Load())));
            return InputRows().Total;
        }

        // Opens the Tuning tab at a node: the tree's fold, the node's fold (1D), and scrolls to it. A null key (a switch)
        // opens the tree only.
        // The box selected in the expression graph (for Show: Selected).
        string graphTree, graphPath;
        const string ShowSelectedPrefKey = "Kmnk.FaceTrackingGenerator.TuningShowSelected";

        internal void RevealInTuning(string tree, string key, string path = null)
        {
            if (doc == null || tree == null) return;
            graphTree = tree;
            graphPath = path;
            tab = 2;
            tuneOpen[tree] = true;
            if (key != null)
            {
                if (key.IndexOf("/2d/", StringComparison.Ordinal) < 0) tuneOpen[key] = true;
                tuneScrollTo = key;
                ShowTuning(key);
            }
            else tuneScrollTo = tree;
            Repaint();
        }

        internal string SplitPosition2D(string tree, string path, int row, string inputX, string inputY)
        {
            if (doc == null || !ExpressionGraph.CanSplit(ExpressionJson, tree, path, row)) return null;
            string made = null;
            Edit(TableDocument.Expression, "Split FT position", e => made = ExpressionGraph.Split2D(e, tree, path, row, inputX, inputY));
            return made;
        }

        internal string InsertBoxAbove(string tree, string path, string inputX, string inputY)
        {
            if (doc == null || ExpressionGraph.Find(ExpressionJson, tree, path) == null) return null;
            string made = null;
            Edit(TableDocument.Expression, "Insert FT box", e => made = ExpressionGraph.InsertAbove(e, tree, path, inputX, inputY));
            return made;
        }

        internal string NewTree(string name, string inputX, string inputY, bool sided = false)
        {
            if (doc == null || !ExpressionGraph.CanCreateTree(ExpressionJson, name, sided)) return null;
            string made = null;
            Edit(TableDocument.Expression, "New FT expression", e => made = ExpressionGraph.NewTree(e, name, inputX, inputY, sided));
            return made;
        }

        internal bool RemoveTree(string name)
        {
            if (doc == null || !(ExpressionJson?["trees"]?[name] is JObject)) return false;
            var done = false;
            Edit(TableDocument.Expression, "Remove FT expression", e => done = ExpressionGraph.RemoveTree(e, name));
            return done;
        }

        // One unsaved edit of the expression table that returns a value (for the graph and the switch panel).
        internal T EditExpression<T>(string undoName, Func<JObject, T> edit)
        {
            T result = default;
            if (doc == null) return result;
            Edit(TableDocument.Expression, undoName, e => result = edit(e));
            return result;
        }

        // Replaces a box with the content of one of its rows (ExpressionGraph.Remove) as an unsaved edit.
        internal string RemoveBox(string tree, string path, int keepRow)
        {
            if (doc == null || !ExpressionGraph.CanRemove(ExpressionJson, tree, path)) return null;
            string kept = null;
            Edit(TableDocument.Expression, "Remove FT box", e => kept = ExpressionGraph.Remove(e, tree, path, keepRow));
            return kept;
        }

        // Splits a position of the tree with 1D (ExpressionGraph.Split) as an unsaved edit. Returns the new box's path.
        internal string SplitPosition(string tree, string path, int row, string input)
        {
            if (doc == null || !ExpressionGraph.CanSplit(ExpressionJson, tree, path, row)) return null;
            string made = null;
            Edit(TableDocument.Expression, "Split FT position", e => made = ExpressionGraph.Split(e, tree, path, row, input));
            return made;
        }

        // Called right after a fold's header: scrolls to the header when the graph asked for it. Adds nothing to the layout.
        void ScrollMark(string key)
        {
            if (tuneScrollTo != key || Event.current.type != EventType.Repaint) return;
            var r = GUILayoutUtility.GetLastRect();
            tuneScrollTo = null;
            scroll.y = Math.Max(0, r.y - 4);
            Repaint();
        }

        // For TableEditorChecks.Capture: the tab and the open folds.
        internal string TuningState() => $"tab={Tabs[tab]} open={string.Join(",", tuneOpen.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal))}";

        // ---- Face View

        static readonly Dictionary<string, float> NoWeights = new Dictionary<string, float>();

        void ShowSelected()
        {
            if (model == null) { FaceView.Clear(); return; }
            var avatar = doc.Json(TableDocument.Avatar);
            var shape = selectedPart != null ? TableEdits.PartShape(avatar, selectedPart) : null;
            if (shape == null || TableEdits.IsMissing(avatar, selectedPart)) { FaceView.Show(model, Prefab(), NoWeights, "Neutral"); return; }
            var mesh = TableEdits.PartMesh(avatar, selectedPart) ?? model.DefaultMesh;
            FaceView.Show(model, Prefab(), new Dictionary<string, float> { [mesh + "/" + shape] = 100 }, $"{shape} = 100 ({selectedPart})");
        }

        void ShowCandidate(string part, string mesh, string shape)
        {
            if (model == null) return;
            if (shape == null) FaceView.Show(model, Prefab(), NoWeights, $"(None) (choosing for {part})");
            else FaceView.Show(model, Prefab(), new Dictionary<string, float> { [mesh + "/" + shape] = 100 }, $"{shape} = 100 (choosing for {part})");
        }

        // Records the tables for Undo, applies the edit, and rebuilds the model.
        void Edit(string undoName, Action<JObject> edit) => Edit(TableDocument.Avatar, undoName, edit);

        // The same for the input or expression table (kind is a TableDocument kind).
        void Edit(string kind, string undoName, Action<JObject> edit)
        {
            Undo.RecordObject(state, undoName);
            edit(doc.Json(kind));
            state.snapshot = doc.Snapshot();
            EditorUtility.SetDirty(state);
            Rebuild();
            UpdateDirty();
        }

        void OnUndoRedo()
        {
            if (doc == null || state == null) return;
            doc.Restore(state.snapshot);
            Rebuild();
            UpdateDirty();
            Repaint();
        }

        void UpdateDirty()
        {
            hasUnsavedChanges = doc != null && doc.IsDirty();
            saveChangesMessage = EditorText.Get("dialog.save");
        }

        public override void SaveChanges()
        {
            if (!Save()) return;  // still unsaved: the window stays open
            base.SaveChanges();
        }

        bool Save()
        {
            if (doc == null) return true;
            try { foreach (var p in doc.Save()) AssetDatabase.ImportAsset(p); }
            catch (TableLoader.TableException e)
            {
                EditorUtility.DisplayDialog("FT Settings Editor", e.Message, "OK");
                return false;
            }
            UpdateDirty();
            return true;
        }

        bool ConfirmDiscard() => doc == null || !doc.IsDirty() || EditorUtility.DisplayDialog("FT Settings Editor", EditorText.Get("dialog.discard"), "Discard", "Cancel");

        // ---- drawing

        void OnGUI()
        {
            if (BuildGuard.Building) { BuildGuard.DrawPaused(); return; }
            DrawTop();
            if (doc == null)
            {
                if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
                else if (table == null) EditorGUILayout.LabelField(EditorText.Get("msg.noTable"));
                return;
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawAvatarTab();
            else if (tab == 1) DrawPreviewTab();
            else if (tab == 2) DrawTuningTab();
            else DrawInputsTab();
            EditorGUILayout.EndScrollView();
        }

        void DrawTop()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (DefaultAsset)EditorGUILayout.ObjectField(EditorText.Label("Settings file", "tip.table"), table, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && picked != table && ConfirmDiscard())
            {
                table = picked;
                EditorPrefs.SetString(TablePrefKey, TablePath ?? "");
                Load();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(table == null))
                    if (GUILayout.Button("Reload", GUILayout.Width(70)) && ConfirmDiscard()) Load();
                using (new EditorGUI.DisabledScope(doc == null || !doc.IsDirty() || saveBlocked != null))
                    if (GUILayout.Button(new GUIContent("Save", saveBlocked), GUILayout.Width(70))) Save();
                if (GUILayout.Button("Face view", GUILayout.Width(80)))
                {
                    FaceViewWindow.Open();
                    if (preview.Running) ApplyPreview(); else ShowSelected();
                }
                if (doc != null && doc.IsDirty())
                {
                    var old = GUI.contentColor;
                    GUI.contentColor = new Color(1f, 0.76f, 0.03f);
                    GUILayout.Label("Unsaved changes");
                    GUI.contentColor = old;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("Help", GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                var lang = EditorGUILayout.Popup((int)EditorText.Current, EditorText.Choices, GUILayout.Width(80));
                if (EditorGUI.EndChangeCheck()) { EditorText.Current = (EditorText.Language)lang; UpdateDirty(); }
            }
            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space(4);
        }

        static void Heading(string text, string right = null)
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
                if (right != null) GUILayout.Label(right, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            }
        }

        static GUIStyle helpStyle;
        static void Help(string key)
        {
            var text = EditorText.Help(key);
            if (text == null) return;
            if (helpStyle == null)
            {
                helpStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                helpStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
            }
            EditorGUILayout.LabelField(text, helpStyle);
        }

        static GUIStyle partsStyle;
        static GUIStyle PartsStyle
        {
            get
            {
                if (partsStyle == null)
                {
                    partsStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                    partsStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
                }
                return partsStyle;
            }
        }

        static void PartsLine(string text) => EditorGUILayout.LabelField(text, PartsStyle);

        // Gray text beside a field in a horizontal row: a fixed width, or the rest of the row with width 0.
        static void SideNote(string text, float width)
        {
            if (width > 0) GUILayout.Label(text, PartsStyle, GUILayout.Width(width));
            else GUILayout.Label(text, PartsStyle, GUILayout.ExpandWidth(true));
        }

        static void RowBackground(int i)
        {
            var r = GUILayoutUtility.GetLastRect();
            if (i % 2 == 0 && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0, r.y, r.width + r.x * 2, r.height), new Color(1, 1, 1, EditorGUIUtility.isProSkin ? 0.03f : 0.06f));
        }

        // ---- Avatar tab

        GameObject Prefab()
        {
            var p = model?.Prefab;
            if (p == null) return null;
            var path = !string.IsNullOrEmpty(p.Guid) ? AssetDatabase.GUIDToAssetPath(p.Guid) : null;
            if (string.IsNullOrEmpty(path)) path = p.Path;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static AssetRefLike RefOf(JToken t) => t is JObject o ? new AssetRefLike { Guid = (string)o["guid"], Path = (string)o["path"] } : null;
        sealed class AssetRefLike { public string Guid, Path; }

        static T LoadRef<T>(JToken t) where T : UnityEngine.Object
        {
            var r = RefOf(t);
            if (r == null) return null;
            var path = !string.IsNullOrEmpty(r.Guid) ? AssetDatabase.GUIDToAssetPath(r.Guid) : null;
            if (string.IsNullOrEmpty(path)) path = r.Path;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        void DrawAvatarTab()
        {
            var avatar = doc.Json(TableDocument.Avatar);
            var prefab = LoadRef<GameObject>(avatar["prefab"]);

            Heading("Avatar");
            Help("help.avatar");
            EditorGUI.BeginChangeCheck();
            var newPrefab = (GameObject)EditorGUILayout.ObjectField(EditorText.Label("Prefab", "tip.prefab"), prefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                var path = newPrefab != null ? AssetDatabase.GetAssetPath(newPrefab) : null;
                Edit("Set FT prefab", a => TableEdits.SetAssetRef(a, "prefab", path != null ? AssetDatabase.AssetPathToGUID(path) : null, path));
                prefab = newPrefab;
            }
            EditorGUI.BeginChangeCheck();
            var menu = (VRCExpressionsMenu)EditorGUILayout.ObjectField(EditorText.Label("Menu", "tip.menu"), LoadRef<VRCExpressionsMenu>(avatar["menu"]), typeof(VRCExpressionsMenu), false);
            if (EditorGUI.EndChangeCheck())
            {
                var path = menu != null ? AssetDatabase.GetAssetPath(menu) : null;
                Edit("Set FT menu", a => TableEdits.SetAssetRef(a, "menu", path != null ? AssetDatabase.AssetPathToGUID(path) : null, path));
            }
            var meshes = prefab != null
                ? prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                    .Select(r => AnimationUtility.CalculateTransformPath(r.transform, prefab.transform)).ToList()
                : new List<string>();
            var mesh = avatar["mesh"]?.Type == JTokenType.String ? (string)avatar["mesh"] : "";
            if (mesh != "" && !meshes.Contains(mesh)) meshes.Insert(0, mesh);
            using (new EditorGUI.DisabledScope(prefab == null))
            {
                EditorGUI.BeginChangeCheck();
                var mi = EditorGUILayout.Popup(EditorText.Label("Face mesh", "tip.mesh"), meshes.IndexOf(mesh), meshes.Select(m => new GUIContent(m)).ToArray());
                if (EditorGUI.EndChangeCheck() && mi >= 0) Edit("Set FT face mesh", a => TableEdits.SetString(a, "mesh", meshes[mi]));
            }
            if (prefab == null) EditorGUILayout.HelpBox(EditorText.Get("msg.noPrefab"), MessageType.Warning);

            DrawParts(avatar, prefab, mesh);
            DrawLayers(avatar, prefab);

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            var stop = EditorGUILayout.ToggleLeft(EditorText.Label("Stop VRChat auto blink and eye look while FT is on", "tip.stopAutoEyes"), avatar["stopAutoEyes"]?.Type == JTokenType.Boolean && (bool)avatar["stopAutoEyes"]);
            if (EditorGUI.EndChangeCheck()) Edit("Set FT stopAutoEyes", a => TableEdits.SetBool(a, "stopAutoEyes", stop));
        }

        // Parts used by the expression layer (also those the avatar layer lacks) and parts in the avatar layer.
        List<(string part, bool used)> PartRows(JObject avatar)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (model != null)
            {
                foreach (var t in model.Trees) CollectParts(t.Root, used);
                foreach (var m in model.Dropped.Concat(model.Warnings))
                {
                    var mm = NotInAvatar.Match(m);
                    if (mm.Success) used.Add(mm.Groups[1].Value);
                }
            }
            var all = new SortedSet<string>(used, StringComparer.Ordinal);
            all.UnionWith(pendingParts);
            if (avatar["parts"] is JObject parts) foreach (var p in parts.Properties()) all.Add(p.Name);
            if (avatar["missingParts"] is JArray mp) foreach (var x in mp.Where(x => x.Type == JTokenType.String)) all.Add((string)x);
            return all.Select(p => (p, used.Contains(p))).ToList();
        }

        internal static void CollectParts(Node n, HashSet<string> into)
        {
            switch (n)
            {
                case PoseNode p: foreach (var k in p.Parts.Keys) into.Add(k); break;
                case Blend1DNode b: foreach (var c in b.Children) CollectParts(c.node, into); break;
                case Blend2DNode b: foreach (var c in b.Children) CollectParts(c.node, into); break;
                case SwitchNode s: foreach (var c in s.Cases) CollectParts(c.node, into); break;
            }
        }

        static List<string> ShapesOf(GameObject prefab, string mesh)
        {
            var t = prefab != null && !string.IsNullOrEmpty(mesh) ? prefab.transform.Find(mesh) : null;
            var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            var m = smr != null ? smr.sharedMesh : null;
            return m == null ? null : Enumerable.Range(0, m.blendShapeCount).Select(m.GetBlendShapeName).ToList();
        }

        // Set by TableEditorChecks.Capture to open a picker on the next repaint.
        internal string openPickerFor, openPickerSearch;
        // Face movements added in the Avatar tab whose blendshape is not chosen yet: the avatar table has no place for a
        // movement without a shape, so they live here until a shape is chosen (and go on Reload or Save).
        readonly HashSet<string> pendingParts = new HashSet<string>(StringComparer.Ordinal);
        string partScrollTo;

        void DrawParts(JObject avatar, GameObject prefab, string defaultMesh)
        {
            var rows = PartRows(avatar);
            int mapped = 0, missing = 0, notSet = 0;
            foreach (var (part, _) in rows)
            {
                if (TableEdits.IsMissing(avatar, part)) missing++;
                else if (TableEdits.PartShape(avatar, part) != null) mapped++;
                else notSet++;
            }
            Heading("Face movements", $"{mapped} set · {missing} missing · {notSet} not set");
            Help("help.parts");
            partFilter = EditorGUILayout.TextField(partFilter, EditorStyles.toolbarSearchField);

            const float partW = 150, missW = 60;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Movement", EditorStyles.miniBoldLabel, GUILayout.Width(partW));
                GUILayout.Label("Blendshape", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Missing", EditorStyles.miniBoldLabel, GUILayout.Width(missW));
                GUILayout.Space(24);
            }
            var shapeCache = new Dictionary<string, List<string>>();
            var partUsage = model != null ? PartUsage.Of(model) : null;
            var i = 0;
            foreach (var (part, used) in rows)
            {
                if (!string.IsNullOrEmpty(partFilter) && part.IndexOf(partFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var isMissing = TableEdits.IsMissing(avatar, part);
                var shape = TableEdits.PartShape(avatar, part);
                var mesh = TableEdits.PartMesh(avatar, part) ?? defaultMesh;
                if (!shapeCache.TryGetValue(mesh ?? "", out var shapes)) shapeCache[mesh ?? ""] = shapes = ShapesOf(prefab, mesh);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (used) GUILayout.Label(part, GUILayout.Width(partW));
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Width(partW)))
                        {
                            GUILayout.Label(part, GUILayout.ExpandWidth(false));
                            GUILayout.Label("unused", EditorStyles.miniLabel);
                        }
                    }

                    GUIContent content;
                    var color = GUI.contentColor;
                    if (isMissing) content = new GUIContent("—");
                    else if (shape == null) { content = new GUIContent("Not set", EditorGUIUtility.IconContent("console.warnicon.sml").image); GUI.contentColor = new Color(1f, 0.76f, 0.03f); }
                    else if (shapes != null && !shapes.Contains(shape)) { content = new GUIContent(shape + " (not in mesh)", EditorGUIUtility.IconContent("console.erroricon.sml").image); GUI.contentColor = new Color(1f, 0.42f, 0.42f); }
                    else content = new GUIContent(shape);
                    using (new EditorGUI.DisabledScope(isMissing || shapes == null))
                    {
                        var r = GUILayoutUtility.GetRect(content, EditorStyles.popup, GUILayout.ExpandWidth(true), GUILayout.MinWidth(80));
                        var open = EditorGUI.DropdownButton(r, content, FocusType.Keyboard, EditorStyles.popup)
                            || (openPickerFor == part && Event.current.type == EventType.Repaint);
                        if (open && shapes != null)
                        {
                            var p = part;
                            var search = openPickerFor == part ? openPickerSearch : "";
                            openPickerFor = null;
                            var meshForPart = mesh;
                            selectedPart = p;
                            var undo = pendingParts.Contains(p) ? "Add FT face movement" : "Set FT part";
                            PopupWindow.Show(r, new BlendshapePicker(shapes, shape, picked => { Edit(undo, a => TableEdits.SetPart(a, p, picked)); if (picked != null) pendingParts.Remove(p); Repaint(); }, search,
                                hover: name => ShowCandidate(p, meshForPart, name),
                                closed: () => { if (!preview.Running) ShowSelected(); Repaint(); }));
                        }
                    }
                    GUI.contentColor = color;

                    EditorGUI.BeginChangeCheck();
                    var m = GUILayout.Toggle(isMissing, new GUIContent("", EditorText.Get("tip.missing")), GUILayout.Width(missW));
                    if (EditorGUI.EndChangeCheck()) { var p = part; Edit("Set FT missing part", a => TableEdits.SetMissing(a, p, m, removedParts)); }
                    if (TrashButton(used, EditorText.Get("part.cannotRemove")))
                        {
                            var p = part;
                            if (pendingParts.Remove(p) && TableEdits.PartShape(avatar, p) == null && !TableEdits.IsMissing(avatar, p)) Repaint();
                            else Edit("Remove FT face movement", a => TableEdits.RemovePart(a, p));
                            GUIUtility.ExitGUI();
                        }
                }
                var rowRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.ContextClick && rowRect.Contains(Event.current.mousePosition))
                {
                    var p = part;
                    var at = rowRect;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Rename…"), false, () => ShowRenamePart(p, at));
                    menu.ShowAsContext();
                    Event.current.Use();
                }
                if (renameAt == part && Event.current.type == EventType.Repaint) renameRect = rowRect;
                if (pendingParts.Contains(part) && part != selectedPart && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(0, rowRect.y, rowRect.width + rowRect.x * 2, rowRect.height), new Color(0.17f, 0.36f, 0.53f, 0.35f));
                if (partScrollTo == part && Event.current.type == EventType.Repaint) { scroll.y = Math.Max(0, rowRect.y - 60); partScrollTo = null; Repaint(); }
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    selectedPart = part;
                    if (!preview.Running) ShowSelected();
                    Repaint();
                }
                if (part == selectedPart && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(0, rowRect.y, rowRect.width + rowRect.x * 2, rowRect.height), new Color(0.17f, 0.36f, 0.53f, 0.35f));
                RowBackground(i);
                // What moves it, on the full width under the row.
                var how = partUsage != null && partUsage.TryGetValue(part, out var pu) ? PartUsage.Text(pu) : null;
                if (how == null && pendingParts.Contains(part)) how = EditorText.Get("part.new");
                if (how != null)
                {
                    PartsLine("    " + how);
                    RowBackground(i);
                }
                i++;
            }

            // + Add Face Movement: a name (and left/right) as in Tuning, then the row opens its blendshape list.
            var addRect = GUILayoutUtility.GetRect(new GUIContent("+ Add Face Movement"), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            if (GUI.Button(addRect, "+ Add Face Movement", EditorStyles.miniButton))
            {
                var names = Tuning.AllPartNames(avatar, doc.Json(TableDocument.Expression)).Concat(pendingParts).ToList();
                PopupWindow.Show(addRect, new NewPartPopup(names, "s", AddPendingPart));
            }
        }

        string renameAt, renameCapture;  // for TableEditorChecks.Capture: open the rename panel at this row
        Rect renameRect;

        void ShowRenamePart(string part, Rect at)
        {
            var avatar = doc.Json(TableDocument.Avatar);
            var expression = doc.Json(TableDocument.Expression);
            var all = Tuning.AllPartNames(avatar, expression).Concat(pendingParts).ToList();
            var popup = new RenamePartPopup(part, PartRename.Partner(all, part),
                (to, together) => PartRename.Check(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression), part, to, together, pendingParts),
                (to, together) => RenamePart(part, to, together));
            if (renameCapture != null) popup.SetNameForCapture(renameCapture);
            PopupWindow.Show(at, popup);
        }

        // Renames in both tables (PartRename); rows still waiting for a blendshape are renamed here only.
        internal void RenamePart(string from, string to, bool together)
        {
            var all = Tuning.AllPartNames(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression)).Concat(pendingParts).ToList();
            var map = PartRename.Map(all, from, to, together);
            foreach (var kv in map.Where(kv => pendingParts.Contains(kv.Key)).ToList()) { pendingParts.Remove(kv.Key); pendingParts.Add(kv.Value); }
            var avatar = doc.Json(TableDocument.Avatar);
            var expression = doc.Json(TableDocument.Expression);
            Undo.RecordObject(state, "Rename FT face movement");
            PartRename.Apply(avatar, expression, from, to, together, pendingParts);
            state.snapshot = doc.Snapshot();
            EditorUtility.SetDirty(state);
            Rebuild();
            UpdateDirty();
            if (selectedPart != null && map.TryGetValue(selectedPart, out var sel)) selectedPart = sel;
            Repaint();
        }

        // Adds "name" (or "name_L" and "name_R" for "name_{s}") as rows waiting for a blendshape, and opens the first.
        internal void AddPendingPart(string written)
        {
            var parts = written.EndsWith("_{s}") ? new[] { written.Replace("_{s}", "_L"), written.Replace("_{s}", "_R") } : new[] { written };
            foreach (var p in parts) pendingParts.Add(p);
            partFilter = "";
            openPickerFor = parts[0];
            openPickerSearch = "";
            partScrollTo = parts[0];
            selectedPart = parts[0];
            Repaint();
        }

        void DrawLayers(JObject avatar, GameObject prefab)
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel, GUILayout.Width(60));
                controller = EditorGUILayout.Popup(controller, ControllerLabels, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
            }
            Help("help.layers");
            var key = TableEdits.Controllers[controller];
            var lists = controller == 0 ? TableEdits.LayerLists : TableEdits.LayerLists.Take(2).ToArray();
            var names = ControllerLayers(prefab, ControllerTypes[controller]);
            if (names == null)
            {
                EditorGUILayout.LabelField(EditorText.Get("msg.noController"), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            // Names in the table that the controller does not have are kept and shown after the others.
            foreach (var list in lists)
                if (avatar["layers"]?[key]?[list] is JArray a)
                    foreach (var x in a.Where(x => x.Type == JTokenType.String && !names.Contains((string)x))) names.Add((string)x);

            const float colW = 90;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Layer", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Remove", EditorStyles.miniBoldLabel, GUILayout.Width(colW));
                GUILayout.Label("Stop while FT", EditorStyles.miniBoldLabel, GUILayout.Width(colW));
                if (controller == 0) GUILayout.Label("Mute while FT", EditorStyles.miniBoldLabel, GUILayout.Width(colW));
            }
            var i = 0;
            foreach (var name in names)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(name, GUILayout.ExpandWidth(true));
                    foreach (var list in lists)
                    {
                        var on = TableEdits.HasLayer(avatar, key, list, name);
                        EditorGUI.BeginChangeCheck();
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Width(colW)))
                        {
                            GUILayout.Space(colW / 2 - 8);
                            on = GUILayout.Toggle(on, GUIContent.none);
                        }
                        if (EditorGUI.EndChangeCheck()) { var l = list; var n = name; var v = on; Edit("Set FT layer", a => TableEdits.SetLayer(a, key, l, n, v)); }
                    }
                }
                RowBackground(i++);
            }
        }

        static List<string> ControllerLayers(GameObject prefab, VRCAvatarDescriptor.AnimLayerType type)
        {
            var desc = prefab != null ? prefab.GetComponent<VRCAvatarDescriptor>() : null;
            if (desc == null) return null;
            var layer = desc.baseAnimationLayers.FirstOrDefault(l => l.type == type);
            var ac = layer.animatorController as AnimatorController;
            return ac == null ? null : ac.layers.Select(l => l.name).ToList();
        }

        // ---- Preview tab

        void DrawPreviewTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var a = (GameObject)EditorGUILayout.ObjectField("Avatar in scene", sceneAvatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) { preview.Stop(); sceneAvatar = a; }
                var valid = sceneAvatar != null && sceneAvatar.scene.IsValid() && sceneAvatar.GetComponent<VRCAvatarDescriptor>() != null;
                var blocked = !valid || model == null || EditorApplication.isPlayingOrWillChangePlaymode || (!preview.Running && FacePreview.Busy);
                using (new EditorGUI.DisabledScope(blocked && !preview.Running))
                {
                    var running = preview.Running;
                    var press = GUILayout.Toggle(running, running ? "Stop preview" : "Start preview", "Button", GUILayout.Width(110));
                    if (press != running)
                    {
                        if (press) { if (preview.Start(sceneAvatar)) ApplyPreview(); }
                        else { preview.Stop(); ShowSelected(); }
                    }
                }
            }
            var valid2 = sceneAvatar != null && sceneAvatar.scene.IsValid() && sceneAvatar.GetComponent<VRCAvatarDescriptor>() != null;
            if (preview.Running) EditorGUILayout.LabelField(EditorText.Get("msg.previewing"), EditorStyles.wordWrappedMiniLabel);
            else if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.LabelField(EditorText.Get("msg.playMode"), EditorStyles.wordWrappedMiniLabel);
            else if (!valid2) EditorGUILayout.LabelField(EditorText.Get("msg.chooseAvatar"), EditorStyles.wordWrappedMiniLabel);
            else if (FacePreview.Busy) EditorGUILayout.LabelField(EditorText.Get("msg.animBusy"), EditorStyles.wordWrappedMiniLabel);
            if (model == null) return;

            var changed = false;
            var usage = PartUsage.Of(model);

            // Try expressions
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Try expressions", EditorStyles.boldLabel);
            Help("help.presets");
            var presets = Presets.For(model);
            var width = position.width - 30;
            var rowWidth = 0f;
            EditorGUILayout.BeginHorizontal();
            foreach (var (preset, values) in presets)
            {
                var content = new GUIContent(preset.Label);
                var w = EditorStyles.miniButton.CalcSize(content).x + 8;
                if (rowWidth + w > width && rowWidth > 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); rowWidth = 0; }
                rowWidth += w + 4;
                if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(w)))
                {
                    inputs.Clear();
                    foreach (var kv in values) inputs[kv.Key] = kv.Value;
                    switches.Reset();
                    changed = true;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            var states = switches.Update(model, inputs);

            if (preview.Running && nothingApplied) EditorGUILayout.HelpBox(EditorText.Get("msg.noApplied"), MessageType.Warning);

            // Inputs, by device
            EditorGUILayout.Space(6);
            var devices = Devices.Load();
            var deviceNames = new List<string> { "Settings" };
            deviceNames.AddRange(devices.Select(d => d.Name));
            var deviceIndex = Math.Max(0, devices.FindIndex(d => d.Id == (captureDevice ?? EditorPrefs.GetString(DevicePrefKey, ""))) + 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Device", GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                deviceIndex = EditorGUILayout.Popup(deviceIndex, deviceNames.ToArray(), GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck()) { captureDevice = null; EditorPrefs.SetString(DevicePrefKey, deviceIndex == 0 ? "" : devices[deviceIndex - 1].Id); }
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    inputs.Clear();
                    switches.Reset();
                    changed = true;
                }
            }
            Help("help.inputs");
            var device = deviceIndex == 0 ? null : devices[deviceIndex - 1];
            var partsOf = InputParts.Of(model);
            foreach (var input in model.Inputs.Where(x => !x.IsDerived))
            {
                var (lo, hi) = SliderRange(input.Name);
                var v = inputs.TryGetValue(input.Name, out var x) ? x : 0f;
                var sent = device == null || device.Has(input.Key);
                using (new EditorGUI.DisabledScope(!sent))
                {
                    EditorGUI.BeginChangeCheck();
                    v = EditorGUILayout.Slider(new GUIContent(sent ? input.Name : input.Name + EditorText.Get("device.notSent"), ParameterHelp.Tooltip(model, input)), v, lo, hi);
                    if (EditorGUI.EndChangeCheck()) { inputs[input.Name] = (float)Math.Round(v, 2); changed = true; }
                }
                if (partsOf.TryGetValue(input.Name, out var line)) PartsLine(line);
            }
            foreach (var input in model.Inputs.Where(x => x.IsDerived))
            {
                var v = input.Average.Average(s => inputs.TryGetValue(s, out var x) ? x : 0f);
                var (lo, hi) = SliderRange(input.Name);
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.Slider(new GUIContent(input.Name + " (average)", ParameterHelp.Tooltip(model, input)), v, lo, hi);
            }
            if (device != null)
            {
                var unused = device.Keys.Where(k => model.Inputs.All(i => i.Key != k.key)).ToList();
                showUnusedInputs = EditorGUILayout.Foldout(showUnusedInputs, string.Format(EditorText.Get("device.unused"), unused.Count), true);
                if (showUnusedInputs)
                    foreach (var k in unused)
                    {
                        var name = Devices.NameOf(k.key);
                        var v = unusedInputs.TryGetValue(name, out var x) ? x : 0f;
                        unusedInputs[name] = (float)Math.Round(EditorGUILayout.Slider(new GUIContent(name, ParameterHelp.Tooltip(new[] { k.key })), v, k.min, k.max), 2);
                    }
            }

            if (changed) states = switches.Update(model, inputs);
            if (changed && preview.Running) ApplyPreview(states);

            // Overlaps under the sliders, so that a warning that comes and goes does not move the slider in use.
            foreach (var o in Overlaps.Find(CurveEval.EvaluateParts(model, inputs, states)))
            {
                EditorGUILayout.HelpBox(Overlaps.Message(o, usage), MessageType.Warning);
                foreach (var (part, _) in o.Parts)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(20);
                        if (GUILayout.Button(string.Format(EditorText.Get("overlap.again"), part), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            tab = 0;
                            selectedPart = part;
                            openPickerFor = part;
                            openPickerSearch = "";
                            ShowSelected();
                        }
                        if (GUILayout.Button(string.Format(EditorText.Get("overlap.notUse"), part), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            var p = part;
                            Edit("Do not use FT movement", a => TableEdits.SetMissing(a, p, true, removedParts));
                            changed = true;
                        }
                    }
            }

            // Weights
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Weights", EditorStyles.boldLabel);
            Help("help.weights");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Blendshape", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Amount", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUILayout.Label("Source", EditorStyles.miniBoldLabel, GUILayout.Width(200));
            }
            var i = 0;
            foreach (var row in PreviewWeights.Rows(model, inputs, states))
            {
                var (mesh, shape) = FacePreview.Split(row.Key);
                var name = mesh == model.DefaultMesh ? shape : row.Key;
                if (notOnAvatar.Contains(row.Key)) name += " " + EditorText.Get("msg.notOnAvatar");
                var sourceInputs = model.Parts.Where(p => p.Value.Mesh + "/" + p.Value.Shape == row.Key)
                    .SelectMany(p => usage.TryGetValue(p.Key, out var u) ? u.Inputs : new List<string>()).Distinct().ToList();
                var source = sourceInputs.Count > 0 ? string.Join(", ", sourceInputs.Select(PartUsage.Source)) : string.Join(", ", row.From);
                var old = GUI.contentColor;
                if (notOnAvatar.Contains(row.Key)) GUI.contentColor = Color.gray;
                else if (row.Amount > 100) GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(name, GUILayout.ExpandWidth(true));
                    GUILayout.Label(Math.Round(row.Amount).ToString(), GUILayout.Width(60));
                    GUILayout.Label(source, GUILayout.Width(200));
                }
                GUI.contentColor = old;
                RowBackground(i++);
            }

            // Switching expressions (folded)
            if (model.Switches.Count > 0)
            {
                EditorGUILayout.Space(6);
                showSwitches = EditorGUILayout.Foldout(showSwitches, EditorText.Heading("switches.title"), true, EditorStyles.foldoutHeader);
                if (showSwitches)
                {
                    Help("help.switches");
                    var switchChanged = false;
                    foreach (var sw in model.Switches)
                    {
                        var options = new List<string> { string.Format(EditorText.Get("switches.auto"), switches.Current(sw.Name) ?? sw.States[0]) };
                        options.AddRange(sw.States);
                        var index = switches.Pinned.TryGetValue(sw.Name, out var pin) ? sw.States.IndexOf(pin) + 1 : 0;
                        EditorGUI.BeginChangeCheck();
                        index = EditorGUILayout.Popup(sw.Name, index, options.ToArray());
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (index == 0) switches.Pinned.Remove(sw.Name); else switches.Pinned[sw.Name] = sw.States[index - 1];
                            switchChanged = true;
                        }
                    }
                    if (switchChanged)
                    {
                        states = switches.Update(model, inputs);
                        if (preview.Running) ApplyPreview(states);
                        Repaint();
                    }
                }
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(EditorText.Get("note.preview"), MessageType.Info);
        }

        // The slider range: thresholds and positions that use the input, also through averages of it.
        (float, float) SliderRange(string input)
        {
            var names = new List<string> { input };
            names.AddRange(model.Inputs.Where(d => d.IsDerived && d.Average.Contains(input)).Select(d => d.Name));
            var d0 = model.Inputs.FirstOrDefault(d => d.Name == input && d.IsDerived);
            if (d0 != null) names.AddRange(d0.Average);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var n in names)
            {
                var (a, b) = CurveEval.Range(model, n);
                lo = Math.Min(lo, a); hi = Math.Max(hi, b);
            }
            return (lo, hi);
        }

        void ApplyPreview(Dictionary<string, string> states = null)
        {
            if (model == null || !preview.Running) return;
            states = states ?? switches.Update(model, inputs);
            var weights = CurveEval.Evaluate(model, inputs, states);
            notOnAvatar = preview.Apply(model, weights);
            nothingApplied = FacePreview.Targets(model, weights).All(notOnAvatar.Contains);
            FaceView.Show(model, Prefab(), weights, "Preview");
        }

        // ---- Tuning tab

        static readonly Color[] CurveColors =
        {
            new Color(0.31f, 0.64f, 1f), new Color(1f, 0.63f, 0.25f), new Color(0.4f, 0.8f, 0.4f), new Color(0.75f, 0.5f, 1f),
            new Color(1f, 0.45f, 0.55f), new Color(0.35f, 0.85f, 0.85f),
        };

        void DrawTuningTab()
        {
            if (model == null) return;
            var expression = doc.Json(TableDocument.Expression);
            var trees = Tuning.Trees(expression);
            var allNames = Tuning.AllPartNames(doc.Json(TableDocument.Avatar), expression);

            var selectedOnly = captureShowSelected ?? EditorPrefs.GetBool(ShowSelectedPrefKey, false);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Show", GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                var pick = GUILayout.Toolbar(selectedOnly ? 1 : 0, new[] { "All", "Selected" }, GUILayout.Width(140));
                if (EditorGUI.EndChangeCheck()) { captureShowSelected = null; EditorPrefs.SetBool(ShowSelectedPrefKey, pick == 1); selectedOnly = pick == 1; }
            }
            EditorGUILayout.Space(4);
            if (selectedOnly) { DrawTuningSelected(expression, trees, allNames); return; }

            EditorGUILayout.LabelField("Expressions", EditorStyles.boldLabel);
            Help("help.thresholds");
            var shown = trees.Where(t => t.Nodes.Count > 0 || t.Has2D).ToList();
            if (shown.Count == 0) EditorGUILayout.LabelField(EditorText.Get("tune.no1d"), EditorStyles.wordWrappedLabel);
            var firstNode = true;
            foreach (var t in shown)
            {
                tuneOpen.TryGetValue(t.Name, out var open);
                if (!tuneOpen.ContainsKey(t.Name)) open = t == shown[0];
                tuneOpen[t.Name] = open = EditorGUILayout.Foldout(open, Tuning.TreeTitle(t), true, EditorStyles.foldoutHeader);
                ScrollMark(t.Name);
                if (!open) { firstNode = false; continue; }
                EditorGUI.indentLevel++;
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    var key = t.Name + "/" + i;
                    if (!tuneOpen.ContainsKey(key)) tuneOpen[key] = firstNode;
                    firstNode = false;
                    tuneOpen[key] = EditorGUILayout.Foldout(tuneOpen[key], new GUIContent(Tuning.NodeTitle(t.Nodes[i]), NodeInputTooltip(t, t.Nodes[i].Input)), true);
                    ScrollMark(key);
                    if (tuneOpen[key]) DrawTuningNode(t, i, key, allNames);
                }
                for (int k = 0; k < t.Nodes2D.Count; k++) DrawTuningNode2D(t, k, allNames);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);
            var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Smoothing", EditorStyles.boldLabel);
            Help("help.smoothing");
            var smoothings = Tuning.Smoothings(doc.Json(TableDocument.Input));
            if (smoothings.Count == 0) EditorGUILayout.LabelField(EditorText.Get("tune.noSmoothing"));
            foreach (var (name, def) in smoothings)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(name, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                    SideNote(Tuning.Uses(model.Inputs.Where(x => x.Smoothing == name).Select(x => x.Name)), 0);
                }
                EditorGUI.indentLevel++;
                foreach (var local in new[] { true, false })
                {
                    var field = local ? "localTau" : "remoteTau";
                    var ms = Tuning.Ms(def[field]?.Value<float>() ?? 0);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var v = EditorGUILayout.DelayedIntField(EditorText.Get(local ? "tune.local" : "tune.remote"), ms, GUILayout.Width(260));
                        GUILayout.Label("ms", GUILayout.ExpandWidth(false));
                        if (EditorGUI.EndChangeCheck() && v != ms)
                        {
                            var n = name; var l = local;
                            Edit(TableDocument.Input, "Set FT smoothing", j => Tuning.SetMs(Tuning.Smoothings(j).First(x => x.name == n).def, l, v));
                        }
                    }
                }
                var syncNote = EditorText.Help("help.syncDelay");
                if (syncNote != null) PartsLine(syncNote);
                EditorGUI.indentLevel--;
            }
            DrawSwitchList(expression);
        }

        bool? captureShowSelected;  // set by TableEditorChecks.Capture instead of the saved choice
        readonly HashSet<string> switchOpen = new HashSet<string>();

        // Show: All, at the bottom: every switch in the table, with its users; open one to edit it; unused ones can go.
        void DrawSwitchList(JObject expression)
        {
            EditorGUILayout.Space(6);
            var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Switches", EditorStyles.boldLabel);
            Help("help.switches");
            var ja = EditorText.Current == EditorText.Language.Japanese;
            foreach (var name in SwitchEdit.Written(expression))
            {
                var users = SwitchEdit.Users(expression, name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var title = ExpressionGraph.InputText(name);
                    var titleWidth = EditorStyles.foldout.CalcSize(new GUIContent(title)).x + 16;
                    var foldRect = GUILayoutUtility.GetRect(titleWidth, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(false));
                    var open = EditorGUI.Foldout(foldRect, switchOpen.Contains(name), title, true);
                    if (open) switchOpen.Add(name); else switchOpen.Remove(name);
                    GUILayout.Label(users.Count > 0 ? string.Format(EditorText.Get("inputs.usedBy"), string.Join(ja ? "・" : ", ", users)) : EditorText.Get("switch.unused"), PartsStyle, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();
                    if (TrashButton(users.Count > 0, EditorText.Get("switch.cannotRemove")))
                        {
                            var n = name;
                            EditExpression("Set FT switch", e => SwitchEdit.Remove(e, n));
                            GUIUtility.ExitGUI();
                        }
                }
                if (switchOpen.Contains(name))
                {
                    EditorGUI.indentLevel++;
                    DrawSwitchPanel(expression, name, null);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(4);
                }
            }
            if (GUILayout.Button("+ New Switch", GUILayout.ExpandWidth(false)))
            {
                var first = ExpressionGraph.SplitInputs(doc.Json(TableDocument.Input), expression, null).FirstOrDefault();
                var made = EditExpression("Set FT switch", e => SwitchEdit.NewSwitch(e, false, first));
                if (made != null) switchOpen.Add(made);
                GUIUtility.ExitGUI();
            }
        }

        // Show: Selected. Only the node of the box selected in the expression graph, under the path to it.
        void DrawTuningSelected(JObject expression, List<Tuning.Tree> trees, ICollection<string> allNames)
        {
            var pathText = graphTree != null ? ExpressionGraph.PathText(expression, graphTree, graphPath) : null;
            if (pathText == null)
            {
                EditorGUILayout.LabelField(EditorText.Get("tune.selectBox"), EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Open Expression Graph", GUILayout.ExpandWidth(false))) ExpressionGraphWindow.Open();
                return;
            }
            EditorGUILayout.LabelField(pathText, EditorStyles.boldLabel);
            var box = ExpressionGraph.Build(expression, graphTree).FirstOrDefault(b => b.Path == graphPath);
            var t = trees.FirstOrDefault(x => x.Name == graphTree);
            if (box != null && box.Kind == "switch")
            {
                DrawSwitchPanel(expression, (string)ExpressionGraph.Find(expression, graphTree, graphPath)?["switch"], graphTree);
                return;
            }
            if (box == null || t == null || box.TuningKey == null) return;
            var key = box.TuningKey;
            var at2d = key.IndexOf("/2d/", StringComparison.Ordinal);
            EditorGUI.indentLevel++;
            if (at2d >= 0 && int.TryParse(key.Substring(at2d + 4), out var k) && k < t.Nodes2D.Count) DrawTuningNode2D(t, k, allNames);
            else if (int.TryParse(key.Substring(key.LastIndexOf('/') + 1), out var i) && i < t.Nodes.Count)
            {
                EditorGUILayout.Foldout(true, new GUIContent(Tuning.NodeTitle(t.Nodes[i]), NodeInputTooltip(t, t.Nodes[i].Input)), true);  // always open here
                tuneOpen[key] = true;
                DrawTuningNode(t, i, key, allNames);
            }
            EditorGUI.indentLevel--;
        }

        // The switch of the selected switch box, or of a row of Switches: its name, states and transitions (SwitchEdit).
        void DrawSwitchPanel(JObject expression, string reference, string tree)
        {
            if (reference == null || SwitchEdit.Defs(expression, reference).Count == 0) return;
            const string undo = "Set FT switch";
            var ja = EditorText.Current == EditorText.Language.Japanese;
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.DelayedTextField("Switch", Tuning.Label(reference));
            if (EditorGUI.EndChangeCheck() && name != Tuning.Label(reference))
            {
                if (SwitchEdit.CanRename(expression, reference, name))
                {
                    var newRef = name.Trim() + (reference.Contains("{side}") ? "{side}" : "");
                    EditExpression(undo, e => SwitchEdit.Rename(e, reference, name));
                    reference = newRef;
                }
                GUIUtility.ExitGUI();
            }
            var users = SwitchEdit.Users(expression, reference);
            var note = EditorText.Help("tune.switchNote");
            if (note != null) PartsLine(string.Format(note, string.Join(ja ? "・" : ", ", users)));

            var states = SwitchEdit.States(expression, reference);
            EditorGUILayout.LabelField("States", EditorStyles.miniBoldLabel);
            for (int i = 0; i < states.Count; i++)
            {
                var st = states[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUILayout.DelayedTextField(st, GUILayout.Width(200));
                    if (EditorGUI.EndChangeCheck() && v != st) { EditExpression(undo, e => SwitchEdit.RenameState(e, reference, st, v)); GUIUtility.ExitGUI(); }
                    if (i == 0) GUILayout.Label("(default)", PartsStyle, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(states.Count <= 2))
                        if (GUILayout.Button(TrashContent(), TrashStyle, GUILayout.Width(20), GUILayout.Height(18)))
                        { EditExpression(undo, e => SwitchEdit.RemoveState(e, reference, st)); GUIUtility.ExitGUI(); }
                }
            }
            if (GUILayout.Button("+ Add State", GUILayout.ExpandWidth(false))) { EditExpression(undo, e => SwitchEdit.AddState(e, reference)); GUIUtility.ExitGUI(); }

            var inputs = ExpressionGraph.SplitInputs(doc.Json(TableDocument.Input), expression, tree, reference.Contains("{side}"));
            var inputTexts = inputs.Select(ExpressionGraph.InputText).ToArray();
            var stateArray = states.ToArray();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Transitions", EditorStyles.miniBoldLabel);
            var view = SwitchEdit.View(expression, reference);
            for (int i = 0; i < view.Count; i++)
            {
                var tr = view[i];
                var ti = i;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var from = EditorGUILayout.Popup(Math.Max(0, states.IndexOf(tr.From)), stateArray, GUILayout.Width(100));
                        GUILayout.Label("\u2192", GUILayout.ExpandWidth(false));
                        var to = EditorGUILayout.Popup(Math.Max(0, states.IndexOf(tr.To)), stateArray, GUILayout.Width(100));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("Duration", GUILayout.ExpandWidth(false));
                        var dur = EditorGUILayout.DelayedFloatField((float)tr.Duration, GUILayout.Width(50));
                        GUILayout.Label("s", GUILayout.ExpandWidth(false));
                        if (EditorGUI.EndChangeCheck())
                            EditExpression(undo, e => { SwitchEdit.SetTransition(e, reference, ti, stateArray[from], stateArray[to], Math.Max(0, Math.Round(dur, 3))); return true; });
                        if (GUILayout.Button(TrashContent(), TrashStyle, GUILayout.Width(20), GUILayout.Height(18)))
                        { EditExpression(undo, e => { SwitchEdit.RemoveTransition(e, reference, ti); return true; }); GUIUtility.ExitGUI(); }
                    }
                    for (int j = 0; j < tr.When.Count; j++)
                    {
                        var (input, op, value) = tr.When[j];
                        var cj = j;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(16);
                            var choices = inputs.Contains(input) ? inputTexts : inputTexts.Concat(new[] { ExpressionGraph.InputText(input) }).ToArray();
                            var names = inputs.Contains(input) ? inputs : inputs.Concat(new[] { input }).ToList();
                            EditorGUI.BeginChangeCheck();
                            var ii = EditorGUILayout.Popup(names.IndexOf(input), choices, GUILayout.Width(170));
                            var oi = EditorGUILayout.Popup(op == "<" ? 1 : 0, new[] { ">", "<" }, GUILayout.Width(40));
                            var vv = EditorGUILayout.DelayedFloatField((float)value, GUILayout.Width(60));
                            if (EditorGUI.EndChangeCheck())
                                EditExpression(undo, e => { SwitchEdit.SetCondition(e, reference, ti, cj, names[ii], oi == 1 ? "<" : ">", Math.Round(vv, 3)); return true; });
                            using (new EditorGUI.DisabledScope(tr.When.Count <= 1))
                                if (GUILayout.Button("\u00D7", EditorStyles.miniButton, GUILayout.Width(20)))
                                { EditExpression(undo, e => SwitchEdit.RemoveCondition(e, reference, ti, cj)); GUIUtility.ExitGUI(); }
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(16);
                        if (GUILayout.Button("+ Add Condition", GUILayout.ExpandWidth(false)) && inputs.Count > 0)
                        { EditExpression(undo, e => { SwitchEdit.AddCondition(e, reference, ti, inputs[0]); return true; }); GUIUtility.ExitGUI(); }
                    }
                }
            }
            if (GUILayout.Button("+ Add Transition", GUILayout.ExpandWidth(false)) && inputs.Count > 0)
            { EditExpression(undo, e => { SwitchEdit.AddTransition(e, reference, inputs[0]); return true; }); GUIUtility.ExitGUI(); }
        }

        // Edits the expression table at a tree found again by name (the trees are made again after each edit).
        void EditTree(string undoName, string tree, Action<Tuning.Tree> edit) =>
            Edit(TableDocument.Expression, undoName, e => edit(Tuning.Trees(e).First(x => x.Name == tree)));

        void DrawTuningNode(Tuning.Tree t, int index, string key, ICollection<string> allNames)
        {
            var n = t.Nodes[index];
            var (lo, hi) = Tuning.InputRange(n);
            var name = t.Name;

            // Graph: amount of each face movement (0-100) along the input, positions as dotted lines, the try value as a line.
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(100, 90, GUILayout.ExpandWidth(true)));
            tuneTry.TryGetValue(key, out var tryValue);
            if (Event.current.type == EventType.Repaint) DrawTuningGraph(rect, n, lo, hi, tryValue);

            for (int i = 0; i < n.Children.Count; i++)
            {
                var (plo, phi) = Tuning.PositionRange(n, i);
                var j = i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUILayout.Slider(string.Format(EditorText.Get("tune.position"), i + 1), n.Position(i), plo, phi);
                    if (EditorGUI.EndChangeCheck())
                    {
                        tuneShown = key;
                        EditTree("Set FT position", name, x => Tuning.SetPosition(x.Nodes[index], j, v));
                    }
                    using (new EditorGUI.DisabledScope(!Tuning.CanRemovePosition(n)))
                        if (GUILayout.Button(TrashContent(), TrashStyle, GUILayout.Width(20), GUILayout.Height(18)))
                        {
                            tuneShown = key;
                            EditTree("Remove FT position", name, x => Tuning.RemovePosition(x.Nodes[index], j));
                            GUIUtility.ExitGUI();
                        }
                }
                DrawPose(t, n.Child(i), key, allNames, (x, edit) => edit(Tuning.PoseObject(x.Nodes[index].Children, j)));
            }

            EditorGUI.BeginChangeCheck();
            var tv = EditorGUILayout.Slider(string.Format(EditorText.Get("tune.try"), Tuning.Label(n.Input)), tryValue, lo, hi);
            if (EditorGUI.EndChangeCheck())
            {
                tuneTry[key] = (float)Math.Round(tv, 2);
                ShowTuning(key);
            }
            var note = EditorText.Help("help.tuneTry");
            if (note != null) { EditorGUI.indentLevel++; PartsLine(note); EditorGUI.indentLevel--; }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15);
                using (new EditorGUI.DisabledScope(!Tuning.CanAddPosition(n, tryValue)))
                    if (GUILayout.Button(string.Format(EditorText.Get("tune.addPosition"), tryValue.ToString("0.##")), GUILayout.ExpandWidth(false)))
                    {
                        var x0 = tryValue;
                        tuneShown = key;
                        EditTree("Add FT position", name, x => Tuning.AddPosition(x.Nodes[index], x0));
                        GUIUtility.ExitGUI();
                    }
            }
            EditorGUILayout.Space(4);
        }

        // A trash button; a locked one is drawn faint, does nothing, and says why in its tooltip.
        static bool TrashButton(bool locked, string why)
        {
            var old = GUI.color;
            if (locked) GUI.color = new Color(old.r, old.g, old.b, 0.35f);
            var clicked = GUILayout.Button(new GUIContent(TrashContent().image, locked ? why : null), TrashStyle, GUILayout.Width(20), GUILayout.Height(18));
            GUI.color = old;
            return clicked && !locked;
        }

        static GUIStyle trashStyle;
        static GUIStyle TrashStyle => trashStyle ?? (trashStyle = new GUIStyle(EditorStyles.miniButton)
            { padding = new RectOffset(1, 1, 1, 1), fixedHeight = 18, imagePosition = ImagePosition.ImageOnly });

        // The button that removes a position: Unity's trash icon, so that it does not look like the × of a face movement.
        static GUIContent TrashContent()
        {
            var icon = EditorGUIUtility.IconContent("TreeEditor.Trash");
            return icon?.image != null ? new GUIContent(icon.image, EditorText.Get("tip.removePosition")) : new GUIContent("Del", EditorText.Get("tip.removePosition"));
        }

        readonly Dictionary<string, Vector2> tuneTry2D = new Dictionary<string, Vector2>();

        void DrawTuningNode2D(Tuning.Tree t, int k, ICollection<string> allNames)
        {
            var n = t.Nodes2D[k];
            var name = t.Name;
            var key = $"{t.Name}/2d/{k}";
            var (rx, ry) = Tuning.InputRange(n);
            if (t.Nodes2D.Count > 1 || t.Nodes.Count > 0) EditorGUILayout.LabelField(Tuning.Label(n.InputX) + " × " + Tuning.Label(n.InputY), EditorStyles.miniBoldLabel);
            ScrollMark(key);
            PartsLine(string.Format(EditorText.Get("tune.2d"), Tuning.Label(n.InputX), Tuning.Label(n.InputY)));
            tuneTry2D.TryGetValue(key, out var tryPoint);

            // Square graph: positions as numbered points, Try as a ring. Click or drag inside to move Try.
            const float size = 220;
            var area = GUILayoutUtility.GetRect(size + 60, size + 46, GUILayout.ExpandWidth(false));
            area.x += EditorGUI.indentLevel * 15;
            var plot = new Rect(area.x + 28, area.y + 26, size, size);
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            float X(float v) => plot.x + (rx.hi > rx.lo ? (v - rx.lo) / (rx.hi - rx.lo) : 0) * plot.width;
            float Y(float v) => plot.yMax - (ry.hi > ry.lo ? (v - ry.lo) / (ry.hi - ry.lo) : 0) * plot.height;
            Vector2 ToValue(Vector2 m) => new Vector2(
                (float)Math.Round(Mathf.Lerp(rx.lo, rx.hi, Mathf.Clamp01((m.x - plot.x) / plot.width)), 2),
                (float)Math.Round(Mathf.Lerp(ry.lo, ry.hi, Mathf.Clamp01((plot.yMax - m.y) / plot.height)), 2));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && plot.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    tuneTry2D[key] = tryPoint = ToValue(e.mousePosition);
                    ShowTuning(key); e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    tuneTry2D[key] = tryPoint = ToValue(e.mousePosition);
                    ShowTuning(key); e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0; e.Use();
                    break;
                case EventType.Repaint:
                    DrawGraph2D(plot, n, rx, ry, tryPoint, X, Y);
                    break;
            }

            // Try
            EditorGUI.BeginChangeCheck();
            var tx = EditorGUILayout.Slider(string.Format(EditorText.Get("tune.try"), Tuning.Label(n.InputX)), tryPoint.x, rx.lo, rx.hi);
            var ty = EditorGUILayout.Slider(string.Format(EditorText.Get("tune.try"), Tuning.Label(n.InputY)), tryPoint.y, ry.lo, ry.hi);
            if (EditorGUI.EndChangeCheck())
            {
                tuneTry2D[key] = tryPoint = new Vector2((float)Math.Round(tx, 2), (float)Math.Round(ty, 2));
                ShowTuning(key);
            }
            var note = EditorText.Help("help.tuneTry");
            if (note != null) { EditorGUI.indentLevel++; PartsLine(note); EditorGUI.indentLevel--; }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15);
                using (new EditorGUI.DisabledScope(!Tuning.CanAddPosition2D(n, tryPoint.x, tryPoint.y)))
                    if (GUILayout.Button(string.Format(EditorText.Get("tune.addPosition"), $"{tryPoint.x:0.##}, {tryPoint.y:0.##}"), GUILayout.ExpandWidth(false)))
                    {
                        var p0 = tryPoint;
                        EditTreeAndShow("Add FT position", name, key, x => Tuning.AddPosition2D(x.Nodes2D[k], p0.x, p0.y));
                        GUIUtility.ExitGUI();
                    }
            }

            // Positions
            for (int i = 0; i < n.Children.Count; i++)
            {
                var (px, py) = n.Position(i);
                var j = i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(string.Format(EditorText.Get("tune.position"), i + 1));
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    var lw = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 14;
                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    var nx = EditorGUILayout.DelayedFloatField("X", px, GUILayout.Width(74));
                    var ny = EditorGUILayout.DelayedFloatField("Y", py, GUILayout.Width(74));
                    EditorGUI.indentLevel = indent;
                    EditorGUIUtility.labelWidth = lw;
                    if (EditorGUI.EndChangeCheck())
                        EditTreeAndShow("Set FT position", name, key + "/" + j, x => Tuning.SetPosition2D(x.Nodes2D[k], j, nx, ny));
                    using (new EditorGUI.DisabledScope(!Tuning.CanRemovePosition2D(n)))
                        if (GUILayout.Button(TrashContent(), TrashStyle, GUILayout.Width(20), GUILayout.Height(18)))
                        {
                            EditTreeAndShow("Remove FT position", name, key, x => Tuning.RemovePosition2D(x.Nodes2D[k], j));
                            GUIUtility.ExitGUI();
                        }
                }
                DrawPose(t, n.Child(i), key + "/" + i, allNames, (tree, edit) => edit(Tuning.PoseObject(tree.Nodes2D[k].Children, j)));
            }
            EditorGUILayout.Space(4);
        }

        void DrawGraph2D(Rect plot, Tuning.Blend2D n, (float lo, float hi) rx, (float lo, float hi) ry, Vector2 tryPoint, Func<float, float> X, Func<float, float> Y)
        {
            var pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(plot, pro ? new Color(0.165f, 0.165f, 0.165f) : new Color(0.85f, 0.85f, 0.85f));
            var label = new GUIStyle(EditorStyles.miniLabel);
            label.normal.textColor = pro ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            if (rx.lo < 0 && rx.hi > 0) Handles.DrawLine(new Vector3(X(0), plot.y), new Vector3(X(0), plot.yMax));
            if (ry.lo < 0 && ry.hi > 0) Handles.DrawLine(new Vector3(plot.x, Y(0)), new Vector3(plot.xMax, Y(0)));
            Handles.DrawLine(new Vector3(plot.x, plot.yMax), new Vector3(plot.xMax, plot.yMax));
            Handles.DrawLine(new Vector3(plot.x, plot.y), new Vector3(plot.x, plot.yMax));
            var center = new GUIStyle(label) { alignment = TextAnchor.UpperCenter };
            foreach (var v in new[] { rx.lo, 0f, rx.hi }.Distinct().Where(v => v >= rx.lo && v <= rx.hi))
                GUI.Label(new Rect(X(v) - 14, plot.yMax + 1, 28, 14), v.ToString("0.##"), center);
            GUI.Label(new Rect(plot.x, plot.yMax + 13, plot.width, 14), Tuning.Label(n.InputX), center);
            var right = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            foreach (var v in new[] { ry.lo, 0f, ry.hi }.Distinct().Where(v => v >= ry.lo && v <= ry.hi))
                GUI.Label(new Rect(plot.x - 28, Y(v) - 7, 25, 14), v.ToString("0.##"), right);
            GUI.Label(new Rect(plot.x - 28, plot.y - 26, 120, 14), Tuning.Label(n.InputY), label);
            for (int i = 0; i < n.Children.Count; i++)
            {
                var (px, py) = n.Position(i);
                var c = new Vector2(X(px), Y(py));
                Handles.color = new Color(0.82f, 0.82f, 0.82f);
                Handles.DrawSolidDisc(c, Vector3.forward, 4);
                GUI.Label(new Rect(c.x + 5, c.y - 15, 20, 14), (i + 1).ToString(), label);
            }
            var t = new Vector2(X(tryPoint.x), Y(tryPoint.y));
            Handles.color = pro ? Color.white : Color.black;
            Handles.DrawWireDisc(t, Vector3.forward, 6);
            Handles.DrawLine(new Vector3(t.x - 4, t.y), new Vector3(t.x + 4, t.y));
            Handles.DrawLine(new Vector3(t.x, t.y - 4), new Vector3(t.x, t.y + 4));
        }

        // The face movement rows of a position, and "+ Add face movement". locate finds the pose again in a tree made
        // after the edit and passes it to an edit.
        void DrawPose(Tuning.Tree t, JToken child, string showKey, ICollection<string> allNames, Action<Tuning.Tree, Action<JObject>> locate)
        {
            var name = t.Name;
            EditorGUI.indentLevel++;
            var pose = Tuning.Pose(child);
            if (pose == null) { PartsLine(Tuning.ChildText(child)); EditorGUI.indentLevel--; return; }
            if (pose.Count == 0) PartsLine(Tuning.ChildText(child));
            foreach (var kv in pose)
            {
                var part = kv.Key;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUILayout.IntSlider(Tuning.Label(part), (int)Math.Round(kv.Value), 0, 100);
                    if (EditorGUI.EndChangeCheck())
                        EditTreeAndShow("Set FT amount", name, showKey, tree => locate(tree, p => Tuning.SetAmount(p, part, v)));
                    if (GUILayout.Button(new GUIContent("×", EditorText.Get("tip.removePart")), EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        EditTreeAndShow("Remove FT face movement", name, showKey, tree => locate(tree, p => Tuning.RemovePart(p, part)));
                        GUIUtility.ExitGUI();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15);
                var content = new GUIContent("+ Add face movement");
                if (EditorGUILayout.DropdownButton(content, FocusType.Passive, GUILayout.ExpandWidth(false)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    var menu = new GenericMenu();
                    foreach (var (label, write) in Tuning.Choices(t, allNames))
                    {
                        var dot = label.IndexOf('.');
                        var path = (dot > 0 ? label.Substring(0, dot) + "/" : "") + label;
                        var w = write;
                        if (pose.ContainsKey(write)) menu.AddDisabledItem(new GUIContent(path));
                        else menu.AddItem(new GUIContent(path), false, () => EditTreeAndShow("Add FT face movement", name, showKey, tree => locate(tree, p => Tuning.AddPart(p, w))));
                    }
                    if (menu.GetItemCount() > 0) menu.AddSeparator("");
                    var pairVar = Tuning.PairVariable(t);
                    menu.AddItem(new GUIContent("New face movement…"), false, () =>
                        PopupWindow.Show(r, new NewPartPopup(allNames, pairVar, part => EditTreeAndShow("Add FT face movement", name, showKey, tree => locate(tree, p => Tuning.AddPart(p, part))))));
                    menu.DropDown(r);
                }
            }
            EditorGUI.indentLevel--;
        }

        void EditTreeAndShow(string undoName, string tree, string showKey, Action<Tuning.Tree> edit)
        {
            tuneShown = showKey;
            EditTree(undoName, tree, edit);
            Repaint();
        }

        void DrawTuningGraph(Rect r, Tuning.Blend1D n, float lo, float hi, float tryValue)
        {
            var pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(r, pro ? new Color(0.165f, 0.165f, 0.165f) : new Color(0.85f, 0.85f, 0.85f));
            var plot = new Rect(r.x + 28, r.y + 8, r.width - 40, r.height - 24);
            float X(float v) => plot.x + (hi > lo ? (v - lo) / (hi - lo) : 0) * plot.width;
            float Y(float amount) => plot.yMax - Mathf.Clamp(amount, 0, 100) / 100f * plot.height;
            var label = new GUIStyle(EditorStyles.miniLabel);
            label.normal.textColor = pro ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);

            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Handles.DrawLine(new Vector3(plot.x, plot.yMax), new Vector3(plot.xMax, plot.yMax));
            for (int i = 0; i < n.Children.Count; i++)
            {
                var x = X(n.Position(i));
                Handles.DrawDottedLine(new Vector3(x, plot.y), new Vector3(x, plot.yMax), 3);
            }
            GUI.Label(new Rect(r.x + 2, plot.y - 7, 26, 14), "100", label);
            GUI.Label(new Rect(r.x + 2, plot.yMax - 7, 26, 14), "0", label);
            var mid = (lo + hi) / 2;
            foreach (var v in new[] { lo, mid, hi })
                GUI.Label(new Rect(X(v) - 14, plot.yMax + 1, 28, 14), v.ToString("0.##"), new GUIStyle(label) { alignment = TextAnchor.UpperCenter });

            const int steps = 64;
            var xs = Enumerable.Range(0, steps + 1).Select(k => lo + (hi - lo) * k / steps).ToArray();
            var curves = Tuning.Curve(n, xs);
            // Legend at the top right, in the order of the lines.
            var texts = curves.Keys.Select(Tuning.Label).ToList();
            var widths = texts.Select(t => label.CalcSize(new GUIContent(t)).x).ToList();
            var legendX = plot.xMax - widths.Sum(w => w + 14);
            var c = 0;
            foreach (var kv in curves)
            {
                var color = CurveColors[c % CurveColors.Length];
                Handles.color = color;
                Handles.DrawAAPolyLine(2f, xs.Select((x, k) => new Vector3(X(x), Y(kv.Value[k]))).ToArray());
                EditorGUI.DrawRect(new Rect(legendX, r.y + 4, 8, 8), color);
                GUI.Label(new Rect(legendX + 10, r.y - 1, widths[c] + 4, 14), texts[c], label);
                legendX += widths[c] + 14;
                c++;
            }
            Handles.color = pro ? Color.white : Color.black;
            var tx = X(Mathf.Clamp(tryValue, lo, hi));
            Handles.DrawLine(new Vector3(tx, plot.y), new Vector3(tx, plot.yMax));
        }

        // Face view for the try value of a node ("<tree>/<node>"), with the other inputs at the Preview tab's values.
        void ShowTuning(string key)
        {
            if (model == null || doc == null) return;
            tuneShown = key;
            var at2d = key.IndexOf("/2d/", StringComparison.Ordinal);
            if (at2d >= 0)
            {
                var rest = key.Substring(at2d + 4).Split('/');
                var t2 = Tuning.Trees(doc.Json(TableDocument.Expression)).FirstOrDefault(x => x.Name == key.Substring(0, at2d));
                if (t2 == null || !int.TryParse(rest[0], out var k2) || k2 >= t2.Nodes2D.Count) return;
                if (rest.Length == 1)
                {
                    tuneTry2D.TryGetValue(key, out var tp);
                    FaceView.Show(model, Prefab(), Tuning.TryWeights2D(model, t2, t2.Nodes2D[k2], tp.x, tp.y, inputs), $"Try ({tp.x:0.##}, {tp.y:0.##}) ({Tuning.Label(t2.Name)})");
                    return;
                }
                if (!int.TryParse(rest[1], out var c) || c >= t2.Nodes2D[k2].Children.Count) return;
                var (px, py) = t2.Nodes2D[k2].Position(c);
                FaceView.Show(model, Prefab(), Tuning.TryWeights2D(model, t2, t2.Nodes2D[k2], c, inputs), $"({px:0.##}, {py:0.##}) ({Tuning.Label(t2.Name)})");
                return;
            }
            var k = key.LastIndexOf('/');
            var t = Tuning.Trees(doc.Json(TableDocument.Expression)).FirstOrDefault(x => x.Name == key.Substring(0, k));
            if (t == null || !int.TryParse(key.Substring(k + 1), out var i) || i >= t.Nodes.Count) return;
            tuneTry.TryGetValue(key, out var value);
            var weights = Tuning.TryWeights(model, t, t.Nodes[i], value, inputs);
            FaceView.Show(model, Prefab(), weights, $"{Tuning.Label(t.Nodes[i].Input)} = {value:0.##} ({Tuning.Label(t.Name)})");
        }

        // ---- TableEditorChecks.Capture

        // -ftTable <avatar.jsonc>, -ftTab avatar|preview, -ftHelp en|ja|off, -ftState dirty|picker|preview|notable,
        // -ftSet "Input=value,...", -ftPickerPart <part>, -ftPickerSearch <text>
        internal void ApplyCaptureArgs()
        {
            var help = CliArgs.Get("-ftHelp");
            if (help != null) EditorText.Override = help == "ja" ? EditorText.Language.Japanese : help == "off" ? EditorText.Language.Off : EditorText.Language.English;
            var st = CliArgs.Get("-ftState") ?? "";
            var tablePath = CliArgs.Get("-ftTable");
            if (st.Contains("notable")) { table = null; Load(); }
            else if (tablePath != null)
            {
                // The table may be outside Assets (a copy for checking): open it directly.
                table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(tablePath);
                try { doc = TableDocument.Open(tablePath); state.snapshot = doc.Snapshot(); Rebuild(); }
                catch (Exception e) { doc = null; error = e.Message; }
                UpdateDirty();
            }
            var tabArg = CliArgs.Get("-ftTab");
            tab = tabArg == "preview" ? 1 : tabArg == "tuning" ? 2 : tabArg == "inputs" ? 3 : 0;
            var tuneSet = CliArgs.Get("-ftTuneSet");
            if (tuneSet != null && doc != null)
                foreach (var item in tuneSet.Split(';').Where(x => x.Contains("=")))
                {
                    var k = item.LastIndexOf('=');
                    var key = item.Substring(0, k).Split('/');
                    var value = float.Parse(item.Substring(k + 1), System.Globalization.CultureInfo.InvariantCulture);
                    Edit(TableDocument.Expression, "Capture", e =>
                    {
                        var t = Tuning.Trees(e).First(x => x.Name == key[0]);
                        Tuning.SetPosition(t.Nodes[int.Parse(key[1])], int.Parse(key[2]), value);
                    });
                }
            var tuneOpenArg = CliArgs.Get("-ftTuneOpen");
            // "<fold>;!<fold>": open, or close with "!"
            if (tuneOpenArg != null) foreach (var o in tuneOpenArg.Split(';')) { if (o.StartsWith("!")) tuneOpen[o.Substring(1)] = false; else tuneOpen[o] = true; }
            var tuneTry2DArg = CliArgs.Get("-ftTuneTry2D");
            if (tuneTry2DArg != null && doc != null)
            {
                var kk = tuneTry2DArg.IndexOf('=');
                var head = tuneTry2DArg.Substring(0, kk).Split('/');
                var xy = tuneTry2DArg.Substring(kk + 1).Split(',');
                var key2 = $"{head[0]}/2d/{head[1]}";
                tuneTry2D[key2] = new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                ShowTuning(key2);
            }
            var tuneTryArg = CliArgs.Get("-ftTuneTry");
            if (tuneTryArg != null && doc != null)
            {
                var t = Tuning.Trees(doc.Json(TableDocument.Expression)).FirstOrDefault(x => x.Nodes.Count > 0);
                if (t != null)
                {
                    tuneTry[t.Name + "/0"] = float.Parse(tuneTryArg, System.Globalization.CultureInfo.InvariantCulture);
                    ShowTuning(t.Name + "/0");
                }
            }
            scroll.y = CliArgs.GetFloat("-ftScroll", 0);
            if (st.Contains("dirty") && doc != null)
            {
                var a = doc.Json(TableDocument.Avatar);
                var stop = a["stopAutoEyes"]?.Type == JTokenType.Boolean && (bool)a["stopAutoEyes"];
                Edit("Capture", x => TableEdits.SetBool(x, "stopAutoEyes", !stop));
            }
            if (st.Contains("picker"))
            {
                openPickerFor = CliArgs.Get("-ftPickerPart");
                openPickerSearch = CliArgs.Get("-ftPickerSearch") ?? "";
            }
            var set = CliArgs.Get("-ftSet");
            if (set != null)
                foreach (var kv in set.Split(',').Select(s => s.Split('=')).Where(p => p.Length == 2))
                    inputs[kv[0].Trim()] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            captureDevice = CliArgs.Get("-ftDevice");
            var openSw = CliArgs.Get("-ftSwitchOpen");  // "<switch>;..." open rows of Switches
            if (openSw != null) foreach (var n in openSw.Split(';').Where(x => x != "")) switchOpen.Add(n);
            var renameArg = CliArgs.Get("-ftAvatarRename");  // "<part>=<new name>": open the rename panel with the name typed
            if (renameArg != null && renameArg.Contains("=")) { renameAt = renameArg.Substring(0, renameArg.IndexOf('=')); renameCapture = renameArg.Substring(renameArg.IndexOf('=') + 1); selectedPart = renameAt; }
            var addPart = CliArgs.Get("-ftAvatarAddPart");  // "<name>" or "<name>_{s}": as + Add Face Movement
            if (addPart != null && doc != null) AddPendingPart(addPart);
            var showArg = CliArgs.Get("-ftTuneShow");  // all | selected
            if (showArg != null) captureShowSelected = showArg == "selected";
            // "-ftInputAdd <name>;...": check rows of "Not in the settings" (names as written, e.g. "BrowInnerUp{side}")
            var inputAdd = CliArgs.Get("-ftInputAdd");
            if (inputAdd != null && doc != null)
                foreach (var name in inputAdd.Split(';').Where(x => x != ""))
                {
                    var row = InputRows().NotInTable.FirstOrDefault(r => r.Name == name);
                    if (row != null) Edit(TableDocument.Input, "Capture", j => InputChoices.Add(j, row, ChosenDevice(Devices.Load())));
                    else Debug.Log($"[FTG] capture: no input row {name}");
                }
            inputSearch = CliArgs.Get("-ftInputSearch") ?? inputSearch;
            if (CliArgs.Get("-ftShowSwitches") == "1") showSwitches = true;
            if (CliArgs.Get("-ftFaceView") == "1") FaceViewWindow.Open();
            var select = CliArgs.Get("-ftSelectPart");
            if (select != null) { selectedPart = select; ShowSelected(); }
            var hoverShape = CliArgs.Get("-ftHoverShape");
            if (hoverShape != null && doc != null && model != null)
            {
                var part = CliArgs.Get("-ftPickerPart") ?? selectedPart;
                selectedPart = part;
                ShowCandidate(part, TableEdits.PartMesh(doc.Json(TableDocument.Avatar), part) ?? model.DefaultMesh, hoverShape);
            }
            if (st.Contains("preview") && model != null)
            {
                var prefab = Prefab();
                if (prefab != null)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    sceneAvatar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Selection.activeGameObject = sceneAvatar;
                    if (preview.Start(sceneAvatar)) ApplyPreview();
                }
            }
        }

        internal void CaptureTick(int frame)
        {
            // Opens the rename panel after the windows have their places (a popup closes when focus moves).
            if (frame == 40 && renameAt != null && renameRect.height > 0) { var p = renameAt; renameAt = null; ShowRenamePart(p, renameRect); }
        }

        // For the capture of the picker: the open picker window, if any.
        internal static EditorWindow OpenPicker() => Resources.FindObjectsOfTypeAll<PopupWindow>().FirstOrDefault();
    }
}
