using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // What moves each face movement: the inputs of the 1D and 2D nodes above it, and the switch states it is under.
    public static class PartUsage
    {
        public sealed class Usage
        {
            public List<string> Inputs = new List<string>();
            public List<(string sw, List<string> states)> When = new List<(string, List<string>)>();
        }

        public static Dictionary<string, Usage> Of(TableModel m)
        {
            var occurrences = new Dictionary<string, List<(List<string> inputs, List<(string sw, string st)> path)>>(StringComparer.Ordinal);
            void Walk(Node n, List<string> inputs, List<(string, string)> path)
            {
                switch (n)
                {
                    case PoseNode p:
                        foreach (var part in p.Parts.Keys)
                        {
                            if (!occurrences.TryGetValue(part, out var list)) occurrences[part] = list = new List<(List<string>, List<(string, string)>)>();
                            list.Add((inputs.ToList(), path.ToList()));
                        }
                        break;
                    case Blend1DNode b:
                        foreach (var c in b.Children) Walk(c.node, inputs.Append(b.Input).ToList(), path);
                        break;
                    case Blend2DNode b:
                        foreach (var c in b.Children) Walk(c.node, inputs.Append(b.InputX).Append(b.InputY).ToList(), path);
                        break;
                    case SwitchNode s:
                        foreach (var c in s.Cases) Walk(c.node, inputs, path.Append((s.Switch, c.state)).ToList());
                        break;
                }
            }
            foreach (var t in m.Trees) Walk(t.Root, new List<string>(), new List<(string, string)>());

            var result = new Dictionary<string, Usage>(StringComparer.Ordinal);
            foreach (var kv in occurrences)
            {
                var u = new Usage();
                foreach (var occ in kv.Value)
                    foreach (var i in occ.inputs)
                        if (!u.Inputs.Contains(i)) u.Inputs.Add(i);
                var switches = kv.Value.SelectMany(o => o.path.Select(p => p.sw)).Distinct().ToList();
                foreach (var sw in switches)
                {
                    // A condition only when every occurrence is under this switch and not all of its states appear.
                    if (kv.Value.Any(o => o.path.All(p => p.sw != sw))) continue;
                    var states = kv.Value.SelectMany(o => o.path.Where(p => p.sw == sw).Select(p => p.st)).Distinct().ToList();
                    var all = m.Switch(sw)?.States ?? new List<string>();
                    if (all.All(states.Contains)) continue;
                    u.When.Add((sw, all.Where(states.Contains).ToList()));
                }
                result[kv.Key] = u;
            }
            return result;
        }

        // "EyeLidLeft で動く（EyelidSmileLeft が smile のとき）" / "Moves with EyeLidLeft (when EyelidSmileLeft is smile)"
        public static string Text(Usage u)
        {
            if (u == null || u.Inputs.Count == 0) return null;
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var s = ja ? string.Join("・", u.Inputs) + " で動く" : "Moves with " + string.Join(", ", u.Inputs);
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

        static string SideOf(string part) => part.EndsWith("_L") ? "L" : part.EndsWith("_R") ? "R" : "B";
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

        public static readonly Preset[] All =
        {
            new Preset { Ja = "ふつうの顔", En = "Neutral", Values = V(("EyeLidLeft", 0.75f), ("EyeLidRight", 0.75f)) },
            new Preset { Ja = "笑って目を閉じる", En = "Smile with eyes closed", Values = V(("EyeLidLeft", 0), ("EyeLidRight", 0), ("SmileFrown", 1), ("SmileSad", 1)) },
            new Preset { Ja = "口を開けて笑う", En = "Smile with mouth open", Values = V(("EyeLidLeft", 0.75f), ("EyeLidRight", 0.75f), ("SmileFrown", 1), ("SmileSad", 1), ("JawOpen", 1), ("MouthOpen", 0.5f)) },
            new Preset { Ja = "口を大きく開ける", En = "Open mouth wide", Values = V(("EyeLidLeft", 0.75f), ("EyeLidRight", 0.75f), ("JawOpen", 1)) },
            new Preset { Ja = "目を見開く", En = "Eyes wide open", Values = V(("EyeLidLeft", 1), ("EyeLidRight", 1)) },
            new Preset { Ja = "横を見る", En = "Look sideways", Values = V(("EyeLidLeft", 0.75f), ("EyeLidRight", 0.75f), ("EyeLeftX", 1), ("EyeRightX", 1)) },
        };

        // The presets that set at least one input of the settings, with the values for those inputs only.
        public static List<(Preset preset, Dictionary<string, float> values)> For(TableModel m)
        {
            var names = new HashSet<string>(m.Inputs.Where(i => !i.IsDerived).Select(i => i.Name));
            return All.Select(p => (p, p.Values.Where(kv => names.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)))
                .Where(x => x.Item2.Count > 0).ToList();
        }
    }

    // Device lists in Templates/Devices/*.jsonc: the values a device is said to send.
    public static class Devices
    {
        public const string Folder = "Assets/Kmnk/FaceTrackingGenerator/Templates/Devices";

        public sealed class Device
        {
            public string Id, Name;
            public List<(string key, float min, float max)> Keys = new List<(string, float, float)>();
            public bool Has(string key) => Keys.Any(k => k.key == key);
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

        // The slider name of a device key: "v2/BrowInnerUpLeft" -> "BrowInnerUpLeft".
        public static string NameOf(string key) => key.StartsWith("v2/") ? key.Substring(3) : key;
    }
}
