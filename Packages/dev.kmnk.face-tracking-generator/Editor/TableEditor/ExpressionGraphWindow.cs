using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kmnk.FaceTrackingGenerator
{
    // Tools/Kmnk/Face Tracking Expression Graph: the expression trees of the settings open in the settings editor, as
    // boxes and lines (ExpressionGraph). Selecting a box shows its node in the Tuning tab; dragging from a position's port
    // to empty space splits that position with 1D. Edits go to the settings editor as unsaved changes.
    public sealed class ExpressionGraphWindow : EditorWindow
    {
        string tree;
        string selectedPath;
        readonly HashSet<string> openBoxes = new HashSet<string>();  // "<tree>|<box path>" of the boxes shown open
        bool frameNext = true;
        ToolbarMenu treeMenu;
        ToolbarButton newButton, removeTreeButton;
        Label help;
        Toolbar toolbar;
        Button resumeButton;
        Graph graph;

        [MenuItem("Tools/Kmnk/Face Tracking Expression Graph")]
        public static ExpressionGraphWindow Open() => GetWindow<ExpressionGraphWindow>("FT Expression Graph");

        void OnEnable()
        {
            titleContent = new GUIContent("FT Expression Graph");
            toolbar = new Toolbar();
            treeMenu = new ToolbarMenu { text = "" };
            treeMenu.style.minWidth = 120;
            toolbar.Add(treeMenu);
            toolbar.Add(new ToolbarButton(() => graph.FrameAll()) { text = "Frame All" });
            newButton = new ToolbarButton(() => ShowNewTree()) { text = "+ New Expression" };
            toolbar.Add(newButton);
            removeTreeButton = new ToolbarButton(() => AskRemoveTree(tree)) { text = "Remove Expression" };
            toolbar.Add(removeTreeButton);
            rootVisualElement.Add(toolbar);
            help = new Label();
            help.style.color = new Color(0.6f, 0.6f, 0.6f);
            help.style.fontSize = 11;
            help.style.paddingLeft = 6; help.style.paddingTop = 3; help.style.paddingBottom = 3;
            help.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(help);
            resumeButton = new Button(BuildGuard.End) { text = "Resume" };
            resumeButton.style.alignSelf = Align.FlexStart;
            resumeButton.style.marginLeft = 6;
            rootVisualElement.Add(resumeButton);
            graph = new Graph(this);
            graph.style.flexGrow = 1;
            rootVisualElement.Add(graph);
            TableEditorWindow.Changed += Refresh;
            EditorText.Changed += Refresh;
            BuildGuard.Changed += Refresh;
            Refresh();
        }

        void OnDisable() { TableEditorWindow.Changed -= Refresh; EditorText.Changed -= Refresh; BuildGuard.Changed -= Refresh; }

        void OnFocus() => Refresh();

        internal string Tree => tree;
        internal Graph View => graph;

        internal void SelectTree(string name)
        {
            tree = name;
            selectedPath = null;
            frameNext = true;
            Refresh();
        }

        // Draws the graph again from the settings editor's tables (after any edit there).
        internal void Refresh()
        {
            if (graph == null) return;
            // During a build: the note and the Resume button, as in the other windows, and no toolbar.
            var building = BuildGuard.Building;
            toolbar.SetEnabled(!building);
            resumeButton.style.display = building ? DisplayStyle.Flex : DisplayStyle.None;
            if (building)
            {
                help.text = EditorText.Get("build.paused");
                help.style.display = DisplayStyle.Flex;
                graph.style.display = DisplayStyle.None;
                return;
            }
            graph.style.display = DisplayStyle.Flex;
            var editor = TableEditorWindow.Current;
            var expression = editor?.ExpressionJson;
            var names = ExpressionGraph.TreeNames(expression);
            if (expression == null)
            {
                help.text = EditorText.Get("graph.noTable");
                help.style.display = DisplayStyle.Flex;
                treeMenu.text = "";
                newButton.SetEnabled(false);
                removeTreeButton.SetEnabled(false);
                graph.Show(null, null, null, null);
                return;
            }
            var note = EditorText.Help("graph.help");
            help.text = note ?? "";
            help.style.display = note == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (tree == null || !names.Contains(tree)) { tree = names.FirstOrDefault(); selectedPath = null; frameNext = true; }
            treeMenu.text = tree == null ? "" : TreeTitle(expression, tree);
            newButton.SetEnabled(true);
            removeTreeButton.SetEnabled(tree != null);
            treeMenu.menu.MenuItems().Clear();
            foreach (var n in names)
            {
                var name = n;
                treeMenu.menu.AppendAction(TreeTitle(expression, name), _ => SelectTree(name),
                    _ => name == tree ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            graph.Show(expression, tree, selectedPath, OpenPaths());
            if (frameNext)
            {
                frameNext = false;
                graph.schedule.Execute(() => graph.FrameAll()).ExecuteLater(50);
            }
        }

        static string TreeTitle(Newtonsoft.Json.Linq.JObject expression, string name)
        {
            var t = Tuning.Trees(expression).FirstOrDefault(x => x.Name == name);
            return t != null ? Tuning.TreeTitle(t) : Tuning.Label(name);
        }

        internal HashSet<string> OpenPaths() =>
            new HashSet<string>(openBoxes.Where(k => k.StartsWith(tree + "|", StringComparison.Ordinal)).Select(k => k.Substring(tree.Length + 1)));

        // Opens or closes a box (its face movements one per line); for the arrow in the box title and for checks.
        internal void SetOpen(string path, bool open)
        {
            var k = tree + "|" + path;
            if (open) openBoxes.Add(k); else openBoxes.Remove(k);
            Refresh();
        }

        internal void RemoveBox(string path, int keepRow)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            var kept = editor.RemoveBox(tree, path, keepRow);
            if (kept == null) return;
            openBoxes.RemoveWhere(k => k.StartsWith(tree + "|" + path, StringComparison.Ordinal));
            selectedPath = null;
            Refresh();
        }

        internal void OnBoxSelected(ExpressionGraph.Box box)
        {
            selectedPath = box.Path;
            TableEditorWindow.Current?.RevealInTuning(tree, box.TuningKey, box.Path);
        }

        // The search list after a line from a position is dropped on empty space.
        internal enum ListMode { Split, InsertAbove, NewTree }

        internal void OpenSplitList(string path, int row, Vector2 screenPosition) => OpenList(ListMode.Split, path, row, null, screenPosition);

        // The input list for splitting a position, inserting a box above a box, or the first box of a new expression.
        internal void OpenList(ListMode mode, string path, int row, string newName, Vector2 screenPosition, bool newSided = false)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            var provider = CreateInstance<SplitInputList>();
            provider.Window = this;
            provider.Mode = mode;
            provider.Path = path;
            provider.Row = row;
            provider.NewName = newName;
            provider.NewSided = newSided;
            provider.Inputs = ExpressionGraph.SplitInputs(editor.InputJson, editor.ExpressionJson, mode == ListMode.NewTree ? null : tree, mode == ListMode.NewTree && newSided);
            provider.Switches = SwitchEdit.Choices(editor.ExpressionJson, mode == ListMode.NewTree ? null : tree, mode == ListMode.NewTree && newSided);
            var (device, rows) = editor.DeviceInputs();
            if (device != null)
            {
                var sided = mode == ListMode.NewTree ? newSided : editor.ExpressionJson?["trees"]?[tree]?["sides"] != null;
                provider.DeviceName = device.Name;
                provider.DeviceInputs = InputChoices.SplitChoices(rows, sided);
            }
            SearchWindow.Open(new SearchWindowContext(screenPosition, provider.Width(), 200), provider);
        }

        internal void InsertAbove(string path, string inputX, string inputY)
        {
            var editor = TableEditorWindow.Current;
            var made = editor?.InsertBoxAbove(tree, path, inputX, inputY);
            if (made == null) return;
            selectedPath = made;
            Refresh();
            var box = ExpressionGraph.Build(editor.ExpressionJson, tree).FirstOrDefault(b => b.Path == made);
            if (box != null) editor.RevealInTuning(tree, box.TuningKey, box.Path);
        }

        // A switch box: an existing switch (reference), or a new one (null) made on the first input of the list.
        internal void ChooseSwitch(ListMode mode, string path, int row, string newName, bool newSided, string reference, string firstInput)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            var sided = mode == ListMode.NewTree ? newSided : editor.ExpressionJson?["trees"]?[tree]?["sides"] != null;
            var made = editor.EditExpression(mode == ListMode.NewTree ? "New FT expression" : mode == ListMode.InsertAbove ? "Insert FT box" : "Split FT position", e =>
            {
                var r = reference ?? SwitchEdit.NewSwitch(e, sided, firstInput);
                if (r == null) return null;
                return mode == ListMode.NewTree ? ExpressionGraph.NewTreeSwitch(e, newName, r, newSided)
                    : mode == ListMode.InsertAbove ? ExpressionGraph.InsertSwitchAbove(e, tree, path, r)
                    : ExpressionGraph.SplitSwitch(e, tree, path, row, r);
            });
            if (made == null) return;
            if (mode == ListMode.NewTree) { SelectTree(made); return; }
            selectedPath = made;
            Refresh();
            var box = ExpressionGraph.Build(editor.ExpressionJson, tree).FirstOrDefault(b => b.Path == made);
            if (box != null) editor.RevealInTuning(tree, box.TuningKey, box.Path);
        }

        // A choice from "Add from <device>": adds the device's input (and a second one, add2, for a 2D with both from the
        // device; once when both are sides of one pair) to the input table, then splits, inserts or makes the tree with
        // them as the other choices do, as one Undo step; a notification says what was added.
        internal void ChooseWithAdd(ListMode mode, string path, int row, string newName, bool newSided, string add, string display, string x, string y,
            string add2 = null, string display2 = null)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            if (!editor.AddDeviceInput(add)) return;
            if (add2 != null && add2 != add)
            {
                if (!editor.AddDeviceInput(add2)) { Undo.RevertAllDownToGroup(group); return; }
                display += ", " + display2;
            }
            if (mode == ListMode.InsertAbove) InsertAbove(path, x, y);
            else if (mode == ListMode.NewTree) CreateTree(newName, x, y, newSided);
            else Split(path, row, x, y);
            Undo.CollapseUndoOperations(group);
            var text = $"Added {display}";
            ShowNotification(new GUIContent(text), 4);
            Debug.Log("[FTG] graph add-from-device: " + text);
        }

        internal void CreateTree(string name, string inputX, string inputY, bool sided = false)
        {
            var made = TableEditorWindow.Current?.NewTree(name, inputX, inputY, sided);
            if (made != null) SelectTree(made);
        }

        void ShowNewTree(string name = "", bool sided = false)
        {
            if (TableEditorWindow.Current?.ExpressionJson == null) return;
            var r = newButton.worldBound;
            UnityEditor.PopupWindow.Show(new Rect(r.x, r.y, r.width, r.height), new NewTreePopup { Window = this, Name = name, Sided = sided });
        }

        internal void ShowNewTreeForCapture(string name, bool sided) => ShowNewTree(name, sided);

        internal void AskRemoveTree(string name)
        {
            if (name == null || TableEditorWindow.Current == null) return;
            if (!EditorUtility.DisplayDialog("FT Expression Graph", string.Format(EditorText.Get("graph.removeTree"), Tuning.Label(name)), "Remove", "Cancel")) return;
            if (TableEditorWindow.Current.RemoveTree(name)) { tree = null; selectedPath = null; frameNext = true; Refresh(); }
        }

        // The small panel under "+ New Expression": the name, then the list for the first box.
        internal sealed class NewTreePopup : PopupWindowContent
        {
            public ExpressionGraphWindow Window;
            internal string Name = "";
            internal bool Sided;
            bool focused;

            public override Vector2 GetWindowSize() => new Vector2(260, Sided ? 104 : 84);

            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(4);
                GUI.SetNextControlName("ftNewName");
                var labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 60;
                Name = EditorGUILayout.TextField("Name", Name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(63);  // under the field, past the 60 px label
                    Sided = EditorGUILayout.ToggleLeft("Left and right", Sided);
                }
                var pair = Sided ? EditorText.Help("graph.pairNote") : null;
                if (pair != null)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(63);
                        GUILayout.Label(string.Format(pair, Name.Trim()), EditorStyles.miniLabel);
                    }
                EditorGUIUtility.labelWidth = labelWidth;
                if (!focused) { EditorGUI.FocusTextInControl("ftNewName"); focused = true; }
                var note = EditorText.Help("graph.newNote");
                if (note != null) EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!ExpressionGraph.CanCreateTree(TableEditorWindow.Current?.ExpressionJson, Name, Sided)))
                        if (GUILayout.Button("Choose the First Box…", GUILayout.ExpandWidth(false)))
                        {
                            var at = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.yMax));
                            var name = Name;
                            editorWindow.Close();
                            Window.OpenList(ListMode.NewTree, null, -1, name, at, Sided);
                        }
                }
            }
        }

        // Splits with 1D (inputY null) or 2D.
        internal void Split(string path, int row, string input, string inputY = null)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            var made = inputY == null ? editor.SplitPosition(tree, path, row, input) : editor.SplitPosition2D(tree, path, row, input, inputY);
            if (made == null) return;
            selectedPath = made;
            Refresh();
            var box = ExpressionGraph.Build(editor.ExpressionJson, tree).FirstOrDefault(b => b.Path == made);
            if (box != null) editor.RevealInTuning(tree, box.TuningKey, box.Path);
        }

        sealed class SplitInputList : ScriptableObject, ISearchWindowProvider
        {
            public ExpressionGraphWindow Window;
            public ListMode Mode;
            public string Path, NewName;
            public bool NewSided;
            public int Row;
            public List<string> Inputs;
            public List<string> Switches = new List<string>();
            public string DeviceName;                                       // null: no "Add from <device>"
            public List<InputChoices.SplitChoice> DeviceInputs = new List<InputChoices.SplitChoice>();
            const string Add = "\u0002add", Info = "\u0003info";

            // A clear 1x1 icon for the rows: Unity's search list leaves room for an icon only when a row has one, so rows
            // without it start further left than the groups ("AC ComponentButton" and "AC GroupButton").
            static Texture2D indent;
            static Texture2D Indent
            {
                get
                {
                    if (indent != null) return indent;
                    indent = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    indent.SetPixel(0, 0, Color.clear);
                    indent.Apply();
                    return indent;
                }
            }
            static SearchTreeEntry Leaf(string text, int level, string[] data) => new SearchTreeEntry(new GUIContent(text, Indent)) { level = level, userData = data };

            // The width that shows every row and page title of the list without cutting it: at least the list's usual
            // 240 pt, at most the screen. Rows leave room for the icon on the left and the group arrow and scroll bar on
            // the right; a title leaves room for the back arrow on both sides.
            internal float Width()
            {
                const float Min = 240, Row = 60, Title = 60;
                var width = Min;
                foreach (var e in CreateSearchTree(default))
                {
                    var text = new GUIContent(e.name);
                    width = Mathf.Max(width, EditorStyles.label.CalcSize(text).x + Row);
                    if (e is SearchTreeGroupEntry) width = Mathf.Max(width, EditorStyles.boldLabel.CalcSize(text).x + Title);
                }
                var screen = Screen.currentResolution.width / EditorGUIUtility.pixelsPerPoint;
                return Mathf.Ceil(screen > Min ? Mathf.Min(width, screen) : width);
            }

            bool HasDevice => DeviceName != null && DeviceInputs.Count > 0;
            string AddTitle => "Add from " + DeviceName;

            // "Add from <device>" at level: one entry (or group, for an X of 2D) per device input.
            void AddFromDevice(List<SearchTreeEntry> list, int level, Func<InputChoices.SplitChoice, SearchTreeEntry> entry)
            {
                if (!HasDevice) return;
                list.Add(new SearchTreeGroupEntry(new GUIContent(AddTitle), level));
                foreach (var d in DeviceInputs) { var e = entry(d); if (e != null) list.Add(e); }
            }

            // Split > Split with 1D > <input>, and Split > Split with 2D: X > <X> > "<X> × <Y>".
            public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
            {
                var list = new List<SearchTreeEntry>
                {
                    new SearchTreeGroupEntry(new GUIContent(Mode == ListMode.InsertAbove ? "Insert Above" : Mode == ListMode.NewTree ? "First Box" : "Split"), 0),
                    new SearchTreeGroupEntry(new GUIContent("Split with 1D"), 1),
                };
                foreach (var name in Inputs)
                    list.Add(Leaf(ExpressionGraph.InputText(name), 2, new[] { name }));
                AddFromDevice(list, 2, d => Leaf(d.Display, 3, new[] { Add, d.Row.Name, d.Row.Display, d.Write, null }));
                var switchGroup = new SearchTreeGroupEntry(new GUIContent(Mode == ListMode.InsertAbove ? "Insert Switch" : Mode == ListMode.NewTree ? "Switch" : "Split with Switch"), 1);
                list.Add(new SearchTreeGroupEntry(new GUIContent("Split with 2D: X"), 1));
                foreach (var x in Inputs)
                {
                    list.Add(new SearchTreeGroupEntry(new GUIContent(ExpressionGraph.InputText(x)), 2));
                    foreach (var y in Inputs.Where(y => y != x))
                        list.Add(Leaf(ExpressionGraph.InputText(x) + " × " + ExpressionGraph.InputText(y), 3, new[] { x, y }));
                    // Y from the device
                    AddFromDevice(list, 3, d => Leaf(ExpressionGraph.InputText(x) + " × " + d.Display, 4, new[] { Add, d.Row.Name, d.Row.Display, x, d.Write }));
                }
                // X from the device: a group per device input, as for the table's X, with Y from the table and then Y
                // from the device (the other device inputs; both are added)
                var deviceY = DeviceInputs.Count > 1;
                if (HasDevice && (Inputs.Count > 0 || deviceY))
                {
                    list.Add(new SearchTreeGroupEntry(new GUIContent(AddTitle), 2));
                    foreach (var d in DeviceInputs)
                    {
                        list.Add(new SearchTreeGroupEntry(new GUIContent(d.Display), 3));
                        foreach (var y in Inputs)
                            list.Add(Leaf(d.Display + " × " + ExpressionGraph.InputText(y), 4, new[] { Add, d.Row.Name, d.Row.Display, d.Write, y }));
                        if (!deviceY) continue;
                        list.Add(new SearchTreeGroupEntry(new GUIContent(AddTitle), 4));
                        foreach (var e in DeviceInputs.Where(e => e.Write != d.Write))
                            list.Add(Leaf(d.Display + " × " + e.Display, 5, new[] { Add, d.Row.Name, d.Row.Display, d.Write, e.Write, e.Row.Name, e.Row.Display }));
                    }
                }
                list.Add(switchGroup);
                foreach (var sw in Switches)
                    list.Add(Leaf(ExpressionGraph.InputText(sw), 2, new[] { "\u0001switch", sw }));
                list.Add(Leaf("+ New Switch", 2, new[] { "\u0001switch", null }));
                return list;
            }

            public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
            {
                var names = (string[])entry.userData;
                if (names[0] == Info) return false;
                if (names[0] == Add) { Window.ChooseWithAdd(Mode, Path, Row, NewName, NewSided, names[1], names[2], names[3], names[4], names.Length > 6 ? names[5] : null, names.Length > 6 ? names[6] : null); return true; }
                if (names[0] == "\u0001switch") { Window.ChooseSwitch(Mode, Path, Row, NewName, NewSided, names[1], Inputs.FirstOrDefault()); return true; }
                var y = names.Length > 1 ? names[1] : null;
                if (Mode == ListMode.InsertAbove) Window.InsertAbove(Path, names[0], y);
                else if (Mode == ListMode.NewTree) Window.CreateTree(NewName, names[0], y, NewSided);
                else Window.Split(Path, Row, names[0], y);
                return true;
            }
        }

        // ---- the GraphView

        internal sealed class Graph : GraphView
        {
            readonly ExpressionGraphWindow window;
            internal readonly List<BoxNode> Boxes = new List<BoxNode>();
            bool showing;

            public Graph(ExpressionGraphWindow window)
            {
                this.window = window;
                SetupZoom(0.25f, 2f);
                this.AddManipulator(new ContentDragger());
                this.AddManipulator(new SelectionDragger());
                this.AddManipulator(new RectangleSelector());
                var grid = new GridBackground();
                Insert(0, grid);
                grid.StretchToParentSize();
                deleteSelection = (op, ask) => DeleteKey();
                serializeGraphElements = _ => null;
                canPasteSerializedData = _ => false;
            }

            // Only the boxes' own menu (Remove Box): no Cut, Copy, Paste, Delete or Duplicate.
            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt) { }

            internal void RemoveBox(BoxNode node, int keepRow) => window.RemoveBox(node.Box.Path, keepRow);
            internal void InsertAboveList(BoxNode node, Vector2 panelPosition) =>
                window.OpenList(ListMode.InsertAbove, node.Box.Path, -1, null, window.position.position + panelPosition);
            internal void AskRemoveTree() => window.AskRemoveTree(window.Tree);

            internal Newtonsoft.Json.Linq.JObject Expression => TableEditorWindow.Current?.ExpressionJson;
            internal string Tree => window.Tree;

            // The Delete key on one selected box: the same "Keep" choices as the context menu, below the box.
            void DeleteKey()
            {
                var node = selection.OfType<BoxNode>().FirstOrDefault();
                if (node == null || !ExpressionGraph.CanRemove(Expression, Tree, node.Box.Path)) return;
                var menu = new GenericMenu();
                for (int i = 0; i < node.Box.Rows.Count; i++)
                {
                    var row = i;
                    menu.AddItem(new GUIContent(node.KeepText(row)), false, () => RemoveBox(node, row));
                }
                var r = node.worldBound;
                menu.DropDown(new Rect(r.x, r.yMax, 0, 0));
            }

            // No port accepts a line: a line from a position always ends on empty space.
            public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) => new List<Port>();

            internal void Show(Newtonsoft.Json.Linq.JObject expression, string tree, string selectedPath, ICollection<string> open)
            {
                showing = true;
                foreach (var e in graphElements.ToList()) RemoveElement(e);
                Boxes.Clear();
                if (expression != null && tree != null)
                {
                    var boxes = ExpressionGraph.Build(expression, tree, open);
                    var byPath = new Dictionary<string, BoxNode>();
                    foreach (var b in boxes)
                    {
                        var n = new BoxNode(this, b);
                        AddElement(n);
                        Boxes.Add(n);
                        byPath[b.Path] = n;
                    }
                    foreach (var b in boxes.Where(b => b.ParentPath != null && byPath.ContainsKey(b.ParentPath)))
                    {
                        var edge = byPath[b.ParentPath].Outputs[b.ParentRow].ConnectTo(byPath[b.Path].Input);
                        edge.capabilities = 0;
                        edge.pickingMode = PickingMode.Ignore;
                        edge.edgeControl.pickingMode = PickingMode.Ignore;
                        AddElement(edge);
                    }
                    if (selectedPath != null && byPath.TryGetValue(selectedPath, out var sel)) AddToSelection(sel);
                }
                showing = false;
            }

            public override void AddToSelection(ISelectable selectable)
            {
                base.AddToSelection(selectable);
                if (!showing && selectable is BoxNode node) window.OnBoxSelected(node.Box);
            }

            internal void ToggleOpen(BoxNode node) => window.SetOpen(node.Box.Path, !node.Box.Open);

            internal void DroppedOutside(BoxNode node, int row, Vector2 panelPosition)
            {
                window.OpenSplitList(node.Box.Path, row, window.position.position + panelPosition);
            }
        }

        internal sealed class BoxNode : UnityEditor.Experimental.GraphView.Node
        {
            internal readonly ExpressionGraph.Box Box;
            internal readonly Port Input;
            internal readonly List<Port> Outputs = new List<Port>();
            readonly Graph graph;

            public BoxNode(Graph graph, ExpressionGraph.Box box)
            {
                this.graph = graph;
                Box = box;
                title = box.Title;
                capabilities = Capabilities.Selectable | Capabilities.Movable | Capabilities.Ascendable;
                style.width = ExpressionGraph.BoxWidth;
                titleButtonContainer.style.display = DisplayStyle.None;
                // The arrow that opens the box. It does not select the box.
                var arrow = new Label(box.Open ? "\u25BC" : "\u25B6") { tooltip = box.Open ? "Close" : "Open" };
                arrow.style.color = new Color(0.69f, 0.69f, 0.69f);
                arrow.style.fontSize = 10;
                arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
                arrow.style.width = 16;
                arrow.style.marginLeft = 4;
                arrow.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0) { graph.ToggleOpen(this); e.StopPropagation(); } });
                titleContainer.Insert(0, arrow);
                titleContainer.style.justifyContent = Justify.FlexStart;
                var titleLabel = titleContainer.Q<Label>("title-label");
                if (titleLabel != null) { titleLabel.style.flexGrow = 1; titleLabel.style.marginLeft = 2; titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft; }
                Input = RowPort.Make(Direction.Input, null);
                Input.portName = "";
                inputContainer.Add(Input);
                for (int i = 0; i < box.Rows.Count; i++)
                {
                    var r = box.Rows[i];
                    var row = i;
                    var port = RowPort.Make(Direction.Output, r.CanSplit ? new Listener(this, row) : null);
                    port.portName = r.Label;
                    port.tooltip = r.FullLabel;
                    outputContainer.Add(port);
                    Outputs.Add(port);
                    foreach (var line in r.Lines)
                    {
                        var l = new Label(line);
                        l.style.color = new Color(0.72f, 0.72f, 0.72f);
                        l.style.fontSize = 11;
                        l.style.unityTextAlign = TextAnchor.MiddleRight;
                        l.style.height = ExpressionGraph.LineHeight;
                        l.style.paddingRight = 24;
                        outputContainer.Add(l);
                    }
                }
                RefreshExpandedState();
                RefreshPorts();
                SetPosition(new Rect(box.X, box.Y, ExpressionGraph.BoxWidth, box.Height));
            }

            internal string KeepText(int row) =>
                "Keep " + (ExpressionGraph.KeepMenuText(graph.Expression, graph.Tree, Box.Path, row) ?? Box.Rows[row].Label).Replace("/", "\u2215");

            // Right-click: Remove Box > Keep <row>. The top box cannot be removed.
            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
            {
                evt.menu.AppendAction("Insert Box Above…", a => graph.InsertAboveList(this, a.eventInfo?.mousePosition ?? worldBound.center));
                var can = ExpressionGraph.CanRemove(graph.Expression, graph.Tree, Box.Path);
                if (!can)
                {
                    evt.menu.AppendAction("Remove Box", _ => { }, DropdownMenuAction.Status.Disabled);
                    evt.menu.AppendSeparator();
                    evt.menu.AppendAction($"Remove Expression \"{Tuning.Label(graph.Tree)}\"…", _ => graph.AskRemoveTree());
                    return;
                }
                for (int i = 0; i < Box.Rows.Count; i++)
                {
                    var row = i;
                    evt.menu.AppendAction("Remove Box/" + KeepText(row), _ => graph.RemoveBox(this, row));
                }
            }

            sealed class Listener : IEdgeConnectorListener
            {
                readonly BoxNode node;
                readonly int row;
                public Listener(BoxNode node, int row) { this.node = node; this.row = row; }
                public void OnDropOutsidePort(Edge edge, Vector2 position) => node.graph.DroppedOutside(node, row, position);
                public void OnDrop(GraphView graphView, Edge edge) { }
            }
        }

        // A port that starts a line only when it has a listener (a position that can be split).
        sealed class RowPort : Port
        {
            RowPort(Direction direction) : base(Orientation.Horizontal, direction, Capacity.Single, typeof(float)) { }

            internal static Port Make(Direction direction, IEdgeConnectorListener listener)
            {
                var p = new RowPort(direction);
                if (listener != null)
                {
                    p.m_EdgeConnector = new EdgeConnector<Edge>(listener);
                    p.AddManipulator(p.m_EdgeConnector);
                }
                return p;
            }
        }
    }
}
