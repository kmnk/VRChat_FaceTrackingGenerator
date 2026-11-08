using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The Tuning tab without drawing: the 1D and 2D nodes of the expression table (positions where face movements change),
    // the switch nodes with face movements in their states,
    // and the smoothing times of the input table. Works on the JSON trees, so a left/right pair stays one node.
    public static class Tuning
    {
        public sealed class Blend1D
        {
            public JObject Node;                                   // {"1d": input, "children": [[position, pose or node], ...]}
            public string Input;                                   // as written, e.g. "EyeLid{side}"
            public List<(string sw, string state)> When = new List<(string, string)>();  // switches above it, as written
            public JArray Children => (JArray)Node[ExpressionSyntax.KeyChildren];
            public float Position(int i) => ((JArray)Children[i])[0].Value<float>();
            public JToken Child(int i) => ((JArray)Children[i])[1];
        }

        public sealed class Blend2D
        {
            public JObject Node;                                   // {"2d": [inputX, inputY], "children": [[[x, y], pose or node], ...]}
            public string InputX, InputY;
            public List<(string sw, string state)> When = new List<(string, string)>();
            public JArray Children => (JArray)Node[ExpressionSyntax.KeyChildren];
            public (float x, float y) Position(int i) { var p = (JArray)((JArray)Children[i])[0]; return (p[0].Value<float>(), p[1].Value<float>()); }
            public JToken Child(int i) => ((JArray)Children[i])[1];
        }

        // A switch node with a pose (face movements that stay the same whatever the values) in at least one of its states.
        public sealed class SwitchCases
        {
            public JObject Node;                                   // {"switch": name, "cases": {state: pose or node, ...}}
            public string Switch;                                  // as written, e.g. "EyelidSmile{side}"
            public List<(string sw, string state)> When = new List<(string, string)>();  // switches above it, as written
            public List<string> States = new List<string>();      // the switch's order; the order of "cases" when it is not found
            public JToken Case(string state) => Node[ExpressionSyntax.KeyCases]?[state];
        }

        public sealed class Tree
        {
            public string Name;                                    // as written, e.g. "Eyelid{side}"
            public List<Dictionary<string, string>> Sides;         // the side variables, or null for a tree without sides
            public List<Blend1D> Nodes = new List<Blend1D>();
            public List<Blend2D> Nodes2D = new List<Blend2D>();
            public List<SwitchCases> Switches = new List<SwitchCases>();
            public bool Has2D => Nodes2D.Count > 0;
        }

        // ---- finding

        public static List<Tree> Trees(JObject expression)
        {
            var result = new List<Tree>();
            if (!(expression?["trees"] is JObject trees)) return result;
            foreach (var p in trees.Properties())
            {
                if (!(p.Value is JObject entry)) continue;
                var t = new Tree { Name = p.Name, Sides = SidesOf(entry["sides"]) };
                foreach (var (o, when) in ExpressionSyntax.Walk(entry["tree"])) Add(o, when, t, expression);
                result.Add(t);
            }
            return result;
        }

        static void Add(JObject o, List<(string, string)> when, Tree t, JObject expression)
        {
            switch (ExpressionSyntax.KindOf(o))
            {
                case ExpressionSyntax.Kind.Blend1D when o[ExpressionSyntax.KeyChildren] is JArray:
                    t.Nodes.Add(new Blend1D { Node = o, Input = (string)o[ExpressionSyntax.Key1D], When = when.ToList() });
                    break;
                case ExpressionSyntax.Kind.Blend2D when o[ExpressionSyntax.KeyChildren] is JArray:
                    var axes = o[ExpressionSyntax.Key2D] as JArray;
                    t.Nodes2D.Add(new Blend2D { Node = o, InputX = (string)axes?[0], InputY = (string)axes?[1], When = when.ToList() });
                    break;
                case ExpressionSyntax.Kind.Switch when o[ExpressionSyntax.KeyCases] is JObject cases:
                    var sw = (string)o[ExpressionSyntax.KeySwitch];
                    var states = SwitchEdit.States(expression, sw);
                    if (states.Count == 0) states = cases.Properties().Select(c => c.Name).ToList();
                    // A state without a case is an empty pose (TableLoader), so it counts as a pose here.
                    if (states.Any(st => ExpressionSyntax.Pose(cases[st]) != null))
                        t.Switches.Add(new SwitchCases { Node = o, Switch = sw, When = when.ToList(), States = states });
                    break;
            }
        }

        // The same side variables as TableLoader.Expand: ["Left", "Right"] or {"Left": {"s": "L"}, ...}.
        internal static List<Dictionary<string, string>> SidesOf(JToken sides)
        {
            if (sides is JArray a) return a.Select(s => new Dictionary<string, string> { ["side"] = (string)s }).ToList();
            if (sides is JObject o)
                return o.Properties().Select(sp =>
                {
                    var vars = new Dictionary<string, string> { ["side"] = sp.Name };
                    if (sp.Value is JObject vo) foreach (var vp in vo.Properties()) vars[vp.Name] = (string)vp.Value;
                    return vars;
                }).ToList();
            return null;
        }

        // A number for the file: whole numbers as integers ("1", not "1.0"), others as they are.
        public static JToken Num(double v) => Math.Abs(v - Math.Round(v)) < 1e-9 ? (JToken)(long)Math.Round(v) : new JValue(v);

        public static string Sub(string s, Dictionary<string, string> vars)
        {
            if (vars != null) foreach (var kv in vars) s = s.Replace("{" + kv.Key + "}", kv.Value);
            return s;
        }

        // The names after expanding the sides (one name for a tree without sides).
        public static List<string> Expanded(Tree t, string name) =>
            t.Sides == null ? new List<string> { name } : t.Sides.Select(v => Sub(name, v)).Distinct().ToList();

        static readonly Regex Marks = new Regex(@"_?\{[^}]*\}");

        // A name without the side marks: "EyeLid{side}" -> "EyeLid", "eye.close_{s}" -> "eye.close".
        public static string Label(string name) => Marks.Replace(name ?? "", "");

        // ---- texts

        public static string TreeTitle(Tree t)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            return Label(t.Name) + (t.Sides == null ? "" : ja ? "（左右）" : " (left and right)");
        }

        public static string NodeTitle(Blend1D n)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var s = ja ? $"{Label(n.Input)} で動く" : $"Moves with {Label(n.Input)}";
            if (n.When.Count == 0) return s;
            var conds = n.When.Select(w => ja ? $"{Label(w.sw)} が {w.state}" : $"{Label(w.sw)} is {w.state}");
            return s + (ja ? "（" + string.Join("、", conds) + " のとき）" : " (when " + string.Join(", ", conds) + ")");
        }

        public static string SwitchTitle(SwitchCases n)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var name = Label(n.Switch) + (n.Switch.Contains("{side}") ? ja ? "（左右）" : " (left and right)" : "");
            var s = ja ? $"{name} で切り替わる" : $"Changes with {name}";
            if (n.When.Count == 0) return s;
            var conds = n.When.Select(w => ja ? $"{Label(w.sw)} が {w.state}" : $"{Label(w.sw)} is {w.state}");
            return s + (ja ? "（" + string.Join("、", conds) + " のとき）" : " (when " + string.Join(", ", conds) + ")");
        }

        // The face movements at a position: "eye.close 100", "普通の顔" or "別の表情の組み合わせ".
        public static string ChildText(JToken child)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var pose = ExpressionSyntax.Pose(child);
            if (pose == null) return ja ? "別の表情の組み合わせ" : "Nested expressions";
            if (pose.Count == 0) return ja ? "普通の顔" : "Neutral";
            return string.Join(ja ? "、" : ", ", pose.Select(kv => $"{Label(kv.Key)} {Math.Round(kv.Value)}"));
        }

        public static string Uses(IEnumerable<string> inputs)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var list = inputs.ToList();
            if (list.Count == 0) return ja ? "使っている入力はありません" : "No input uses it";
            var shown = list.Take(4);
            var more = list.Count - 4;
            if (ja) return string.Join("・", shown) + (more > 0 ? $" ほか {more} つ" : "") + "が使う";
            return "Used by " + string.Join(", ", shown) + (more > 0 ? $" and {more} more" : "");
        }

        // ---- positions

        // The range of an input's values (the same rule as the device lists), widened to the given positions.
        public static (float lo, float hi) Range(string input, IEnumerable<float> positions)
        {
            var name = Label(input);
            var signed = name.EndsWith("X") || name.EndsWith("Y") || name == "SmileSad" || name == "SmileFrown" || name.StartsWith("CheekPuffSuck");
            float lo = signed ? -1 : 0, hi = 1;
            foreach (var p in positions) { lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
            return (lo, hi);
        }

        public static (float lo, float hi) InputRange(Blend1D n) => Range(n.Input, Enumerable.Range(0, n.Children.Count).Select(n.Position));

        public static ((float lo, float hi) x, (float lo, float hi) y) InputRange(Blend2D n)
        {
            var pts = Enumerable.Range(0, n.Children.Count).Select(n.Position).ToList();
            return (Range(n.InputX, pts.Select(p => p.x)), Range(n.InputY, pts.Select(p => p.y)));
        }

        // ---- 2D positions

        // The amount of each face movement at a point of a 2D node (pose children only).
        public static Dictionary<string, float> Amounts2D(Blend2D n, float x, float y, bool own = true)
        {
            var pts = Enumerable.Range(0, n.Children.Count).Select(n.Position).ToList();
            var poses = Enumerable.Range(0, n.Children.Count).Select(i => ExpressionSyntax.Pose(n.Child(i)) ?? new Dictionary<string, float>()).ToList();
            var result = new Dictionary<string, float>();
            if (pts.Count == 0) return result;
            var w = CurveEval.W2D(pts, (x, y));
            for (int i = 0; i < poses.Count; i++)
                foreach (var kv in poses[i]) result[kv.Key] = (result.TryGetValue(kv.Key, out var v) ? v : 0) + w[i] * kv.Value;
            if (own) foreach (var kv in ExpressionSyntax.OwnParts(n.Node)) result[kv.Key] = kv.Value;
            return result;
        }

        public static bool HasNested(Blend2D n) => Enumerable.Range(0, n.Children.Count).Any(i => ExpressionSyntax.Pose(n.Child(i)) == null);

        public static bool CanAddPosition2D(Blend2D n, float x, float y)
        {
            double rx = Math.Round((double)x, 2), ry = Math.Round((double)y, 2);
            return !HasNested(n) && Enumerable.Range(0, n.Children.Count).All(i => { var p = n.Position(i); return Math.Abs(p.x - rx) > 0.001 || Math.Abs(p.y - ry) > 0.001; });
        }

        public static bool CanRemovePosition2D(Blend2D n) => n.Children.Count > 3;

        // Adds a position at (x, y) with the amounts the node has there now, at the end. Returns its index.
        public static int AddPosition2D(Blend2D n, float x, float y)
        {
            double rx = Math.Round((double)x, 2), ry = Math.Round((double)y, 2);
            var pose = new JObject();
            foreach (var kv in Amounts2D(n, (float)rx, (float)ry, own: false).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var a = (long)Math.Round(kv.Value);
                if (a != 0) pose[kv.Key] = a;
            }
            n.Children.Add(new JArray(new JArray(Num(rx), Num(ry)), pose));
            return n.Children.Count - 1;
        }

        public static void RemovePosition2D(Blend2D n, int i)
        {
            if (CanRemovePosition2D(n)) n.Children.RemoveAt(i);
        }

        // Sets X or Y of a position, kept in the input's range and rounded to 2 decimals.
        public static void SetPosition2D(Blend2D n, int i, float x, float y)
        {
            var (rx, ry) = InputRange(n);
            var pos = (JArray)((JArray)n.Children[i])[0];
            pos[0] = Num(Math.Round((double)Math.Min(Math.Max(x, rx.lo), rx.hi), 2));
            pos[1] = Num(Math.Round((double)Math.Min(Math.Max(y, ry.lo), ry.hi), 2));
        }

        // The weights of the face at a point of a 2D node, with the switches above the node held in its states.
        public static SortedDictionary<string, float> TryWeights2D(TableModel model, Tree t, Blend2D n, float x, float y, IDictionary<string, float> others)
        {
            var inputs = new Dictionary<string, float>(others ?? new Dictionary<string, float>());
            foreach (var name in Expanded(t, n.InputX)) inputs[name] = x;
            foreach (var name in Expanded(t, n.InputY)) inputs[name] = y;
            var tracker = new SwitchTracker();
            foreach (var (sw, st) in n.When)
                foreach (var name in Expanded(t, sw)) tracker.Pinned[name] = st;
            return CurveEval.Evaluate(model, inputs, tracker.Update(model, inputs));
        }

        // Position i may move between its neighbours (the ends of the input range for the first and last).
        public static (float lo, float hi) PositionRange(Blend1D n, int i)
        {
            var (lo, hi) = InputRange(n);
            return (i > 0 ? n.Position(i - 1) : lo, i + 1 < n.Children.Count ? n.Position(i + 1) : hi);
        }

        // Sets position i, kept between its neighbours and rounded to 2 decimals. Returns the value written.
        public static float SetPosition(Blend1D n, int i, float value)
        {
            var (lo, hi) = PositionRange(n, i);
            // Written as a double so that 0.5 stays "0.5" in the file (a float would widen to 0.5000000xxx).
            var v = Math.Round((double)Math.Min(Math.Max(value, lo), hi), 2);
            ((JArray)n.Children[i])[0] = Num(v);
            return (float)v;
        }

        // The amount of each face movement along the input, for the graph: part -> values at xs.
        // With own, the node's own parts (All positions) are added as constant lines.
        public static Dictionary<string, float[]> Curve(Blend1D n, float[] xs, bool own = true)
        {
            var ths = Enumerable.Range(0, n.Children.Count).Select(n.Position).ToList();
            var poses = Enumerable.Range(0, n.Children.Count).Select(i => ExpressionSyntax.Pose(n.Child(i)) ?? new Dictionary<string, float>()).ToList();
            var parts = poses.SelectMany(p => p.Keys).Distinct().ToList();
            var result = parts.ToDictionary(p => p, p => new float[xs.Length]);
            if (ths.Count == 0) return result;
            for (int k = 0; k < xs.Length; k++)
            {
                var w = CurveEval.W1D(ths, xs[k]);
                for (int i = 0; i < poses.Count; i++)
                    foreach (var kv in poses[i]) result[kv.Key][k] += w[i] * kv.Value;
            }
            if (own)
                foreach (var kv in ExpressionSyntax.OwnParts(n.Node))
                    result[kv.Key] = Enumerable.Repeat(kv.Value, xs.Length).ToArray();
            return result;
        }

        // The weights of the face for a value of the node's input: the other inputs as given, the node's input (both
        // sides) at the value, and the switches above the node held in its states.
        public static SortedDictionary<string, float> TryWeights(TableModel model, Tree t, Blend1D n, float value, IDictionary<string, float> others)
        {
            var inputs = new Dictionary<string, float>(others ?? new Dictionary<string, float>());
            foreach (var name in Expanded(t, n.Input)) inputs[name] = value;
            var tracker = new SwitchTracker();
            foreach (var (sw, st) in n.When)
                foreach (var name in Expanded(t, sw)) tracker.Pinned[name] = st;
            return CurveEval.Evaluate(model, inputs, tracker.Update(model, inputs));
        }

        // ---- face movements of a position (a pose: part -> amount)

        // The pose object of a child, made when the child is null. Null when the child is a nested node.
        public static JObject PoseObject(JArray children, int i)
        {
            var row = (JArray)children[i];
            if (row[1] == null || row[1].Type == JTokenType.Null) row[1] = new JObject();
            return ExpressionSyntax.Pose(row[1]) != null ? (JObject)row[1] : null;
        }

        // The pose object of a state of a switch node, made when the state has no case. Null when the case is a nested node.
        public static JObject CasePose(SwitchCases n, string state)
        {
            if (!(n.Node[ExpressionSyntax.KeyCases] is JObject cases)) n.Node[ExpressionSyntax.KeyCases] = cases = new JObject();
            var c = cases[state];
            if (c == null || c.Type == JTokenType.Null) cases[state] = c = new JObject();
            return ExpressionSyntax.Pose(c) != null ? (JObject)c : null;
        }

        // The weights of the face with a switch node held in a state (and the switches above it in theirs).
        public static SortedDictionary<string, float> TryWeightsSwitch(TableModel model, Tree t, SwitchCases n, string state, IDictionary<string, float> others)
        {
            var inputs = new Dictionary<string, float>(others ?? new Dictionary<string, float>());
            var tracker = new SwitchTracker();
            foreach (var (sw, st) in n.When.Append((n.Switch, state)))
                foreach (var name in Expanded(t, sw)) tracker.Pinned[name] = st;
            return CurveEval.Evaluate(model, inputs, tracker.Update(model, inputs));
        }

        // Amounts are written as whole numbers so that 100 stays "100".
        public static void SetAmount(JObject pose, string part, float amount) =>
            ExpressionSyntax.SetPart(pose, part, (long)Math.Round(Math.Min(Math.Max(amount, 0), 100)));

        public static void RemovePart(JObject pose, string part) => pose.Remove(part);

        public static void AddPart(JObject pose, string part, float amount = 100)
        {
            if (pose[part] == null) SetAmount(pose, part, amount);
        }

        // ---- face movements of a 1D or 2D node itself (All positions): written while the node is chosen, at every position

        // The parts written anywhere under the node (its rows' poses and the nodes below), as written.
        public static HashSet<string> PartsBelow(JObject node)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in ExpressionSyntax.Rows(node))
                foreach (var (o, _) in ExpressionSyntax.Walk(r.Content))
                    foreach (var part in ExpressionSyntax.OwnParts(o).Keys) names.Add(part);
            return names;
        }

        // Parts that every position has with the same amount (none when a position holds a node).
        public static Dictionary<string, float> SameAtEveryPosition(JObject node)
        {
            var poses = ExpressionSyntax.Rows(node).Select(r => ExpressionSyntax.Pose(r.Content)).ToList();
            if (poses.Count == 0 || poses.Any(p => p == null)) return new Dictionary<string, float>();
            return poses[0].Where(kv => poses.All(p => p.TryGetValue(kv.Key, out var v) && v == kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // Moves a part that every position has with the same amount to the node. The face does not change.
        public static bool MakeAllPositions(JObject node, string part)
        {
            if (!SameAtEveryPosition(node).TryGetValue(part, out var v)) return false;
            foreach (var r in ExpressionSyntax.Rows(node)) (r.Content as JObject)?.Remove(part);
            SetAmount(node, part, v);
            return true;
        }

        // Writes the node's own part at every position instead (into the own parts of a node at a position, and into
        // every state of a switch at a position), and removes it from the node. The face does not change.
        // statesOf: switch name -> its states, for the states that have no case yet.
        public static bool ToPositions(JObject node, string part, Func<string, List<string>> statesOf = null)
        {
            if (!(node[part] is JValue value) || !ExpressionSyntax.IsPartKey(part)) return false;
            var rows = ExpressionSyntax.Rows(node);
            for (int i = 0; i < rows.Count; i++) ExpressionSyntax.SetContent(node, i, WriteInto(rows[i].Content, part, value, statesOf));
            node.Remove(part);
            return true;
        }

        // The content with the part written into it: a pose (made from null) or a 1D or 2D node takes it as its own;
        // a switch takes it in every state.
        public static JToken WriteInto(JToken content, string part, JToken value, Func<string, List<string>> statesOf = null)
        {
            switch (ExpressionSyntax.KindOf(content))
            {
                case ExpressionSyntax.Kind.Switch:
                    var sw = (JObject)content;
                    if (!(sw[ExpressionSyntax.KeyCases] is JObject cases)) sw[ExpressionSyntax.KeyCases] = cases = new JObject();
                    foreach (var st in statesOf?.Invoke(ExpressionSyntax.SwitchOf(sw)) ?? new List<string>())
                        if (cases[st] == null) cases[st] = new JObject();
                    var states = ExpressionSyntax.Rows(sw);
                    for (int i = 0; i < states.Count; i++) ExpressionSyntax.SetContent(sw, i, WriteInto(states[i].Content, part, value, statesOf));
                    return sw;
                case ExpressionSyntax.Kind.Other:
                    return content;
                default:
                    var o = content as JObject ?? new JObject();
                    ExpressionSyntax.SetPart(o, part, value.DeepClone());
                    return o;
            }
        }

        // ---- adding and removing positions of a 1D node

        public static bool HasNested(Blend1D n) => Enumerable.Range(0, n.Children.Count).Any(i => ExpressionSyntax.Pose(n.Child(i)) == null);

        public static bool CanAddPosition(Blend1D n, float x)
        {
            var v = Math.Round((double)x, 2);
            return !HasNested(n) && Enumerable.Range(0, n.Children.Count).All(i => Math.Abs(n.Position(i) - v) > 0.001);
        }

        public static bool CanRemovePosition(Blend1D n) => n.Children.Count > 2;

        // Adds a position at x with the amounts the node has there now, so the face does not change. Returns its index.
        public static int AddPosition(Blend1D n, float x)
        {
            var v = Math.Round((double)x, 2);
            var amounts = Curve(n, new[] { (float)v }, own: false);
            var pose = new JObject();
            foreach (var kv in amounts.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var a = (long)Math.Round(kv.Value[0]);
                if (a != 0) pose[kv.Key] = a;
            }
            var index = Enumerable.Range(0, n.Children.Count).Count(i => n.Position(i) < v);
            n.Children.Insert(index, new JArray(Num(v), pose));
            return index;
        }

        public static void RemovePosition(Blend1D n, int i)
        {
            if (CanRemovePosition(n)) n.Children.RemoveAt(i);
        }

        // ---- names for "+ Add face movement"

        // The side variable whose values are L and R (the template's "s"), or null.
        public static string PairVariable(Tree t)
        {
            if (t.Sides == null || t.Sides.Count != 2) return null;
            foreach (var k in t.Sides[0].Keys)
            {
                if (k == "side" || !t.Sides[1].ContainsKey(k)) continue;
                var vals = new[] { t.Sides[0][k], t.Sides[1][k] };
                if (vals.Contains("L") && vals.Contains("R")) return k;
            }
            return null;
        }

        // All face movement names: the avatar settings' parts and missing parts, and the expression's parts expanded.
        public static SortedSet<string> AllPartNames(JObject avatar, JObject expression)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (avatar?["parts"] is JObject parts) foreach (var p in parts.Properties()) names.Add(p.Name);
            if (avatar?["missingParts"] is JArray mp) foreach (var x in mp.Where(x => x.Type == JTokenType.String)) names.Add((string)x);
            // Every pose and node of the trees (a node's own parts too), with the sides expanded.
            if (expression?["trees"] is JObject trees)
                foreach (var p in trees.Properties().Where(p => p.Value is JObject))
                {
                    var sides = SidesOf(p.Value["sides"]) ?? new List<Dictionary<string, string>> { null };
                    foreach (var (o, _) in ExpressionSyntax.Walk(p.Value["tree"]))
                        foreach (var part in ExpressionSyntax.OwnParts(o).Keys)
                            foreach (var vars in sides) names.Add(Sub(part, vars));
                }
            return names;
        }

        // Menu items for a tree: (label, name to write). In a left/right tree, "<base>_L" and "<base>_R" become one
        // item "<base>" written as "<base>_{s}".
        public static List<(string label, string write)> Choices(Tree t, IEnumerable<string> allNames)
        {
            var names = allNames.ToList();
            var v = PairVariable(t);
            var result = new List<(string, string)>();
            var paired = new HashSet<string>();
            if (v != null)
                foreach (var n in names.Where(n => n.EndsWith("_L")))
                {
                    var b = n.Substring(0, n.Length - 2);
                    if (!names.Contains(b + "_R")) continue;
                    result.Add((b, b + "_{" + v + "}"));
                    paired.Add(b + "_L"); paired.Add(b + "_R");
                }
            foreach (var n in names.Where(n => !paired.Contains(n))) result.Add((n, n));
            return result.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
        }

        static readonly Regex NameRule = new Regex(@"^[a-z0-9_]+\.[a-z0-9_]+$");

        public enum NameProblem { None, Form, Used }

        // A new face movement name: lowercase letters, digits and _, with one dot, and not used yet (also as _L/_R).
        public static NameProblem CheckName(string name, bool separate, IEnumerable<string> allNames)
        {
            if (string.IsNullOrEmpty(name) || !NameRule.IsMatch(name)) return NameProblem.Form;
            var names = new HashSet<string>(allNames);
            if (names.Contains(name) || names.Contains(name + "_L") || names.Contains(name + "_R")) return NameProblem.Used;
            return NameProblem.None;
        }

        public static SortedDictionary<string, float> TryWeights2D(TableModel model, Tree t, Blend2D n, int i, IDictionary<string, float> others)
        {
            var (x, y) = n.Position(i);
            return TryWeights2D(model, t, n, x, y, others);
        }

        // ---- smoothing

        public static readonly double Ln10 = Math.Log(10);

        // Time in ms to catch up 90% of a sudden change: tau * ln 10.
        public static int Ms(float tau) => (int)Math.Round(tau * Ln10 * 1000);
        public static double Tau(int ms) => Math.Round(ms / 1000.0 / Ln10, 4);

        public static List<(string name, JObject def)> Smoothings(JObject input) =>
            input?["smoothing"] is JObject sm ? sm.Properties().Where(p => p.Value is JObject).Select(p => (p.Name, (JObject)p.Value)).ToList()
                                             : new List<(string, JObject)>();

        // Writes localTau or remoteTau for a time in ms (0 to 2000).
        public static double SetMs(JObject def, bool local, int ms)
        {
            var tau = Tau(Math.Min(Math.Max(ms, 0), 2000));
            def[local ? "localTau" : "remoteTau"] = Num(tau);
            return tau;
        }
    }
}
