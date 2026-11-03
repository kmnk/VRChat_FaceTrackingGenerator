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
        Label help;
        Graph graph;

        [MenuItem("Tools/Kmnk/Face Tracking Expression Graph")]
        public static ExpressionGraphWindow Open() => GetWindow<ExpressionGraphWindow>("FT Expression Graph");

        void OnEnable()
        {
            titleContent = new GUIContent("FT Expression Graph");
            var toolbar = new Toolbar();
            treeMenu = new ToolbarMenu { text = "" };
            treeMenu.style.minWidth = 120;
            toolbar.Add(treeMenu);
            toolbar.Add(new ToolbarButton(() => graph.FrameAll()) { text = "Frame All" });
            rootVisualElement.Add(toolbar);
            help = new Label();
            help.style.color = new Color(0.6f, 0.6f, 0.6f);
            help.style.fontSize = 11;
            help.style.paddingLeft = 6; help.style.paddingTop = 3; help.style.paddingBottom = 3;
            help.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(help);
            graph = new Graph(this);
            graph.style.flexGrow = 1;
            rootVisualElement.Add(graph);
            TableEditorWindow.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => TableEditorWindow.Changed -= Refresh;

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
            var editor = TableEditorWindow.Current;
            var expression = editor?.ExpressionJson;
            var names = ExpressionGraph.TreeNames(expression);
            if (expression == null)
            {
                help.text = EditorText.Get("graph.noTable");
                help.style.display = DisplayStyle.Flex;
                treeMenu.text = "";
                graph.Show(null, null, null, null);
                return;
            }
            var note = EditorText.Help("graph.help");
            help.text = note ?? "";
            help.style.display = note == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (tree == null || !names.Contains(tree)) { tree = names.FirstOrDefault(); selectedPath = null; frameNext = true; }
            treeMenu.text = tree == null ? "" : TreeTitle(expression, tree);
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
            TableEditorWindow.Current?.RevealInTuning(tree, box.TuningKey);
        }

        // The search list after a line from a position is dropped on empty space.
        internal void OpenSplitList(string path, int row, Vector2 screenPosition)
        {
            var editor = TableEditorWindow.Current;
            if (editor == null) return;
            var provider = CreateInstance<SplitInputList>();
            provider.Window = this;
            provider.Path = path;
            provider.Row = row;
            provider.Inputs = ExpressionGraph.SplitInputs(editor.InputJson, editor.ExpressionJson, tree);
            SearchWindow.Open(new SearchWindowContext(screenPosition, 240, 200), provider);
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
            if (box != null) editor.RevealInTuning(tree, box.TuningKey);
        }

        sealed class SplitInputList : ScriptableObject, ISearchWindowProvider
        {
            public ExpressionGraphWindow Window;
            public string Path;
            public int Row;
            public List<string> Inputs;

            // Split > Split with 1D > <input>, and Split > Split with 2D: X > <X> > "<X> × <Y>".
            public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
            {
                var list = new List<SearchTreeEntry>
                {
                    new SearchTreeGroupEntry(new GUIContent("Split"), 0),
                    new SearchTreeGroupEntry(new GUIContent("Split with 1D"), 1),
                };
                foreach (var name in Inputs)
                    list.Add(new SearchTreeEntry(new GUIContent(Tuning.Label(name))) { level = 2, userData = new[] { name } });
                list.Add(new SearchTreeGroupEntry(new GUIContent("Split with 2D: X"), 1));
                foreach (var x in Inputs)
                {
                    list.Add(new SearchTreeGroupEntry(new GUIContent(Tuning.Label(x)), 2));
                    foreach (var y in Inputs.Where(y => y != x))
                        list.Add(new SearchTreeEntry(new GUIContent(Tuning.Label(x) + " × " + Tuning.Label(y))) { level = 3, userData = new[] { x, y } });
                }
                return list;
            }

            public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
            {
                var names = (string[])entry.userData;
                Window.Split(Path, Row, names[0], names.Length > 1 ? names[1] : null);
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
                "Keep " + (ExpressionGraph.RowMenuText(graph.Expression, graph.Tree, Box.Path, row) ?? Box.Rows[row].Label).Replace("/", "\u2215");

            // Right-click: Remove Box > Keep <row>. The top box cannot be removed.
            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
            {
                var can = ExpressionGraph.CanRemove(graph.Expression, graph.Tree, Box.Path);
                if (!can) { evt.menu.AppendAction("Remove Box", _ => { }, DropdownMenuAction.Status.Disabled); return; }
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
