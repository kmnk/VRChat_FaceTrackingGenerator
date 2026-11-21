using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Turns response curves into the Custom expression of a Parameter Behavior (design.md "カーブを式にする"): each curve
    // becomes a polyline within Tolerance of it (split where it is farthest off, on CurveKeys.Samples samples), the
    // polylines of one blendshape go into one expression as max(a, max(b, ...)), and points are taken out while the
    // expression is over VRChat's limits of one expression (the largest difference that leaves is MaxError).
    public static class CurveFit
    {
        public const float Tolerance = 0.01f;
        public const int MaxCharacters = 1024, MaxTokens = 256, MaxInstructions = 256;

        // One row's polyline: points on the sample grid (index i is x = i / Samples).
        public sealed class Line
        {
            public string Key, Part;
            public float[] Samples;          // the curve x the part's scale, cut to 0..1
            public List<int> Points;         // sample indices, from 0 to Samples
            public float MaxError;
            public float X(int k) => Points[k] / (float)CurveKeys.Samples;
            public float Y(int k) => Samples[Points[k]];

            public float Eval(float x)
            {
                x = Mathf.Clamp01(x);
                for (int k = 1; k < Points.Count; k++)
                    if (x <= X(k) || k == Points.Count - 1)
                    {
                        float x0 = X(k - 1), x1 = X(k);
                        return x1 <= x0 ? Y(k) : Y(k - 1) + (Y(k) - Y(k - 1)) * (x - x0) / (x1 - x0);
                    }
                return Y(0);
            }

            public Line Copy() => new Line { Key = Key, Part = Part, Samples = Samples, Points = new List<int>(Points), MaxError = MaxError };
        }

        // The samples of value(x) for x = 0, 0.001, ..., 1, cut to 0..1.
        public static float[] Sample(Func<float, float> value)
        {
            var s = new float[CurveKeys.Samples + 1];
            for (int i = 0; i <= CurveKeys.Samples; i++) s[i] = Mathf.Clamp01(value(i / (float)CurveKeys.Samples));
            return s;
        }

        static readonly ConditionalWeakTable<CurveKeys, Dictionary<float, Line>> cache = new ConditionalWeakTable<CurveKeys, Dictionary<float, Line>>();

        // The polyline of a row: its curve (or the straight line of its strength) x scale.
        public static Line Fit(string key, string part, CurveKeys curve, float strength, float scale)
        {
            if (curve == null) return FitSamples(key, part, Sample(x => strength * x * scale));
            var byScale = cache.GetOrCreateValue(curve);
            if (!byScale.TryGetValue(scale, out var line))
            {
                var c = curve.ToAnimationCurve();
                line = FitSamples(null, null, Sample(x => CurveKeys.Value(c, x) * scale));
                byScale[scale] = line;
            }
            var r = line.Copy();
            r.Key = key; r.Part = part;
            return r;
        }

        public static Line FitSamples(string key, string part, float[] samples)
        {
            var pts = new List<int> { 0, CurveKeys.Samples };
            while (true)
            {
                var (seg, at, err) = Worst(samples, pts);
                if (err <= Tolerance) return new Line { Key = key, Part = part, Samples = samples, Points = pts, MaxError = err };
                pts.Insert(seg + 1, at);
            }
        }

        // The sample farthest from the polyline: (segment, sample index, difference).
        static (int seg, int at, float err) Worst(float[] s, List<int> pts)
        {
            int seg = 0, at = 0; float err = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                int a = pts[k - 1], b = pts[k];
                for (int i = a + 1; i < b; i++)
                {
                    var e = Math.Abs(s[i] - (s[a] + (s[b] - s[a]) * (i - a) / (float)(b - a)));
                    if (e > err) { err = e; seg = k - 1; at = i; }
                }
            }
            return (seg, at, err);
        }

        static float SegmentError(float[] s, int a, int b)
        {
            float err = 0;
            for (int i = a + 1; i < b; i++) err = Math.Max(err, Math.Abs(s[i] - (s[a] + (s[b] - s[a]) * (i - a) / (float)(b - a))));
            return err;
        }

        // Fits the lines of one blendshape into one expression: takes out the point whose removal raises a line's
        // largest difference the least until the expression is within the limits. Returns the expression.
        public static string FitOne(List<Line> lines)
        {
            while (true)
            {
                var text = Expression(lines);
                var c = FtExpression.Count(text);
                if (c.Characters <= MaxCharacters && c.Tokens <= MaxTokens && c.Instructions <= MaxInstructions) return text;
                Line bestLine = null; int bestK = -1; float bestErr = float.MaxValue;
                foreach (var l in lines)
                    for (int k = 1; k < l.Points.Count - 1; k++)
                    {
                        var e = Math.Max(SegmentError(l.Samples, l.Points[k - 1], l.Points[k + 1]), l.MaxError);
                        if (e < bestErr) { bestErr = e; bestLine = l; bestK = k; }
                    }
                if (bestLine == null) return text;  // only straight segments are left: cannot shorten further
                bestLine.Points.RemoveAt(bestK);
                bestLine.MaxError = Worst(bestLine.Samples, bestLine.Points).err;
            }
        }

        // ---- the expression

        public static string Expression(List<Line> lines)
        {
            var parts = lines.Select(LineExpression).ToList();
            var text = parts[parts.Count - 1];
            for (int i = parts.Count - 2; i >= 0; i--) text = $"max({parts[i]}, {text})";
            return text;
        }

        // f(x) = y0 + sum of s_k (clamp(K, x_k-1, x_k) - x_k-1): the first segment as min(K, x1) (K >= 0), the last as
        // max(K, x_n-1) (K <= 1), segments with no slope left out, the constants gathered into one.
        public static string LineExpression(Line l)
        {
            var n = l.Points.Count - 1;
            var sb = new System.Text.StringBuilder();
            double c = l.Y(0);
            for (int k = 1; k <= n; k++)
            {
                double x0 = l.X(k - 1), x1 = l.X(k);
                double s = (l.Y(k) - l.Y(k - 1)) / (x1 - x0);
                if (Math.Abs(s) < 1e-7) continue;
                c -= s * x0;
                string term = n == 1 ? l.Key
                    : k == 1 ? $"min({l.Key}, {N(x1)})"
                    : k == n ? $"max({l.Key}, {N(x0)})"
                    : $"clamp({l.Key}, {N(x0)}, {N(x1)})";
                var mag = Math.Abs(s);
                var t = Math.Abs(mag - 1) < 1e-7 ? term : $"{term} * {N(mag)}";
                if (sb.Length == 0) sb.Append(s < 0 ? "-" + t : t);
                else sb.Append(s < 0 ? " - " : " + ").Append(t);
            }
            // A behavior without a tracking input is ignored by the client: a flat line still reads its key.
            if (sb.Length == 0) sb.Append($"{l.Key} * 0");
            if (Math.Abs(c) >= 5e-7) sb.Append(c < 0 ? " - " : " + ").Append(N(Math.Abs(c)));
            return sb.ToString();
        }

        static string N(double v)
        {
            var t = v.ToString("0.#####", CultureInfo.InvariantCulture);
            return t == "-0" ? "0" : t;
        }
    }
}
