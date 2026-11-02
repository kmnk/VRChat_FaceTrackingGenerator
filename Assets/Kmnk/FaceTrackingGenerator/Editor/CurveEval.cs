using System;
using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Computes blendshape weights from the table model without Unity's Animator (spec R26):
    // 1D BlendTrees interpolate linearly between thresholds (clamped at the ends); 2D Freeform Cartesian uses
    // gradient band interpolation (Johansen 2009), an approximation that may differ from Unity's.
    // Trees are added together like the generated Direct BlendTree. FT is assumed on and not talking.
    public static class CurveEval
    {
        // inputs: input name -> value (missing = 0). states: switch name -> state (missing = default state).
        // Returns "mesh/shape" -> weight.
        public static SortedDictionary<string, float> Evaluate(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states = null)
        {
            var result = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (var tree in EvaluateTrees(m, inputs, states))
                foreach (var kv in tree.Value)
                    result[kv.Key] = (result.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            return result;
        }

        // The same calculation before the parts become blendshapes: face movement (part) -> value, summed over trees.
        public static SortedDictionary<string, float> EvaluateParts(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states = null)
        {
            var values = new Dictionary<string, float>(inputs);
            foreach (var d in m.Inputs.Where(i => i.IsDerived))
                values[d.Name] = d.Average.Average(s => values.TryGetValue(s, out var v) ? v : 0);
            var result = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (var t in m.Trees)
                foreach (var kv in Node(m, t.Root, values, states))
                    result[kv.Key] = (result.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            return result;
        }

        // The same weights per tree: tree name -> "mesh/shape" -> weight, in the order of the trees.
        public static List<KeyValuePair<string, SortedDictionary<string, float>>> EvaluateTrees(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states = null)
        {
            var values = new Dictionary<string, float>(inputs);
            foreach (var d in m.Inputs.Where(i => i.IsDerived))
                values[d.Name] = d.Average.Average(s => values.TryGetValue(s, out var v) ? v : 0);
            var trees = new List<KeyValuePair<string, SortedDictionary<string, float>>>();
            foreach (var t in m.Trees)
            {
                var result = new SortedDictionary<string, float>(StringComparer.Ordinal);
                foreach (var kv in Node(m, t.Root, values, states))
                {
                    var key = m.Parts[kv.Key].Mesh + "/" + m.Parts[kv.Key].Shape;
                    result[key] = (result.TryGetValue(key, out var v) ? v : 0) + kv.Value;
                }
                trees.Add(new KeyValuePair<string, SortedDictionary<string, float>>(t.Name, result));
            }
            return trees;
        }

        static Dictionary<string, float> Node(TableModel m, Node n, Dictionary<string, float> values, IDictionary<string, string> states)
        {
            float V(string name) => values.TryGetValue(name, out var v) ? v : 0;
            var outp = new Dictionary<string, float>();
            void Add(Dictionary<string, float> sub, float w)
            {
                if (w == 0) return;
                foreach (var kv in sub) outp[kv.Key] = (outp.TryGetValue(kv.Key, out var o) ? o : 0) + w * kv.Value;
            }
            switch (n)
            {
                case PoseNode p:
                    foreach (var kv in p.Parts) outp[kv.Key] = kv.Value;
                    break;
                case Blend1DNode b:
                {
                    var w = W1D(b.Children.Select(c => c.threshold).ToList(), V(b.Input));
                    for (int i = 0; i < w.Count; i++) Add(Node(m, b.Children[i].node, values, states), w[i]);
                    break;
                }
                case Blend2DNode b:
                {
                    var w = W2D(b.Children.Select(c => (c.x, c.y)).ToList(), (V(b.InputX), V(b.InputY)));
                    for (int i = 0; i < w.Count; i++) Add(Node(m, b.Children[i].node, values, states), w[i]);
                    break;
                }
                case SwitchNode s:
                {
                    var state = states != null && states.TryGetValue(s.Switch, out var st) ? st : m.Switch(s.Switch).States[0];
                    var c = s.Cases.FirstOrDefault(x => x.state == state);
                    if (c.node != null) Add(Node(m, c.node, values, states), 1);
                    break;
                }
            }
            return outp;
        }

        public static List<float> W1D(List<float> ths, float x)
        {
            var n = ths.Count;
            var w = Enumerable.Repeat(0f, n).ToList();
            var order = Enumerable.Range(0, n).OrderBy(i => ths[i]).ToList();
            if (x <= ths[order[0]]) { w[order[0]] = 1; return w; }
            if (x >= ths[order[n - 1]]) { w[order[n - 1]] = 1; return w; }
            for (int k = 0; k + 1 < n; k++)
            {
                int a = order[k], b = order[k + 1];
                if (ths[a] <= x && x <= ths[b])
                {
                    var t = ths[b] != ths[a] ? (x - ths[a]) / (ths[b] - ths[a]) : 0;
                    w[a] = 1 - t; w[b] = t;
                    return w;
                }
            }
            return w;
        }

        public static List<float> W2D(List<(float x, float y)> pts, (float x, float y) p)
        {
            var hs = new List<float>();
            for (int i = 0; i < pts.Count; i++)
            {
                var h = 1f;
                for (int j = 0; j < pts.Count; j++)
                {
                    if (i == j) continue;
                    float dx = pts[j].x - pts[i].x, dy = pts[j].y - pts[i].y, dd = dx * dx + dy * dy;
                    if (dd == 0) continue;
                    h = Math.Min(h, 1 - ((p.x - pts[i].x) * dx + (p.y - pts[i].y) * dy) / dd);
                }
                hs.Add(Math.Max(h, 0));
            }
            var s = hs.Sum();
            return s > 0 ? hs.Select(h => h / s).ToList() : Enumerable.Repeat(1f / pts.Count, pts.Count).ToList();
        }

        // The input range to draw: from the smallest to the largest threshold or position that uses it.
        public static (float min, float max) Range(TableModel m, string input)
        {
            var vals = new List<float>();
            void Walk(Node n)
            {
                switch (n)
                {
                    case Blend1DNode b: if (b.Input == input) vals.AddRange(b.Children.Select(c => c.threshold)); foreach (var c in b.Children) Walk(c.node); break;
                    case Blend2DNode b:
                        if (b.InputX == input) vals.AddRange(b.Children.Select(c => c.x));
                        if (b.InputY == input) vals.AddRange(b.Children.Select(c => c.y));
                        foreach (var c in b.Children) Walk(c.node);
                        break;
                    case SwitchNode s: foreach (var c in s.Cases) Walk(c.node); break;
                }
            }
            foreach (var t in m.Trees) Walk(t.Root);
            if (vals.Count == 0) return (0, 1);
            float lo = vals.Min(), hi = vals.Max();
            return lo == hi ? (lo - 1, hi + 1) : (lo, hi);
        }
    }
}
