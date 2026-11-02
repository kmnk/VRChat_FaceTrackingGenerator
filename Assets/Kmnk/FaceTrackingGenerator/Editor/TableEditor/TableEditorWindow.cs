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
    // Tools/Kmnk/Face Tracking Table Editor: edits the avatar table and shows the tables' weights on an avatar in
    // the scene. The tables are edited as JSON trees (TableDocument) and written only on Save.
    public sealed class TableEditorWindow : EditorWindow
    {
        const string TablePrefKey = "Kmnk.FaceTrackingGenerator.AvatarTable";  // shared with GeneratorWindow
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
        readonly FacePreview preview = new FacePreview();
        readonly SwitchTracker switches = new SwitchTracker();
        readonly Dictionary<string, float> inputs = new Dictionary<string, float>();
        HashSet<string> notOnAvatar = new HashSet<string>();
        bool nothingApplied;
        string selectedPart;

        [MenuItem("Tools/Kmnk/Face Tracking Table Editor")]
        static void Open() => GetWindow<TableEditorWindow>("FT Table Editor");

        // Opens the table editor on a table (the generator window's "Open Table Editor").
        public static TableEditorWindow OpenTable(string avatarTablePath)
        {
            var w = GetWindow<TableEditorWindow>("FT Table Editor");
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
            titleContent = new GUIContent("FT Table Editor");
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
        void Edit(string undoName, Action<JObject> edit)
        {
            Undo.RecordObject(state, undoName);
            edit(doc.Json(TableDocument.Avatar));
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

        bool ConfirmDiscard() => doc == null || !doc.IsDirty() || EditorUtility.DisplayDialog("FT Table Editor", EditorText.Get("dialog.discard"), "Discard", "Cancel");

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
            else DrawPreviewTab();
            EditorGUILayout.EndScrollView();
        }

        void DrawTop()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (DefaultAsset)EditorGUILayout.ObjectField(EditorText.Label("avatar.jsonc", "tip.table"), table, typeof(DefaultAsset), false);
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
        static void PartsLine(string text)
        {
            if (partsStyle == null)
            {
                partsStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                partsStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
            }
            EditorGUILayout.LabelField(text, partsStyle);
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
            Heading("Parts", $"{mapped} mapped · {missing} missing · {notSet} not set");
            Help("help.parts");
            partFilter = EditorGUILayout.TextField(partFilter, EditorStyles.toolbarSearchField);

            const float partW = 150, missW = 60;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Part", EditorStyles.miniBoldLabel, GUILayout.Width(partW));
                GUILayout.Label("Blendshape", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Missing", EditorStyles.miniBoldLabel, GUILayout.Width(missW));
            }
            var shapeCache = new Dictionary<string, List<string>>();
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
                RowBackground(i++);
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
            if (preview.Running && nothingApplied) EditorGUILayout.HelpBox(EditorText.Get("msg.noApplied"), MessageType.Warning);
            if (model == null) return;

            var changed = false;
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    inputs.Clear();
                    switches.Reset();
                    changed = true;
                }
            }
            Help("help.inputs");
            var partsOf = InputParts.Of(model);
            foreach (var input in model.Inputs.Where(x => !x.IsDerived))
            {
                var (lo, hi) = SliderRange(input.Name);
                var v = inputs.TryGetValue(input.Name, out var x) ? x : 0f;
                EditorGUI.BeginChangeCheck();
                v = EditorGUILayout.Slider(input.Name, v, lo, hi);
                if (EditorGUI.EndChangeCheck()) { inputs[input.Name] = (float)Math.Round(v, 2); changed = true; }
                if (partsOf.TryGetValue(input.Name, out var line)) PartsLine(line);
            }
            foreach (var input in model.Inputs.Where(x => x.IsDerived))
            {
                var v = input.Average.Average(s => inputs.TryGetValue(s, out var x) ? x : 0f);
                var (lo, hi) = SliderRange(input.Name);
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.Slider(input.Name + " (average)", v, lo, hi);
            }

            var states = switches.Update(model, inputs);
            if (model.Switches.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Switches", EditorStyles.boldLabel);
                Help("help.switches");
                foreach (var sw in model.Switches)
                {
                    var options = new List<string> { $"Auto ({switches.Current(sw.Name) ?? sw.States[0]})" };
                    options.AddRange(sw.States);
                    var index = switches.Pinned.TryGetValue(sw.Name, out var pin) ? sw.States.IndexOf(pin) + 1 : 0;
                    EditorGUI.BeginChangeCheck();
                    index = EditorGUILayout.Popup(sw.Name, index, options.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (index == 0) switches.Pinned.Remove(sw.Name); else switches.Pinned[sw.Name] = sw.States[index - 1];
                        changed = true;
                    }
                }
                if (changed) states = switches.Update(model, inputs);
            }
            if (changed && preview.Running) ApplyPreview(states);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Weights", EditorStyles.boldLabel);
            Help("help.weights");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Blendshape", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Amount", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUILayout.Label("From", EditorStyles.miniBoldLabel, GUILayout.Width(180));
            }
            var i = 0;
            foreach (var row in PreviewWeights.Rows(model, inputs, states))
            {
                var (mesh, shape) = FacePreview.Split(row.Key);
                var name = mesh == model.DefaultMesh ? shape : row.Key;
                if (notOnAvatar.Contains(row.Key)) name += " " + EditorText.Get("msg.notOnAvatar");
                var old = GUI.contentColor;
                if (notOnAvatar.Contains(row.Key)) GUI.contentColor = Color.gray;
                else if (row.Amount > 100) GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(name, GUILayout.ExpandWidth(true));
                    GUILayout.Label(Math.Round(row.Amount).ToString(), GUILayout.Width(60));
                    GUILayout.Label(string.Join(", ", row.From), GUILayout.Width(180));
                }
                GUI.contentColor = old;
                RowBackground(i++);
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
            tab = CliArgs.Get("-ftTab") == "preview" ? 1 : 0;
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
