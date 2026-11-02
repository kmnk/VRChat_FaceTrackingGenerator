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
        static readonly string[] Tabs = { "Avatar", "Preview", "Tuning" };
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
            state = CreateInstance<TableEditState>();
            state.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayMode;
            var path = EditorPrefs.GetString(TablePrefKey, "");
            if (path != "") table = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            Load();
            PickSelectedAvatar();
        }

        void OnDisable()
        {
            preview.Stop();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayMode;
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
        }

        void Rebuild()
        {
            try { model = doc.Model(); error = null; }
            catch (Exception e) { model = null; error = e.Message; }
            if (preview.Running) ApplyPreview();
            else if (tab == 2 && tuneShown != null) ShowTuning(tuneShown);
            else ShowSelected();
        }

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
            Save();
            base.SaveChanges();
        }

        void Save()
        {
            if (doc == null) return;
            foreach (var p in doc.Save()) AssetDatabase.ImportAsset(p);
            UpdateDirty();
        }

        bool ConfirmDiscard() => doc == null || !doc.IsDirty() || EditorUtility.DisplayDialog("FT Settings Editor", EditorText.Get("dialog.discard"), "Discard", "Cancel");

        // ---- drawing

        void OnGUI()
        {
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
            else DrawTuningTab();
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
                using (new EditorGUI.DisabledScope(doc == null || !doc.IsDirty()))
                    if (GUILayout.Button("Save", GUILayout.Width(70))) Save();
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
            if (avatar["parts"] is JObject parts) foreach (var p in parts.Properties()) all.Add(p.Name);
            if (avatar["missingParts"] is JArray mp) foreach (var x in mp.Where(x => x.Type == JTokenType.String)) all.Add((string)x);
            return all.Select(p => (p, used.Contains(p))).ToList();
        }

        static void CollectParts(Node n, HashSet<string> into)
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
                            PopupWindow.Show(r, new BlendshapePicker(shapes, shape, picked => { Edit("Set FT part", a => TableEdits.SetPart(a, p, picked)); Repaint(); }, search,
                                hover: name => ShowCandidate(p, meshForPart, name),
                                closed: () => { if (!preview.Running) ShowSelected(); Repaint(); }));
                        }
                    }
                    GUI.contentColor = color;

                    EditorGUI.BeginChangeCheck();
                    var m = GUILayout.Toggle(isMissing, new GUIContent("", EditorText.Get("tip.missing")), GUILayout.Width(missW));
                    if (EditorGUI.EndChangeCheck()) { var p = part; Edit("Set FT missing part", a => TableEdits.SetMissing(a, p, m, removedParts)); }
                }
                var rowRect = GUILayoutUtility.GetLastRect();
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
                if (how != null)
                {
                    PartsLine("    " + how);
                    RowBackground(i);
                }
                i++;
            }
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
                    v = EditorGUILayout.Slider(sent ? input.Name : input.Name + EditorText.Get("device.notSent"), v, lo, hi);
                    if (EditorGUI.EndChangeCheck()) { inputs[input.Name] = (float)Math.Round(v, 2); changed = true; }
                }
                if (partsOf.TryGetValue(input.Name, out var line)) PartsLine(line);
            }
            foreach (var input in model.Inputs.Where(x => x.IsDerived))
            {
                var v = input.Average.Average(s => inputs.TryGetValue(s, out var x) ? x : 0f);
                var (lo, hi) = SliderRange(input.Name);
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.Slider(input.Name + " (average)", v, lo, hi);
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
                        unusedInputs[name] = (float)Math.Round(EditorGUILayout.Slider(name, v, k.min, k.max), 2);
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
            var ja = EditorText.Current == EditorText.Language.Japanese;

            EditorGUILayout.LabelField("Thresholds", EditorStyles.boldLabel);
            Help("help.thresholds");
            var withNodes = trees.Where(t => t.Nodes.Count > 0).ToList();
            if (withNodes.Count == 0) EditorGUILayout.LabelField(EditorText.Get("tune.no1d"), EditorStyles.wordWrappedLabel);
            var firstNode = true;
            foreach (var t in withNodes)
            {
                tuneOpen.TryGetValue(t.Name, out var open);
                if (!tuneOpen.ContainsKey(t.Name)) open = t == withNodes[0];
                tuneOpen[t.Name] = open = EditorGUILayout.Foldout(open, Tuning.TreeTitle(t), true, EditorStyles.foldoutHeader);
                if (!open) { firstNode = false; continue; }
                EditorGUI.indentLevel++;
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    var key = t.Name + "/" + i;
                    if (!tuneOpen.ContainsKey(key)) tuneOpen[key] = firstNode;
                    firstNode = false;
                    tuneOpen[key] = EditorGUILayout.Foldout(tuneOpen[key], Tuning.NodeTitle(t.Nodes[i]), true);
                    if (tuneOpen[key]) DrawTuningNode(t, i, key);
                }
                EditorGUI.indentLevel--;
            }
            var only2d = trees.Where(t => t.Nodes.Count == 0 && t.Has2D).Select(t => Tuning.Label(t.Name)).ToList();
            if (only2d.Count > 0)
                PartsLine(string.Format(EditorText.Get("tune.only2d"), string.Join(ja ? "・" : ", ", only2d)));

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
        }

        void DrawTuningNode(Tuning.Tree t, int index, string key)
        {
            var n = t.Nodes[index];
            var (lo, hi) = Tuning.InputRange(n);

            // Graph: amount of each face movement (0-100) along the input, positions as dotted lines, the try value as a line.
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(100, 90, GUILayout.ExpandWidth(true)));
            tuneTry.TryGetValue(key, out var tryValue);
            if (Event.current.type == EventType.Repaint) DrawTuningGraph(rect, n, lo, hi, tryValue);

            for (int i = 0; i < n.Children.Count; i++)
            {
                var (plo, phi) = Tuning.PositionRange(n, i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUILayout.Slider(string.Format(EditorText.Get("tune.position"), i + 1), n.Position(i), plo, phi);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var j = i; var at = index; var name = t.Name;
                        tuneShown = key;
                        Edit(TableDocument.Expression, "Set FT position", e => Tuning.SetPosition(Tuning.Trees(e).First(x => x.Name == name).Nodes[at], j, v));
                    }
                    SideNote(Tuning.ChildText(n.Child(i)), 150);
                }
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
            EditorGUILayout.Space(4);
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
            tab = tabArg == "preview" ? 1 : tabArg == "tuning" ? 2 : 0;
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

        internal void CaptureTick(int frame) { }

        // For the capture of the picker: the open picker window, if any.
        internal static EditorWindow OpenPicker() => Resources.FindObjectsOfTypeAll<PopupWindow>().FirstOrDefault();
    }
}
