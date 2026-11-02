using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The Tuning tab without drawing: the 1D nodes of the expression table (positions where face movements change)
    // and the smoothing times of the input table. Works on the JSON trees, so a left/right pair stays one node.
    public static class Tuning
    {
        public sealed class Blend1D
        {
            public JObject Node;                                   // {"1d": input, "children": [[position, pose or node], ...]}
            public string Input;                                   // as written, e.g. "EyeLid{side}"
            public List<(string sw, string state)> When = new List<(string, string)>();  // switches above it, as written
            public JArray Children => (JArray)Node["children"];
            public float Position(int i) => ((JArray)Children[i])[0].Value<float>();
            public JToken Child(int i) => ((JArray)Children[i])[1];
        }

        public sealed class Tree
        {
            public string Name;                                    // as written, e.g. "Eyelid{side}"
            public List<Dictionary<string, string>> Sides;         // the side variables, or null for a tree without sides
            public List<Blend1D> Nodes = new List<Blend1D>();
            public bool Has2D;
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
                Walk(entry["tree"], new List<(string, string)>(), t);
                result.Add(t);
            }
            return result;
        }

        static void Walk(JToken token, List<(string, string)> path, Tree t)
        {
            if (!(token is JObject o)) return;
            if (o["1d"] != null && o["children"] is JArray ch1)
            {
                t.Nodes.Add(new Blend1D { Node = o, Input = (string)o["1d"], When = path.ToList() });
                foreach (var c in ch1.OfType<JArray>().Where(c => c.Count == 2)) Walk(c[1], path, t);
            }
            else if (o["2d"] != null && o["children"] is JArray ch2)
            {
                t.Has2D = true;
                foreach (var c in ch2.OfType<JArray>().Where(c => c.Count == 2)) Walk(c[1], path, t);
            }
            else if (o["switch"] != null && o["cases"] is JObject cases)
                foreach (var c in cases.Properties()) Walk(c.Value, path.Append(((string)o["switch"], c.Name)).ToList(), t);
        }

        // The same side variables as TableLoader.Expand: ["Left", "Right"] or {"Left": {"s": "L"}, ...}.
        static List<Dictionary<string, string>> SidesOf(JToken sides)
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

        // The face movements at a position: "eye.close 100", "普通の顔" or "別の表情の組み合わせ".
        public static string ChildText(JToken child)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var pose = Pose(child);
            if (pose == null) return ja ? "別の表情の組み合わせ" : "Nested expressions";
            if (pose.Count == 0) return ja ? "普通の顔" : "Neutral";
            return string.Join(ja ? "、" : ", ", pose.Select(kv => $"{Label(kv.Key)} {Math.Round(kv.Value)}"));
        }

        // Part -> value of a pose child, or null when the child is a nested node.
        public static Dictionary<string, float> Pose(JToken child)
        {
            if (child == null || child.Type == JTokenType.Null) return new Dictionary<string, float>();
            if (!(child is JObject o) || o["1d"] != null || o["2d"] != null || o["switch"] != null) return null;
            return o.Properties().Where(p => p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                .ToDictionary(p => p.Name, p => p.Value.Value<float>());
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

        // The range of an input's values (the same rule as the device lists), widened to the positions of the node.
        public static (float lo, float hi) InputRange(Blend1D n)
        {
            var name = Label(n.Input);
            var signed = name.EndsWith("X") || name.EndsWith("Y") || name == "SmileSad" || name == "SmileFrown" || name.StartsWith("CheekPuffSuck");
            float lo = signed ? -1 : 0, hi = 1;
            for (int i = 0; i < n.Children.Count; i++) { lo = Math.Min(lo, n.Position(i)); hi = Math.Max(hi, n.Position(i)); }
            return (lo, hi);
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
            ((JArray)n.Children[i])[0] = v;
            return (float)v;
        }

        // The amount of each face movement along the input, for the graph: part -> values at xs.
        public static Dictionary<string, float[]> Curve(Blend1D n, float[] xs)
        {
            var ths = Enumerable.Range(0, n.Children.Count).Select(n.Position).ToList();
            var poses = Enumerable.Range(0, n.Children.Count).Select(i => Pose(n.Child(i)) ?? new Dictionary<string, float>()).ToList();
            var parts = poses.SelectMany(p => p.Keys).Distinct().ToList();
            var result = parts.ToDictionary(p => p, p => new float[xs.Length]);
            if (ths.Count == 0) return result;
            for (int k = 0; k < xs.Length; k++)
            {
                var w = CurveEval.W1D(ths, xs[k]);
                for (int i = 0; i < poses.Count; i++)
                    foreach (var kv in poses[i]) result[kv.Key][k] += w[i] * kv.Value;
            }
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
            def[local ? "localTau" : "remoteTau"] = tau;
            return tau;
        }
    }
}
