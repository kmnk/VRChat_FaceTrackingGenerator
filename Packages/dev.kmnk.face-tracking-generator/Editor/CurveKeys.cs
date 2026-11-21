using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // A row's response curve as the expression table writes it ("curve"): Unity Keyframes as plain data. The value of a
    // key value x is the AnimationCurve's value at x, cut to 0..1; its highest point is the row's strength.
    // ToAnimationCurve makes the Unity curve (Unity evaluates it, weighted tangents included); From reads one back.
    public sealed class CurveKeys
    {
        public sealed class Key
        {
            public float Time, Value, InTangent, OutTangent, InWeight = DefaultWeight, OutWeight = DefaultWeight;
            // AnimationUtility.TangentMode names; WeightedMode names
            public string Left = "Free", Right = "Free", Weighted = "None";
        }

        public const float DefaultWeight = 1f / 3f;
        public static readonly string[] TangentModes = { "Free", "Auto", "ClampedAuto", "Linear", "Constant" };
        public static readonly string[] WeightedModes = { "None", "In", "Out", "Both" };
        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
            { "time", "value", "inTangent", "outTangent", "leftTangent", "rightTangent", "inWeight", "outWeight", "weightedMode" };

        public List<Key> Keys = new List<Key>();

        // Samples used to tell a straight curve and to fit polylines: 0, 0.001, ..., 1.
        public const int Samples = 1000;
        // A curve within this of the straight line s x at every sample is that line.
        public const float StraightTolerance = 0.0001f;

        public AnimationCurve ToAnimationCurve()
        {
            var frames = Keys.Select(k => new Keyframe(k.Time, k.Value, Finite(k.InTangent), Finite(k.OutTangent), k.InWeight, k.OutWeight)
            {
                weightedMode = (WeightedMode)Enum.Parse(typeof(WeightedMode), k.Weighted),
            }).ToArray();
            var c = new AnimationCurve(frames);
            for (int i = 0; i < Keys.Count; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, Mode(Keys[i].Left));
                AnimationUtility.SetKeyRightTangentMode(c, i, Mode(Keys[i].Right));
            }
            // Setting a mode makes Unity compute the tangents of the modes other than Free (from the neighbours). The
            // Free sides keep the written tangents.
            for (int i = 0; i < Keys.Count; i++)
            {
                var f = c.keys[i];
                if (Keys[i].Left == "Free") f.inTangent = Finite(Keys[i].InTangent);
                if (Keys[i].Right == "Free") f.outTangent = Finite(Keys[i].OutTangent);
                c.MoveKey(i, f);
            }
            return c;
        }

        static float Finite(float v) => float.IsNaN(v) ? 0 : v;

        static AnimationUtility.TangentMode Mode(string name) => (AnimationUtility.TangentMode)Enum.Parse(typeof(AnimationUtility.TangentMode), name);

        public static CurveKeys From(AnimationCurve c)
        {
            var r = new CurveKeys();
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                var left = AnimationUtility.GetKeyLeftTangentMode(c, i).ToString();
                var right = AnimationUtility.GetKeyRightTangentMode(c, i).ToString();
                // A Free side with an infinite tangent steps like Constant: written as Constant (JSON has no infinity).
                if (left == "Free" && float.IsInfinity(k.inTangent)) left = "Constant";
                if (right == "Free" && float.IsInfinity(k.outTangent)) right = "Constant";
                r.Keys.Add(new Key
                {
                    Time = k.time, Value = k.value,
                    InTangent = float.IsInfinity(k.inTangent) ? 0 : k.inTangent,
                    OutTangent = float.IsInfinity(k.outTangent) ? 0 : k.outTangent,
                    InWeight = k.inWeight, OutWeight = k.outWeight,
                    Left = left, Right = right, Weighted = k.weightedMode.ToString(),
                });
            }
            return r;
        }

        // The straight line from (0, 0) to (1, s).
        public static AnimationCurve Line(float s) => AnimationCurve.Linear(0, 0, 1, s);

        // The value at x, cut to 0..1.
        public static float Value(AnimationCurve c, float x) => Mathf.Clamp01(c.Evaluate(x));

        // s when the curve is the straight line from (0, 0) to (1, s) (within StraightTolerance at every sample), else NaN.
        public static float Straight(AnimationCurve c)
        {
            var s = Value(c, 1);
            for (int i = 0; i <= Samples; i++)
            {
                var x = i / (float)Samples;
                if (Math.Abs(Value(c, x) - s * x) > StraightTolerance) return float.NaN;
            }
            return s;
        }

        // The highest value of the curve (its strength).
        public static float Max(AnimationCurve c)
        {
            float m = 0;
            for (int i = 0; i <= Samples; i++) m = Math.Max(m, Value(c, i / (float)Samples));
            return m;
        }

        // ---- the table's form

        // Reads "curve" of one device: an array of keys. error(message) makes the exception to throw.
        public static CurveKeys Parse(JToken t, Func<string, Exception> error)
        {
            if (!(t is JArray arr)) throw error($"'{t.Path}' must be an array of keys");
            if (arr.Count < 2) throw error($"'{t.Path}' needs 2 keys or more");
            var r = new CurveKeys();
            foreach (var kt in arr)
            {
                if (!(kt is JObject ko)) throw error($"'{kt.Path}' must be a key {{\"time\", \"value\", ...}}");
                foreach (var p in ko.Properties())
                    if (!Fields.Contains(p.Name)) throw error($"'{p.Path}': a curve key has {string.Join(", ", Fields)} only");
                float Number(string name, float def, bool required = false)
                {
                    var v = ko[name];
                    if (v == null) { if (required) throw error($"'{Join(ko.Path, name)}' is required"); return def; }
                    if (v.Type != JTokenType.Float && v.Type != JTokenType.Integer) throw error($"'{v.Path}' must be a number");
                    return v.Value<float>();
                }
                string Name(string name, string[] allowed, string def)
                {
                    var v = ko[name];
                    if (v == null) return def;
                    if (v.Type != JTokenType.String || !allowed.Contains((string)v)) throw error($"'{v.Path}' must be one of {string.Join(", ", allowed)}");
                    return (string)v;
                }
                var k = new Key
                {
                    Time = Number("time", 0, true), Value = Number("value", 0, true),
                    InTangent = Number("inTangent", 0), OutTangent = Number("outTangent", 0),
                    InWeight = Number("inWeight", DefaultWeight), OutWeight = Number("outWeight", DefaultWeight),
                    Left = Name("leftTangent", TangentModes, "Free"), Right = Name("rightTangent", TangentModes, "Free"),
                    Weighted = Name("weightedMode", WeightedModes, "None"),
                };
                if (k.Time < 0 || k.Time > 1) throw error($"'{Join(ko.Path, "time")}' must be 0 to 1");
                if (k.Value < 0 || k.Value > 1) throw error($"'{Join(ko.Path, "value")}' must be 0 to 1");
                if (r.Keys.Count > 0 && k.Time <= r.Keys[r.Keys.Count - 1].Time) throw error($"'{Join(ko.Path, "time")}': the keys' times must increase");
                r.Keys.Add(k);
            }
            if (Math.Abs(r.Keys[0].Time) > 1e-6f) throw error($"'{arr.Path}': the first key's time must be 0");
            if (Math.Abs(r.Keys[r.Keys.Count - 1].Time - 1) > 1e-6f) throw error($"'{arr.Path}': the last key's time must be 1");
            return r;
        }

        static string Join(string path, string key) => string.IsNullOrEmpty(path) ? key : path + "." + key;

        // The table's form of the keys: defaults left out, times and values to 6 places (a steep segment moves the curve
        // by its slope x the rounding), tangents and weights to 4. A tangent is written only on a Free side
        // (Unity computes the others from the mode), a weight only on a weighted side.
        public JArray ToJson()
        {
            var a = new JArray();
            foreach (var k in Keys)
            {
                var o = new JObject { ["time"] = Num(k.Time, 6), ["value"] = Num(k.Value, 6) };
                var inW = k.Weighted == "In" || k.Weighted == "Both";
                var outW = k.Weighted == "Out" || k.Weighted == "Both";
                if (k.Left == "Free" && Math.Abs(k.InTangent) > 1e-6f) o["inTangent"] = Num(k.InTangent);
                if (k.Right == "Free" && Math.Abs(k.OutTangent) > 1e-6f) o["outTangent"] = Num(k.OutTangent);
                if (k.Left != "Free") o["leftTangent"] = k.Left;
                if (k.Right != "Free") o["rightTangent"] = k.Right;
                if (inW && Math.Abs(k.InWeight - DefaultWeight) > 1e-4f) o["inWeight"] = Num(k.InWeight);
                if (outW && Math.Abs(k.OutWeight - DefaultWeight) > 1e-4f) o["outWeight"] = Num(k.OutWeight);
                if (k.Weighted != "None") o["weightedMode"] = k.Weighted;
                a.Add(o);
            }
            return a;
        }

        static JToken Num(float v, int places = 4)
        {
            var d = Math.Round((double)v, places);
            return d == Math.Floor(d) && Math.Abs(d) < 1e9 ? (JToken)(long)d : d;
        }

        // Whether two curves are the same in the table's form (null is the straight line of the strength).
        public static bool Same(CurveKeys a, CurveKeys b) =>
            a == null || b == null ? a == b : JToken.DeepEquals(a.ToJson(), b.ToJson());

        public override string ToString() => ToJson().ToString(Newtonsoft.Json.Formatting.None);
    }
}
