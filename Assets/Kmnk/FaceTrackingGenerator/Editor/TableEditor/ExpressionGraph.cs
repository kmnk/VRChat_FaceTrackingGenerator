using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The expression graph without drawing: the nodes of a tree that branch (1D, 2D, switch) as boxes laid out left to
    // right, a row per position, and splitting a position with a new 1D node. Works on the JSON trees, so a left/right
    // pair stays one tree. A box is found by its path: the row indices from the top box ("root", "root/0", "root/0/1").
    public static class ExpressionGraph
    {
        public const float ColumnWidth = 370, BoxWidth = 320, TitleHeight = 44, RowHeight = 24, LineHeight = 16, Gap = 20;
        const int PoseTextLength = 26;

        public sealed class Row
        {
            public string Label;      // "0.3", "(0.7, 0)" or a state name, with ":  <face movements>" for a pose
            public bool Nested;       // the row's content is a box (drawn as a line to it)
            public bool CanSplit;     // the row holds face movements and can become a 1D node
            public string FullLabel;  // the label without cutting (for the tooltip)
            public List<string> Lines = new List<string>();  // one face movement per line, for an open box ("mouth.smile  100")
        }

        public sealed class Box
        {
            public string Path;
            public string Kind;       // "1d", "2d" or "switch"
            public string Title;
            public List<Row> Rows = new List<Row>();
            public string ParentPath; // null for the top box
            public int ParentRow;
            public int Depth;
            public float X, Y, Height;
            public string TuningKey;  // the fold key of the Tuning tab ("<tree>/<i>", "<tree>/2d/<k>"), or null for a switch
            public bool Open;         // shows the face movements one per line
        }

        public static List<string> TreeNames(JObject expression) =>
            expression?["trees"] is JObject trees ? trees.Properties().Where(p => p.Value is JObject).Select(p => p.Name).ToList() : new List<string>();

        static bool IsBranch(JToken t) => t is JObject o && (o["1d"] != null || o["2d"] != null || o["switch"] != null);

        // The rows of a branching node: (label of the position, content).
        static List<(string label, JToken child)> Children(JObject o)
        {
            var rows = new List<(string, JToken)>();
            if (o["1d"] != null || o["2d"] != null)
            {
                if (o["children"] is JArray ch)
                    foreach (var c in ch.OfType<JArray>().Where(c => c.Count == 2))
                        rows.Add((c[0] is JArray p ? $"({Num(p[0])}, {Num(p[1])})" : Num(c[0]), c[1]));
            }
            else if (o["cases"] is JObject cases)
                foreach (var p in cases.Properties()) rows.Add((p.Name, p.Value));
            return rows;
        }

        static string Num(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float
            ? t.Value<double>().ToString("0.##", CultureInfo.InvariantCulture) : t.ToString();

        // "mouth.sad 100, eye.sad 100" cut to 26 characters, or "Neutral".
        public static string PoseText(JToken child, bool cut = true)
        {
            var pose = Tuning.Pose(child);
            if (pose == null || pose.Count == 0) return "Neutral";
            var s = string.Join(", ", pose.Select(kv => $"{Tuning.Label(kv.Key)} {Math.Round(kv.Value).ToString(CultureInfo.InvariantCulture)}"));
            return cut && s.Length > PoseTextLength ? s.Substring(0, PoseTextLength - 1) + "…" : s;
        }

        // The face movements of a pose, one per line ("Neutral" for an empty one).
        public static List<string> PoseLines(JToken child)
        {
            var pose = Tuning.Pose(child);
            if (pose == null || pose.Count == 0) return new List<string> { "Neutral" };
            return pose.Select(kv => $"{Tuning.Label(kv.Key)}  {Math.Round(kv.Value).ToString(CultureInfo.InvariantCulture)}").ToList();
        }

        static string Title(JObject o)
        {
            if (o["1d"] != null) return "1D  " + Tuning.Label((string)o["1d"]);
            if (o["2d"] is JArray a) return $"2D  {Tuning.Label((string)a[0])} × {Tuning.Label((string)a[1])}";
            return "Switch  " + Tuning.Label((string)o["switch"]);
        }

        public static JObject TreeRoot(JObject expression, string tree) => expression?["trees"]?[tree]?["tree"] as JObject;

        // The boxes of a tree in the order of a depth-first walk, laid out left to right.
        public static List<Box> Build(JObject expression, string tree, ICollection<string> open = null)
        {
            var boxes = new List<Box>();
            var root = TreeRoot(expression, tree);
            if (!IsBranch(root)) return boxes;
            var keys = TuningKeys(expression, tree);
            float nextY = 0;
            Place(root, "root", null, 0, 0, boxes, keys, open, ref nextY);
            boxes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            boxes[0].Title = Tuning.Label(tree) + " · " + boxes[0].Title;
            return boxes;
        }

        // A parent sits at the height of its first child; boxes without children stack from the top.
        static void Place(JObject o, string path, string parentPath, int parentRow, int depth, List<Box> boxes, Dictionary<JObject, string> keys, ICollection<string> open, ref float nextY)
        {
            var box = new Box
            {
                Path = path, Kind = o["1d"] != null ? "1d" : o["2d"] != null ? "2d" : "switch", Title = Title(o),
                ParentPath = parentPath, ParentRow = parentRow, Depth = depth, X = depth * ColumnWidth,
                TuningKey = keys.TryGetValue(o, out var k) ? k : null,
                Open = open != null && open.Contains(path),
            };
            var startY = nextY;
            var rows = Children(o);
            var hasChild = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var (label, child) = rows[i];
                if (IsBranch(child))
                {
                    box.Rows.Add(new Row { Label = label, FullLabel = label, Nested = true });
                    Place((JObject)child, path + "/" + i, path, i, depth + 1, boxes, keys, open, ref nextY);
                    hasChild = true;
                }
                else box.Rows.Add(new Row
                {
                    Label = box.Open ? label : label + ":  " + PoseText(child), FullLabel = label + ":  " + PoseText(child, false),
                    CanSplit = true, Lines = box.Open ? PoseLines(child) : new List<string>(),
                });
            }
            box.Height = TitleHeight + rows.Count * RowHeight + box.Rows.Sum(r => r.Lines.Count) * LineHeight;
            box.Y = startY;
            nextY = hasChild ? Math.Max(nextY, startY + box.Height + Gap) : nextY + box.Height + Gap;
            boxes.Add(box);
        }

        // The Tuning tab's fold keys of the 1D and 2D nodes, matched by object (the tab finds them with Tuning.Trees).
        static Dictionary<JObject, string> TuningKeys(JObject expression, string tree)
        {
            var keys = new Dictionary<JObject, string>(JTokenEqualityComparerByReference.Instance);
            var t = Tuning.Trees(expression).FirstOrDefault(x => x.Name == tree);
            if (t == null) return keys;
            for (int i = 0; i < t.Nodes.Count; i++) keys[t.Nodes[i].Node] = $"{tree}/{i}";
            for (int k = 0; k < t.Nodes2D.Count; k++) keys[t.Nodes2D[k].Node] = $"{tree}/2d/{k}";
            return keys;
        }

        sealed class JTokenEqualityComparerByReference : IEqualityComparer<JObject>
        {
            public static readonly JTokenEqualityComparerByReference Instance = new JTokenEqualityComparerByReference();
            public bool Equals(JObject a, JObject b) => ReferenceEquals(a, b);
            public int GetHashCode(JObject o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        // The node at a box path, or null.
        public static JObject Find(JObject expression, string tree, string path)
        {
            JToken node = TreeRoot(expression, tree);
            if (path == null || !path.StartsWith("root")) return null;
            foreach (var part in path.Split('/').Skip(1))
            {
                if (!(node is JObject o) || !int.TryParse(part, out var i)) return null;
                var rows = Children(o);
                if (i < 0 || i >= rows.Count) return null;
                node = rows[i].child;
            }
            return IsBranch(node) ? (JObject)node : null;
        }

        // The inputs a position of the tree can be split with: the input table's names as written, sorted. A tree
        // without sides cannot use a name with a side mark ("EyeLid{side}").
        public static List<string> SplitInputs(JObject input, JObject expression, string tree)
        {
            var hasSides = expression?["trees"]?[tree]?["sides"] != null;
            var names = input?["inputs"] is JObject o ? o.Properties().Select(p => p.Name)
                : input?["inputs"] is JArray a ? a.OfType<JObject>().Select(x => (string)x["name"]).Where(x => x != null) : Enumerable.Empty<string>();
            return names.Where(n => hasSides || !n.Contains("{")).Distinct().OrderBy(n => Tuning.Label(n), StringComparer.Ordinal).ToList();
        }

        public static bool CanSplit(JObject expression, string tree, string path, int row)
        {
            var node = Find(expression, tree, path);
            if (node == null) return false;
            var rows = Children(node);
            return row >= 0 && row < rows.Count && !IsBranch(rows[row].child) && Tuning.Pose(rows[row].child) != null;
        }

        // Makes the row's face movements a 1D node of the input with the same face movements at both ends of the
        // input's range, so the face does not change. Returns the new box's path, or null when the row cannot be split.
        public static string Split(JObject expression, string tree, string path, int row, string input)
        {
            if (!CanSplit(expression, tree, path, row) || string.IsNullOrEmpty(input)) return null;
            var node = Find(expression, tree, path);
            var pose = Children(node)[row].child;
            var copy = pose == null || pose.Type == JTokenType.Null ? new JObject() : (JObject)pose.DeepClone();
            var (lo, hi) = Tuning.Range(input, Enumerable.Empty<float>());
            var nested = new JObject
            {
                ["1d"] = input,
                ["children"] = new JArray(new JArray(Tuning.Num(lo), copy), new JArray(Tuning.Num(hi), copy.DeepClone())),
            };
            SetRow(node, row, nested);
            return path + "/" + row;
        }

        static void SetRow(JObject node, int row, JToken content)
        {
            if (node["cases"] is JObject cases) cases.Properties().ElementAt(row).Value = content;
            else ((JArray)((JArray)node["children"]).OfType<JArray>().Where(c => c.Count == 2).ElementAt(row))[1] = content;
        }

        // Makes the row's face movements a 2D node of the two inputs with the same face movements at three positions
        // (the origin, the X end and the Y end), so the face does not change. The origin is 0 when the input's range
        // has 0, its low end otherwise. Returns the new box's path, or null.
        public static string Split2D(JObject expression, string tree, string path, int row, string inputX, string inputY)
        {
            if (!CanSplit(expression, tree, path, row) || string.IsNullOrEmpty(inputX) || string.IsNullOrEmpty(inputY) || inputX == inputY) return null;
            var node = Find(expression, tree, path);
            var pose = Children(node)[row].child;
            var copy = pose == null || pose.Type == JTokenType.Null ? new JObject() : (JObject)pose.DeepClone();
            var (xlo, xhi) = Tuning.Range(inputX, Enumerable.Empty<float>());
            var (ylo, yhi) = Tuning.Range(inputY, Enumerable.Empty<float>());
            float x0 = xlo <= 0 && 0 <= xhi ? 0 : xlo, y0 = ylo <= 0 && 0 <= yhi ? 0 : ylo;
            JArray P(float x, float y) => new JArray(Tuning.Num(x), Tuning.Num(y));
            var nested = new JObject
            {
                ["2d"] = new JArray(inputX, inputY),
                ["children"] = new JArray(new JArray(P(x0, y0), copy), new JArray(P(xhi, y0), copy.DeepClone()), new JArray(P(x0, yhi), copy.DeepClone())),
            };
            SetRow(node, row, nested);
            return path + "/" + row;
        }

        // A box other than the top one can be removed.
        public static bool CanRemove(JObject expression, string tree, string path) =>
            path != null && path != "root" && Find(expression, tree, path) != null;

        // Replaces the box with the content of one of its rows (face movements, or a box that moves up). Returns the path
        // where the content is now, or null.
        public static string Remove(JObject expression, string tree, string path, int keepRow)
        {
            if (!CanRemove(expression, tree, path)) return null;
            var node = Find(expression, tree, path);
            var rows = Children(node);
            if (keepRow < 0 || keepRow >= rows.Count) return null;
            var cut = path.LastIndexOf('/');
            var parent = Find(expression, tree, path.Substring(0, cut));
            if (parent == null || !int.TryParse(path.Substring(cut + 1), out var parentRow)) return null;
            var keep = rows[keepRow].child;
            SetRow(parent, parentRow, keep == null || keep.Type == JTokenType.Null ? new JObject() : keep.DeepClone());
            return path;
        }

        // The row's text for a menu: the label as the closed box shows it, or the title of the box it holds.
        public static string RowMenuText(JObject expression, string tree, string path, int row)
        {
            var node = Find(expression, tree, path);
            if (node == null) return null;
            var (label, child) = Children(node)[row];
            return IsBranch(child) ? label + ":  " + Title((JObject)child) : label + ":  " + PoseText(child);
        }
    }
}
