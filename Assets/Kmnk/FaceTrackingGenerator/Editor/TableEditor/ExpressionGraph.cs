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
        // For a left/right tree (sided, or a tree written with "sides"): the inputs written with "{side}", the pairs of
        // inputs written per side ("EyeLidLeft" and "EyeLidRight" give "EyeLid{side}"), and the inputs without sides.
        // One side alone is left out (both sides would move with it). For other trees: the inputs without "{".
        public static List<string> SplitInputs(JObject input, JObject expression, string tree, bool sided = false)
        {
            var hasSides = sided || (tree != null && expression?["trees"]?[tree]?["sides"] != null);
            var names = (input?["inputs"] is JObject o ? o.Properties().Select(p => p.Name)
                : input?["inputs"] is JArray a ? a.OfType<JObject>().Select(x => (string)x["name"]).Where(x => x != null) : Enumerable.Empty<string>()).ToList();
            List<string> result;
            if (!hasSides) result = names.Where(n => !n.Contains("{")).ToList();
            else
            {
                var set = new HashSet<string>(names);
                result = new List<string>();
                foreach (var n in names)
                {
                    if (n.Contains("{")) { result.Add(n); continue; }
                    if (n.Contains("Left") && set.Contains(n.Replace("Left", "Right"))) { result.Add(n.Replace("Left", "{side}")); continue; }
                    if (n.Contains("Right") && set.Contains(n.Replace("Right", "Left"))) continue;
                    if (n.Contains("Left") || n.Contains("Right")) continue;  // one side alone
                    result.Add(n);
                }
            }
            return result.Distinct().OrderBy(n => Tuning.Label(n), StringComparer.Ordinal).ToList();
        }

        // How an input shows in the lists: "EyeLid (left and right)" for "EyeLid{side}".
        public static string InputText(string name) => Tuning.Label(name) + (name.Contains("{side}") ? " (left and right)" : "");

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

        static (float lo, float origin, float hi) Ends(string input)
        {
            var (lo, hi) = Tuning.Range(input, Enumerable.Empty<float>());
            return (lo, lo <= 0 && 0 <= hi ? 0 : lo, hi);
        }

        // A new 1D (inputY null) or 2D node: "first" at the origin, the face movements "rest" at the other positions
        // (1D: the high end; 2D: the X end and the Y end). With origin false, a 1D node goes from the low end instead.
        static JObject NewNode(string inputX, string inputY, JToken first, JToken rest, bool origin = true)
        {
            var x = Ends(inputX);
            if (inputY == null)
                return new JObject
                {
                    ["1d"] = inputX,
                    ["children"] = new JArray(new JArray(Tuning.Num(origin ? x.origin : x.lo), first), new JArray(Tuning.Num(x.hi), rest)),
                };
            var y = Ends(inputY);
            JArray P(float a, float b) => new JArray(Tuning.Num(a), Tuning.Num(b));
            return new JObject
            {
                ["2d"] = new JArray(inputX, inputY),
                ["children"] = new JArray(new JArray(P(x.origin, y.origin), first), new JArray(P(x.hi, y.origin), rest), new JArray(P(x.origin, y.hi), rest.DeepClone())),
            };
        }

        // Puts a new 1D or 2D box above the box: the box goes to the new box's origin, the other positions are neutral.
        // Works on the top box too. Returns the new box's path (the old box's path), or null.
        public static string InsertAbove(JObject expression, string tree, string path, string inputX, string inputY = null)
        {
            var node = Find(expression, tree, path);
            if (node == null || string.IsNullOrEmpty(inputX) || inputX == inputY) return null;
            var above = NewNode(inputX, inputY, node.DeepClone(), new JObject());
            if (path == "root") ((JObject)expression["trees"][tree])["tree"] = above;
            else
            {
                var cut = path.LastIndexOf('/');
                var parent = Find(expression, tree, path.Substring(0, cut));
                if (parent == null || !int.TryParse(path.Substring(cut + 1), out var parentRow)) return null;
                SetRow(parent, parentRow, above);
            }
            return path;
        }

        // The names a new expression would take (a left/right one: "<name>{side}", "<name>Left", "<name>Right") must not
        // be the name of an expression, as written or after the sides.
        public static bool CanCreateTree(JObject expression, string name, bool sided = false)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("{") || name.Contains("}")) return false;
            name = name.Trim();
            var taken = new HashSet<string>(TreeNames(expression));
            foreach (var t in Tuning.Trees(expression)) foreach (var n in Tuning.Expanded(t, t.Name)) taken.Add(n);
            var mine = sided ? new[] { name + "{side}", name + "Left", name + "Right" } : new[] { name };
            return mine.All(n => !taken.Contains(n));
        }

        // A new expression whose first box has neutral face movements at every position; a left/right one is written once
        // with "sides" (Left/Right, {s} = L/R) like the template's Eyelid. Returns the name as written, or null.
        public static string NewTree(JObject expression, string name, string inputX, string inputY = null, bool sided = false)
        {
            if (!CanCreateTree(expression, name, sided) || string.IsNullOrEmpty(inputX) || inputX == inputY) return null;
            if (!(expression["trees"] is JObject trees)) expression["trees"] = trees = new JObject();
            var key = name.Trim() + (sided ? "{side}" : "");
            var entry = new JObject();
            if (sided) entry["sides"] = new JObject { ["Left"] = new JObject { ["s"] = "L" }, ["Right"] = new JObject { ["s"] = "R" } };
            entry["tree"] = NewNode(inputX, inputY, new JObject(), new JObject(), origin: false);
            trees.Add(key, entry);
            return key;
        }

        public static bool RemoveTree(JObject expression, string name) =>
            expression?["trees"] is JObject trees && trees.Remove(name);

        static string NodeLabel(JObject o)
        {
            if (o["1d"] != null) return Tuning.Label((string)o["1d"]);
            if (o["2d"] is JArray a) return $"{Tuning.Label((string)a[0])} × {Tuning.Label((string)a[1])}";
            return Tuning.Label((string)o["switch"]);
        }

        // Where a box is: "Mouth › LipPucker 0 › MouthOpen" (the tree, each box above with the position on the way,
        // then the box's input). Null when there is no box at the path.
        public static string PathText(JObject expression, string tree, string path)
        {
            var node = TreeRoot(expression, tree);
            if (!IsBranch(node) || path == null || !path.StartsWith("root") || Find(expression, tree, path) == null) return null;
            var parts = new List<string> { Tuning.Label(tree) };
            foreach (var seg in path.Split('/').Skip(1))
            {
                var rows = Children(node);
                var i = int.Parse(seg);
                parts.Add(NodeLabel(node) + " " + rows[i].label);
                node = (JObject)rows[i].child;
            }
            parts.Add(NodeLabel(node));
            return string.Join(" \u203A ", parts);
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
