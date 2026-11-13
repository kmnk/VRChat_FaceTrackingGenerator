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
        static readonly string[] Tabs = { "Avatar", "Preview" };
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
        string avatarSource = "none";  // how sceneAvatar was filled: saved, prefab, selected, chosen or none (for the checks)
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

        // A state shown in Face view by the state machine window (state machine, state), or null.
        (string sm, string state)? stateShown;

        // inputs tab: the search text

        // The open settings editor, for the state machine window. Changed is raised after the tables change.
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
                ProjectSelection.SetTable(avatarTablePath);
                w.Load();
                w.RestoreAvatar();
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
            EditorSceneManager.sceneOpened += OnSceneOpened;
            var path = ProjectSelection.TablePath;
            if (path != null) table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Load();
            RestoreAvatar();
        }

        void OnDisable()
        {
            preview.Stop();
            if (Current == this) { Current = null; Changed?.Invoke(); }
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            BuildGuard.Changed -= Repaint;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
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
            if (sel == null || !sel.scene.IsValid() || sel.GetComponent<VRCAvatarDescriptor>() == null) return;
            sceneAvatar = sel;
            avatarSource = "selected";
            ProjectSelection.SetAvatar(TablePath, sel);
        }

        // Fills the avatar field from what this project chose before for the settings file in the open scenes, else
        // from the only avatar made from the settings file's prefab, else from the selection. Does not start the preview.
        void RestoreAvatar()
        {
            var found = ProjectSelection.FindAvatar(TablePath, Prefab(), out var source);
            sceneAvatar = found;
            avatarSource = source;
            if (found != null) { if (source == "prefab") ProjectSelection.SetAvatar(TablePath, found); }
            else PickSelectedAvatar();
        }

        void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            if (sceneAvatar != null) return;
            RestoreAvatar();
            Repaint();
        }

        // The settings file field: opens the file and records the choice for this project.
        void ChooseTable(DefaultAsset picked)
        {
            table = picked;
            ProjectSelection.SetTable(TablePath);
            Load();
            RestoreAvatar();
        }

        // The "Avatar in scene" field: records the choice for the settings file in the avatar's scene (none forgets it).
        void ChooseAvatar(GameObject avatar)
        {
            preview.Stop();
            sceneAvatar = avatar;
            avatarSource = avatar != null ? "chosen" : "none";
            ProjectSelection.SetAvatar(TablePath, avatar);
        }

        // For TableEditorChecks.Capture: the settings file and the avatar the window shows.
        internal string SelectionState()
        {
            string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
            var avatar = sceneAvatar != null ? HierarchyPath(sceneAvatar.transform) : "none";
            return $"table={TablePath ?? "none"} avatar={avatar} source={avatarSource} preview={preview.Running.ToString().ToLower()}";
        }

        string TablePath => table != null ? AssetDatabase.GetAssetPath(table) : null;

        // ---- loading, editing, saving

        void Load()
        {
            preview.Stop();
            doc = null; model = null; error = null;
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
            if (preview.Running) ApplyPreview();
            else if (stateShown != null) ShowState(stateShown.Value.sm, stateShown.Value.state);
            else ShowSelected();
            if (Current == this) Changed?.Invoke();
        }

        // The device chosen in the Preview (Templates/Devices), or null for none.
        Devices.Device ChosenDevice(List<Devices.Device> devices) =>
            devices.FirstOrDefault(d => d.Id == (captureDevice ?? EditorPrefs.GetString(DevicePrefKey, "")));

        internal bool PreviewRunning => preview.Running;

        internal void StopPreviewForBuild()
        {
            preview.Stop();
            Repaint();
        }

        // ---- the state machine window

        internal JObject ExpressionJson => doc?.Json(TableDocument.Expression);
        internal JObject AvatarJson => doc?.Json(TableDocument.Avatar);
        internal TableModel Model => model;

        // One unsaved edit of the expression table that returns a value (for the state machine window).
        internal T EditExpression<T>(string undoName, Func<JObject, T> edit)
        {
            T result = default;
            if (doc == null) return result;
            Edit(TableDocument.Expression, undoName, e => result = edit(e));
            return result;
        }

        // One unsaved edit of the avatar table (a new part from the state machine window).
        internal void EditAvatar(string undoName, Action<JObject> edit)
        {
            if (doc != null) Edit(TableDocument.Avatar, undoName, edit);
        }

        // Part names to check new names against: the tables' and the ones added but not yet given a blendshape.
        internal List<string> AllPartNames() =>
            doc == null ? new List<string>() : PartNames.Used(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression)).Concat(pendingParts).ToList();

        // Shows a state's face in Face view: neutral keys at 1 (the face with the tracking at its strongest is not
        // useful), so the face is the state's fixed amounts and what its overrides turn off (spec R15).
        internal void ShowState(string sm, string st)
        {
            stateShown = (sm, st);
            if (model == null || model.StateMachine(sm)?.State(st) == null) { FaceView.Clear(); return; }
            var weights = FaceEval.Evaluate(model, inputs, new Dictionary<string, string> { [sm] = st });
            FaceView.Show(model, Prefab(), weights, $"{sm} = {st}");
        }

        // For TableEditorChecks.Capture: the tab.
        internal string TabState() => $"tab={Tabs[tab]}";

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
            else DrawPreviewTab();
            EditorGUILayout.EndScrollView();
        }

        void DrawTop()
        {
            // The file and what is done to it (Reload, Save, Share) on one row; the windows that show it on the next.
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var picked = (DefaultAsset)EditorGUILayout.ObjectField(EditorText.Label("Settings file", "tip.table"), table, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck() && picked != table && ConfirmDiscard()) ChooseTable(picked);
                using (new EditorGUI.DisabledScope(table == null))
                    if (GUILayout.Button("Reload", GUILayout.Width(60)) && ConfirmDiscard()) Load();
                using (new EditorGUI.DisabledScope(doc == null || !doc.IsDirty()))
                    if (GUILayout.Button("Save", GUILayout.Width(50))) Save();
                using (new EditorGUI.DisabledScope(table == null))
                    if (EditorGUILayout.DropdownButton(new GUIContent("Share"), FocusType.Passive, GUILayout.Width(60)))
                        ShareMenu().DropDown(GUILayoutUtility.GetLastRect());
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Face view", GUILayout.Width(80)))
                {
                    FaceViewWindow.Open();
                    if (preview.Running) ApplyPreview(); else ShowSelected();
                }
                using (new EditorGUI.DisabledScope(doc == null))
                    if (GUILayout.Button("State Machine", GUILayout.Width(105))) StateMachineWindow.Open();
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

        // ---- Share: the expression table to and from a folder (TableShare)

        internal const string ShareExport = "Export expression…", ShareImport = "Import expression…";

        // The Share menu's items and whether each can be pressed. Both wait while there are unsaved changes:
        // export writes the saved files, and import would overwrite what is being edited.
        internal static List<(string label, bool enabled)> ShareMenuItems(bool dirty) =>
            new List<(string, bool)> { (ShareExport, !dirty), (ShareImport, !dirty) };

        GenericMenu ShareMenu()
        {
            var menu = new GenericMenu();
            foreach (var (label, enabled) in ShareMenuItems(doc != null && doc.IsDirty()))
            {
                if (!enabled) { menu.AddDisabledItem(new GUIContent(label)); continue; }
                GenericMenu.MenuFunction run = label == ShareExport ? (GenericMenu.MenuFunction)ShareExportTables : ShareImportTables;
                menu.AddItem(new GUIContent(label), false, run);
            }
            return menu;
        }

        void ShareExportTables()
        {
            var path = TablePath;
            if (path == null) return;
            var folder = EditorUtility.OpenFolderPanel(ShareExport, "", "");
            if (string.IsNullOrEmpty(folder)) return;
            var existing = TableShare.Existing(folder);
            if (existing.Count > 0 && !EditorUtility.DisplayDialog("FT Settings Editor",
                    string.Format(EditorText.Get("share.overwrite"), string.Join(", ", existing)), "Overwrite", "Cancel")) return;
            try { TableShare.Export(path, folder); }
            catch (Exception e) when (e is TableLoader.TableException || e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("FT Settings Editor", e.Message, "OK");
                return;
            }
            ShowNotification(new GUIContent(string.Format(EditorText.Get("share.exported"), folder)), 4);
        }

        void ShareImportTables()
        {
            var path = TablePath;
            if (path == null) return;
            var folder = EditorUtility.OpenFolderPanel(ShareImport, "", "");
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                var changes = TableShare.Changes(path, folder);
                if (changes.Count == 0) { EditorUtility.DisplayDialog("FT Settings Editor", EditorText.Get("share.noChanges"), "OK"); return; }
                if (!EditorUtility.DisplayDialog("FT Settings Editor",
                        string.Format(EditorText.Get("share.confirmImport"), string.Join(", ", changes)), "Import", "Cancel")) return;
                TableShare.Import(path, folder);
            }
            catch (Exception e) when (e is TableLoader.TableException || e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("FT Settings Editor", e.Message, "OK");
                return;
            }
            Load();
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
        }

        // Parts used by the expression layer (also those the avatar layer lacks) and parts in the avatar layer, by region:
        // within a region, "region.movement" first, then the parts with a state in the middle (design.md "設定エディタ").
        List<(string part, bool used)> PartRows(JObject avatar)
        {
            var used = new HashSet<string>(PartNames.InExpression(doc.Json(TableDocument.Expression)), StringComparer.Ordinal);
            var all = new SortedSet<string>(used, StringComparer.Ordinal);
            all.UnionWith(pendingParts);
            if (avatar["parts"] is JObject parts) foreach (var p in parts.Properties()) all.Add(p.Name);
            if (avatar["missingParts"] is JArray mp) foreach (var x in mp.Where(x => x.Type == JTokenType.String)) all.Add((string)x);
            return all.Where(p => p.Contains('.'))
                .OrderBy(p => PartNames.Region(p), StringComparer.Ordinal)
                .ThenBy(p => PartNames.IsForm(p) && PartNames.Middle(p) != null ? 1 : 0)
                .ThenBy(p => p, StringComparer.Ordinal)
                .Select(p => (p, used.Contains(p))).ToList();
        }

        static GUIStyle regionBand, middleTag;

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

            const float partW = 150, missW = 60, scaleW = 130;
            if (scaleHeading == null)
            {
                scaleHeading = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
                scaleField = new GUIStyle(EditorStyles.numberField) { alignment = TextAnchor.MiddleRight };
                scaleOne = new GUIStyle(scaleField);
                scaleOne.normal.textColor = scaleOne.hover.textColor = new Color(0.54f, 0.54f, 0.54f);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Movement", EditorStyles.miniBoldLabel, GUILayout.Width(partW));
                GUILayout.Label("Blendshape", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Scale", EditorStyles.miniBoldLabel, GUILayout.Width(scaleW));
                GUILayout.Label("Missing", EditorStyles.miniBoldLabel, GUILayout.Width(missW));
                GUILayout.Space(24);
            }
            var shapeCache = new Dictionary<string, List<string>>();
            var partUsage = model != null ? PartUsage.Of(model) : null;
            var i = 0;
            if (regionBand == null)
            {
                regionBand = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(6, 4, 2, 2) };
                middleTag = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 0, 0) };
            }
            string lastRegion = null;
            foreach (var (part, used) in rows)
            {
                if (!string.IsNullOrEmpty(partFilter) && part.IndexOf(partFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // A band with the region's name before its first part (only regions with a part shown under the filter).
                var region = PartNames.Region(part);
                if (region != lastRegion)
                {
                    lastRegion = region;
                    EditorGUILayout.Space(2);
                    var band = GUILayoutUtility.GetRect(new GUIContent(region), regionBand, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(band, new Color(1, 1, 1, 0.06f));
                    GUI.Label(band, region, regionBand);
                    // + at the right end: a new face movement with "<region>." typed.
                    var plus = new Rect(band.xMax - 24, band.y + 1, 22, band.height - 2);
                    if (Event.current.type == EventType.Repaint && region == newPartRegionCapture) newPartRegionRect = plus;
                    if (GUI.Button(plus, new GUIContent("+", string.Format(EditorText.Get("part.addIn"), region)), EditorStyles.miniButton))
                        ShowNewPart(plus, null, region + ".");
                }
                var isMissing = TableEdits.IsMissing(avatar, part);
                var shape = TableEdits.PartShape(avatar, part);
                var mesh = TableEdits.PartMesh(avatar, part) ?? defaultMesh;
                if (!shapeCache.TryGetValue(mesh ?? "", out var shapes)) shapeCache[mesh ?? ""] = shapes = ShapesOf(prefab, mesh);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(partW)))
                    {
                        // A part with a state in the middle ("eye.joy.tear") shows the state as a tag.
                        var middle = PartNames.IsForm(part) ? PartNames.Middle(part) : null;
                        if (middle != null)
                        {
                            var tag = GUILayoutUtility.GetRect(new GUIContent(middle), middleTag, GUILayout.ExpandWidth(false));
                            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(tag, new Color(0.5f, 0.5f, 0.5f, 0.35f));
                            GUI.Label(tag, middle, middleTag);
                        }
                        GUILayout.Label(part, GUILayout.ExpandWidth(false));
                        if (!used) GUILayout.Label("unused", EditorStyles.miniLabel);
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

                    // Scale: only for a part with a blendshape. Written when Enter is pressed or the field is left.
                    if (!isMissing && shape != null)
                    {
                        var scale = TableEdits.PartScale(avatar, part);
                        EditorGUI.BeginChangeCheck();
                        var s = (float)Math.Round(EditorGUILayout.Slider(scale, 0, 1, GUILayout.Width(scaleW)), 2);
                        if (EditorGUI.EndChangeCheck() && s != scale) { var p = part; Edit("Set FT part scale", a => TableEdits.SetPartScale(a, p, s)); }
                    }
                    else GUILayout.Space(scaleW + 4);

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

            // + Add Face Movement: a name, then the row opens its blendshape list.
            var addRect = GUILayoutUtility.GetRect(new GUIContent("+ Add Face Movement"), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint) addPartRect = addRect;
            if (GUI.Button(addRect, "+ Add Face Movement", EditorStyles.miniButton)) ShowNewPart(addRect, null);
        }

        void ShowNewPart(Rect at, string typed, string prefix = "")
        {
            var popup = new NewPartPopup(AllPartNames(), AddPendingPart, prefix);
            if (typed != null) popup.SetNameForCapture(typed);
            PopupWindow.Show(at, popup);
        }

        static GUIStyle scaleHeading, scaleField, scaleOne;
        string renameAt, renameCapture;  // for TableEditorChecks.Capture: open the rename panel at this row
        Rect renameRect;
        string newPartCapture;  // for TableEditorChecks.Capture: open "+ Add Face Movement" with this name typed
        Rect addPartRect;
        string newPartRegionCapture;  // for TableEditorChecks.Capture: open the + of this region's band
        Rect newPartRegionRect;

        void ShowRenamePart(string part, Rect at)
        {
            var avatar = doc.Json(TableDocument.Avatar);
            var expression = doc.Json(TableDocument.Expression);
            var popup = new RenamePartPopup(part,
                (to, together) => PartRename.Check(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression), part, to, together, pendingParts),
                (to, together) => RenamePart(part, to, together));
            if (renameCapture != null) popup.SetNameForCapture(renameCapture);
            PopupWindow.Show(at, popup);
        }

        // Renames in both tables (PartRename); rows still waiting for a blendshape are renamed here only.
        internal void RenamePart(string from, string to, bool together)
        {
            var map = PartRename.Map(null, from, to, together);
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

        // Adds "name" as a row waiting for a blendshape, and opens its list.
        internal void AddPendingPart(string written)
        {
            var parts = new[] { written };
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
                if (EditorGUI.EndChangeCheck()) ChooseAvatar(a);
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
            // The tracking keys ("key:<key>"). The keys forwarded for the transitions are the same values, so they are not
            // shown again.
            foreach (var input in model.Inputs.Where(x => x.IsTrack))
            {
                var (lo, hi, label, sent) = PreviewSlider(input, device);
                var v = inputs.TryGetValue(input.Name, out var x) ? x : 0f;
                using (new EditorGUI.DisabledScope(!sent))
                {
                    EditorGUI.BeginChangeCheck();
                    v = EditorGUILayout.Slider(new GUIContent(label, ParameterHelp.Tooltip(model, input)), v, lo, hi);
                    if (EditorGUI.EndChangeCheck()) { inputs[input.Name] = (float)Math.Round(v, 2); changed = true; }
                }
                if (partsOf.TryGetValue(input.Name, out var line)) PartsLine(line);
            }

            // Menu: a state chosen as the menu does; the transitions stop until None (design.md "設定エディタ").
            var withMenu = model.StateMachines.Where(sm => sm.MenuStates.Any()).ToList();
            if (withMenu.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Menu", EditorStyles.boldLabel);
                Help("help.previewMenu");
                foreach (var sm in withMenu)
                {
                    var options = new List<string> { "None" };
                    options.AddRange(sm.MenuStates.Select(x => x.Name));
                    var index = switches.Pinned.TryGetValue(sm.Name, out var pin) ? options.IndexOf(pin) : 0;
                    EditorGUI.BeginChangeCheck();
                    index = EditorGUILayout.Popup(sm.Name, Math.Max(0, index), options.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        switches.Choose(sm.Name, index == 0 ? null : options[index]);
                        changed = true;
                    }
                }
            }
            if (device != null)
            {
                var unused = device.Keys.Where(k => !Devices.Device.UsedBy(model, k.key)).ToList();
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
            foreach (var o in Overlaps.Find(FaceEval.EvaluateParts(model, inputs, states)))
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
                var source = string.Join(", ", row.From);
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

            // The states now (folded)
            if (model.StateMachines.Count > 0)
            {
                EditorGUILayout.Space(6);
                showSwitches = EditorGUILayout.Foldout(showSwitches, EditorText.Heading("switches.title"), true, EditorStyles.foldoutHeader);
                if (showSwitches) Help("help.switches");
                if (showSwitches)
                    foreach (var sm in model.StateMachines)
                    {
                        var byMenu = switches.Pinned.ContainsKey(sm.Name);
                        var now = states.TryGetValue(sm.Name, out var st) ? st : sm.States[0].Name;
                        EditorGUILayout.LabelField($"{sm.Name}: {now}  " + EditorText.Get(byMenu ? "switches.byMenu" : "switches.byValue"));
                    }
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(EditorText.Get("note.preview"), MessageType.Info);
        }

        // The slider range: the values the transitions compare the input with (0 to 1 at least).
        (float, float) SliderRange(string input) => Devices.ThresholdRange(model, input);

        // A slider of the preview: the thresholds' range fitted to what the device sends (design.md "Preview").
        (float lo, float hi, string label, bool sent) PreviewSlider(InputDef input, Devices.Device device)
        {
            var (lo, hi) = SliderRange(input.Name);
            if (device == null) return (lo, hi, input.Name, true);
            var s = Devices.Slider(model, input, device, (lo, hi));
            if (!s.sent) return (lo, hi, input.Name + EditorText.Get("device.notSent"), false);
            var label = s.clipped ? input.Name + string.Format(EditorText.Get("device.range"), s.device.lo.ToString("0.##"), s.device.hi.ToString("0.##")) : input.Name;
            return (s.lo, s.hi, label, true);
        }

        void ApplyPreview(Dictionary<string, string> states = null)
        {
            if (model == null || !preview.Running) return;
            states = states ?? switches.Update(model, inputs);
            var weights = FaceEval.Evaluate(model, inputs, states);
            notOnAvatar = preview.Apply(model, weights);
            nothingApplied = FacePreview.Targets(model, weights).All(notOnAvatar.Contains);
            FaceView.Show(model, Prefab(), weights, "Preview");
        }

        static void SeparatorLine()
        {
            EditorGUILayout.Space(6);
            var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));
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
            // -ftChooseTable / -ftChooseAvatar: choose as the fields do ("none" clears; the avatar is a Hierarchy path)
            var chooseTable = CliArgs.Get("-ftChooseTable");
            if (chooseTable != null) ChooseTable(chooseTable == "none" ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(chooseTable));
            var chooseAvatar = CliArgs.Get("-ftChooseAvatar");
            if (chooseAvatar != null) ChooseAvatar(chooseAvatar == "none" ? null : GameObject.Find(chooseAvatar) ?? throw new ArgumentException("not in the scene: " + chooseAvatar));
            var tabArg = CliArgs.Get("-ftTab");
            tab = tabArg == "preview" ? 1 : 0;
            scroll.y = CliArgs.GetFloat("-ftScroll", 0);
            if (st.Contains("dirty") && doc != null)
            {
                var a = doc.Json(TableDocument.Avatar);
                var over = a["overwriteFaceTracking"]?.Type == JTokenType.Boolean && (bool)a["overwriteFaceTracking"];
                Edit("Capture", x => TableEdits.SetBool(x, "overwriteFaceTracking", !over));
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
            var renameArg = CliArgs.Get("-ftAvatarRename");  // "<part>=<new name>": open the rename panel with the name typed
            if (renameArg != null && renameArg.Contains("=")) { renameAt = renameArg.Substring(0, renameArg.IndexOf('=')); renameCapture = renameArg.Substring(renameArg.IndexOf('=') + 1); selectedPart = renameAt; }
            newPartCapture = CliArgs.Get("-ftAvatarNewPart");  // "<name>": open "+ Add Face Movement" with the name typed
            newPartRegionCapture = CliArgs.Get("-ftAvatarNewPartIn");  // "<region>": press the + of the region's band
            var addPart = CliArgs.Get("-ftAvatarAddPart");  // "<name>": as + Add Face Movement
            if (addPart != null && doc != null) AddPendingPart(addPart);
            if (CliArgs.Get("-ftShowSwitches") == "1") showSwitches = true;
            // "-ftPreviewMenu <state machine>=<state>,...": choose in the Preview's Menu
            var menuArg = CliArgs.Get("-ftPreviewMenu");
            if (menuArg != null)
                foreach (var kv in menuArg.Split(',').Select(x => x.Split('=')).Where(x => x.Length == 2))
                    switches.Choose(kv[0].Trim(), kv[1].Trim() == "None" ? null : kv[1].Trim());
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
            if (frame == 40 && newPartCapture != null && addPartRect.height > 0) { var n = newPartCapture; newPartCapture = null; ShowNewPart(addPartRect, n); }
            if (frame == 40 && newPartRegionCapture != null && newPartRegionRect.height > 0) { var r = newPartRegionCapture; newPartRegionCapture = null; ShowNewPart(newPartRegionRect, null, r + "."); }
        }

        // For the capture of the picker: the open picker window, if any.
        internal static EditorWindow OpenPicker() => Resources.FindObjectsOfTypeAll<PopupWindow>().FirstOrDefault();
    }
}
