using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // What moves each face movement: the tracking keys of its rows, and the states it is used in (override rows and
    // fixed amounts). A part of neutral moves in every state that does not take its region.
    public static class PartUsage
    {
        public sealed class Usage
        {
            public List<string> Inputs = new List<string>();
            public List<(string sw, List<string> states)> When = new List<(string, List<string>)>();
        }

        public static Dictionary<string, Usage> Of(TableModel m)
        {
            var result = new Dictionary<string, Usage>(StringComparer.Ordinal);
            Usage U(string part) => result.TryGetValue(part, out var u) ? u : result[part] = new Usage();
            var inNeutral = new HashSet<string>();
            foreach (var r in m.Neutral.Values.SelectMany(x => x))
            {
                inNeutral.Add(r.Part);
                if (!U(r.Part).Inputs.Contains(r.Key)) U(r.Part).Inputs.Add(r.Key);
            }
            foreach (var sm in m.StateMachines)
                foreach (var st in sm.States)
                {
                    var parts = st.Regions.Values.SelectMany(x => x.Rows).Where(r => r.Part != null).Select(r => (r.Part, r.Key)).ToList();
                    foreach (var (part, key) in parts) if (!U(part).Inputs.Contains(key)) U(part).Inputs.Add(key);
                    foreach (var part in parts.Select(x => x.Part).Concat(st.Fixed.Keys).Distinct().Where(p => !inNeutral.Contains(p)))
                    {
                        var u = U(part);
                        var w = u.When.FindIndex(x => x.sw == sm.Name);
                        if (w < 0) u.When.Add((sm.Name, new List<string> { st.Name }));
                        else if (!u.When[w].states.Contains(st.Name)) u.When[w].states.Add(st.Name);
                    }
                }
            return result;
        }

        // "EyeLidLeft で動く（EyelidSmileLeft が smile のとき）" / "Moves with EyeLidLeft (when EyelidSmileLeft is smile)"
        public static string Text(Usage u)
        {
            if (u == null || u.Inputs.Count == 0 && u.When.Count == 0) return null;
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var s = u.Inputs.Count == 0 ? (ja ? "固定" : "Fixed") : ja ? string.Join("・", u.Inputs) + " で動く" : "Moves with " + string.Join(", ", u.Inputs);
            if (u.When.Count > 0)
            {
                var conds = u.When.Select(w => ja ? $"{w.sw} が {string.Join(" / ", w.states)}" : $"{w.sw} is {string.Join(" / ", w.states)}");
                s += ja ? "（" + string.Join("、", conds) + " のとき）" : " (when " + string.Join(", ", conds) + ")";
            }
            return s;
        }

        // The description of an input for Source: "まぶた（EyeLidLeft）", or the name alone.
        public static string Source(string input)
        {
            var d = EditorText.InputDescription(input);
            return d == null ? input : (EditorText.Current == EditorText.Language.Japanese ? $"{d}（{input}）" : $"{d} ({input})");
        }
    }

    // Face movements of the same region and side that are strong at the same time (they can add up and break the shape).
    public static class Overlaps
    {
        public const float Strong = 50;

        public sealed class Overlap
        {
            public string Region;
            public string Side;  // "L", "R" or "B" (both sides)
            public List<(string part, float value)> Parts = new List<(string, float)>();
        }

        static string SideOf(string part) => part.EndsWith("_L") || part.EndsWith("Left") ? "L" : part.EndsWith("_R") || part.EndsWith("Right") ? "R" : "B";
        static string RegionOf(string part) { var i = part.IndexOf('.'); return i < 0 ? part : part.Substring(0, i); }

        public static List<Overlap> Find(IDictionary<string, float> partValues)
        {
            var result = new List<Overlap>();
            var strong = partValues.Where(kv => kv.Value > Strong && kv.Key.IndexOf("look", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            foreach (var region in strong.Select(kv => RegionOf(kv.Key)).Distinct())
            {
                var inRegion = strong.Where(kv => RegionOf(kv.Key) == region).ToList();
                var both = inRegion.Where(kv => SideOf(kv.Key) == "B").ToList();
                var reported = new List<string>();
                foreach (var side in new[] { "L", "R" })
                {
                    var set = inRegion.Where(kv => SideOf(kv.Key) == side || SideOf(kv.Key) == "B").ToList();
                    if (set.Count >= 2 && set.Any(kv => SideOf(kv.Key) == side))
                    {
                        result.Add(new Overlap { Region = region, Side = side, Parts = set.Select(kv => (kv.Key, kv.Value)).ToList() });
                        reported.Add(side);
                    }
                }
                if (both.Count >= 2 && reported.Count < 2)
                    result.Add(new Overlap { Region = region, Side = "B", Parts = both.Select(kv => (kv.Key, kv.Value)).ToList() });
            }
            return result;
        }

        public static string Message(Overlap o, IDictionary<string, PartUsage.Usage> usage)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var region = EditorText.Region(o.Region);
            var side = ja ? (o.Side == "L" ? "左側" : o.Side == "R" ? "右側" : "両側") : (o.Side == "L" ? "left side" : o.Side == "R" ? "right side" : "both sides");
            var head = ja ? $"{region}の{side}で、{o.Parts.Count} つの顔の動きが同時に強く出ています。重なって崩れることがあります。"
                          : $"{o.Parts.Count} face movements are strong at the same time on the {side} of the {region}. They can add up and break the shape.";
            var items = o.Parts.Select(p =>
            {
                var how = usage != null && usage.TryGetValue(p.part, out var u) ? PartUsage.Text(u) : null;
                return how != null ? (ja ? $"{p.part}（{how}）{Math.Round(p.value)}" : $"{p.part} ({how}) {Math.Round(p.value)}") : $"{p.part} {Math.Round(p.value)}";
            });
            return head + "\n" + string.Join(ja ? " と " : " and ", items);
        }
    }

    // Buttons that set the sliders to expressions that break easily.
    public static class Presets
    {
        public sealed class Preset
        {
            public string Ja, En;
            public Dictionary<string, float> Values;
            public string Label => EditorText.Current == EditorText.Language.Japanese ? Ja : En;
        }

        static Dictionary<string, float> V(params (string, float)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

        static Dictionary<string, float> K(params (string, float)[] kv) => kv.ToDictionary(x => InputDef.TrackPrefix + x.Item1, x => x.Item2);

        public static readonly Preset[] All =
        {
            new Preset { Ja = "ふつうの顔", En = "Neutral", Values = K(("EyeClosedLeft", 0), ("EyeClosedRight", 0)) },
            new Preset { Ja = "笑って目を閉じる", En = "Smile with eyes closed", Values = K(("EyeClosedLeft", 1), ("EyeClosedRight", 1), ("MouthCornerPullLeft", 1), ("MouthCornerPullRight", 1), ("CheekSquintLeft", 1), ("CheekSquintRight", 1)) },
            new Preset { Ja = "口を開けて笑う", En = "Smile with mouth open", Values = K(("MouthCornerPullLeft", 1), ("MouthCornerPullRight", 1), ("JawOpen", 1)) },
            new Preset { Ja = "口を大きく開ける", En = "Open mouth wide", Values = K(("JawOpen", 1), ("MouthStretchLeft", 1), ("MouthStretchRight", 1)) },
            new Preset { Ja = "目を見開く", En = "Eyes wide open", Values = K(("EyeWideLeft", 1), ("EyeWideRight", 1), ("BrowOuterUpLeft", 1), ("BrowOuterUpRight", 1)) },
            new Preset { Ja = "横を見る", En = "Look sideways", Values = K(("EyeLookOutLeft", 1), ("EyeLookInRight", 1)) },
        };

        // The presets that set at least one tracking key of the settings, with the values for those keys only.
        public static List<(Preset preset, Dictionary<string, float> values)> For(TableModel m)
        {
            var names = new HashSet<string>(m.Inputs.Where(i => i.IsTrack).Select(i => i.Name));
            return All.Select(p => (p, p.Values.Where(kv => names.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)))
                .Where(x => x.Item2.Count > 0).ToList();
        }
    }

    // Device lists in Templates/Devices/*.jsonc: the values a device is said to send (v2/<name>), or the tracking
    // keys of the Face Tracking section it fills (a key's name, with or without v2/), with their ranges.
    public static class Devices
    {
        public const string Folder = Paths.Templates + "/Devices";

        public sealed class Device
        {
            public string Id, Name;
            public List<(string key, float min, float max)> Keys = new List<(string, float, float)>();

            // The listed entry of a value or key; "v2/EyeLidLeft" and "EyeLidLeft" are the same name.
            public (float lo, float hi)? Listed(string key)
            {
                var n = NameOf(key);
                foreach (var k in Keys) if (NameOf(k.key) == n) return (k.min, k.max);
                return null;
            }

            // The range of an input's value with this device, or null when the device does not send it (design.md
            // "Preview"). An input with its built-in expression and listed in the table uses the listed range;
            // otherwise the range comes from the expression with the device's key ranges, other keys at 0.
            public (float lo, float hi)? Range(string key, string expression = null)
            {
                var builtIn = NativeInputs.Expression(key);
                var e = expression ?? builtIn;
                if (e == builtIn && Listed(key) is (float, float) listed) return listed;
                if (e == null) return null;
                FtExpression.Expr x;
                try { x = FtExpression.Parse(e); }
                catch (FtExpression.ParseException) { return null; }
                (float lo, float hi)? Of(string n) => FtExpression.IsKey(n) ? Listed(n) : null;
                if (!x.Names.Any(n => Of(n) != null)) return null;
                return x.Range(Of);
            }

            public bool Sends(string key, string expression = null) => Range(key, expression) != null;
            public bool Has(string key) => Sends(key);

            // Names of the values to offer as inputs: the listed ones, then the built-in values the device
            // sends through its keys (for a list of keys only).
            public List<string> Values()
            {
                var names = Keys.Select(k => NameOf(k.key)).Distinct().ToList();
                names.AddRange(NativeInputs.FormulaNames.Where(n => !names.Contains(n) && Sends("v2/" + n)));
                return names;
            }

            // Whether an input of the model uses the listed value or key: by its key, or in its expression.
            public static bool UsedBy(TableModel m, string key)
            {
                var n = NameOf(key);
                return m.Inputs.Any(i => !i.IsTrack && (NameOf(i.Key) == n || NativeInputs.NamesIn(i.Expression).Contains(n)));
            }
        }

        public static List<Device> Load()
        {
            var list = new List<Device>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var path in Directory.GetFiles(Folder, "*.jsonc").OrderBy(p => p, StringComparer.Ordinal))
            {
                var o = TableLoader.Read(path);
                var d = new Device { Id = Path.GetFileNameWithoutExtension(path), Name = (string)o["name"] ?? Path.GetFileNameWithoutExtension(path) };
                foreach (var k in (o["keys"] as JArray ?? new JArray()).OfType<JObject>())
                    d.Keys.Add(((string)k["key"], k["min"]?.Value<float>() ?? 0, k["max"]?.Value<float>() ?? 1));
                list.Add(d);
            }
            return list;
        }

        // The range of an input's value with a device, or null when the device does not send it. A tracking key
        // ("key:<key>") is the key's listed range.
        public static (float lo, float hi)? InputRange(TableModel m, InputDef i, Device d) =>
            i.IsTrack ? d.Listed(i.Key) ?? (d.Range(i.Key) is (float, float) r ? r : ((float, float)?)null) : d.Range(i.Key, i.Expression);

        // The range of the values the transitions compare an input with (0 to 1 at least).
        public static (float lo, float hi) ThresholdRange(TableModel m, string input) => FaceEval.Range(m, input);

        // A preview slider with a device: the thresholds' range within the device's range. clipped: some thresholds are
        // outside what the device sends, so they cannot be reached with it.
        public static (bool sent, float lo, float hi, bool clipped, (float lo, float hi) device) Slider(TableModel m, InputDef i, Device d, (float lo, float hi) thresholds)
        {
            var r = InputRange(m, i, d);
            if (r == null) return (false, thresholds.lo, thresholds.hi, false, (0, 0));
            var dr = r.Value;
            var lo = Math.Max(thresholds.lo, dr.lo);
            var hi = Math.Min(thresholds.hi, dr.hi);
            var clipped = thresholds.lo < dr.lo - 1e-4f || thresholds.hi > dr.hi + 1e-4f;
            if (lo >= hi) { lo = dr.lo; hi = dr.hi; }
            if (lo >= hi) hi = lo + 0.01f;
            return (true, lo, hi, clipped, dr);
        }

        // The slider name of a device key: "v2/BrowInnerUpLeft" -> "BrowInnerUpLeft".
        public static string NameOf(string key) => key.StartsWith("v2/") ? key.Substring(3) : key;
    }
}
