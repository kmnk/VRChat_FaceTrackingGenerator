using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Recordings of ft-measure (https://github.com/kmnk/ft-measure, JSON Lines, format_version 1) that the avatar table
    // assigns to VR or desktop ("measurements": [{"file", "side"}]). The files of a side are merged from their raw values,
    // and give per tracking key: whether it was sent, the neutral face's value, the largest value of the full faces, and
    // the largest value while reading with a neutral face. The tables are written for the main side; the other side's
    // thresholds (Thresholds) and Blendshapes Sets' strength are computed from them (design.md "集計").
    public sealed class Measurements
    {
        public const string Vr = "vr", Desktop = "desktop";
        public static readonly string[] Sides = { Vr, Desktop };
        public const string Folder = "Measurements";
        public const int FormatVersion = 1;
        // A key moves less than this (1/255 steps: about 5) between the neutral face and the full faces: not usable.
        public const float MinUsable = 0.02f;
        public const string NeutralId = "neutral", ReadNeutralId = "read_neutral", ReadSmileId = "read_smile";

        public sealed class FileEntry
        {
            public string File, Side, Device, Problem;
            public int Keys, BadLines;
        }

        public sealed class KeyStat
        {
            public bool Sent;
            public float Neutral, Max;
            public float? ReadMax;  // null: no reading with a neutral face
        }

        // A recorded face (a pose, or a reading): per key the smallest and largest value over its repetitions.
        public sealed class Item
        {
            public string Id;
            public readonly SortedDictionary<string, (float min, float max)> Keys = new SortedDictionary<string, (float, float)>(StringComparer.Ordinal);
            public (float min, float max) Of(string key) => Keys.TryGetValue(key, out var r) ? r : (0f, 0f);
        }

        public sealed class Side
        {
            public string Name;
            public readonly List<string> Files = new List<string>();
            public readonly SortedDictionary<string, KeyStat> Keys = new SortedDictionary<string, KeyStat>(StringComparer.Ordinal);
            // The recorded faces in the order they were recorded (spec R4, R18a).
            public readonly List<Item> Items = new List<Item>();
            public Item ItemOf(string id) => Items.FirstOrDefault(i => i.Id == id);

            static readonly KeyStat NotSent = new KeyStat();
            public KeyStat Key(string key) => Keys.TryGetValue(key, out var s) ? s : NotSent;

            // The usable range (largest full-face value - neutral value) of a key that was sent, or null.
            public float? Usable(string key)
            {
                var s = Key(key);
                if (!s.Sent) return null;
                var u = s.Max - s.Neutral;
                return u >= MinUsable ? u : (float?)null;
            }
        }

        public readonly List<FileEntry> Files = new List<FileEntry>();
        public Side VrSide, DesktopSide;
        public bool HasBoth => VrSide != null && DesktopSide != null;
        public bool Any => VrSide != null || DesktopSide != null;

        public Side Of(string side) => side == Vr ? VrSide : side == Desktop ? DesktopSide : null;

        public static string Other(string side) => side == Vr ? Desktop : Vr;
        public static string Label(string side) => side == Vr ? "VR" : "Desktop";

        // ---- reading

        sealed class Raw
        {
            public readonly HashSet<string> Sent = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<float>> Neutral = new Dictionary<string, List<float>>(StringComparer.Ordinal);
            public readonly Dictionary<string, float> Max = new Dictionary<string, float>(StringComparer.Ordinal);
            public readonly Dictionary<string, float> ReadMax = new Dictionary<string, float>(StringComparer.Ordinal);
            public bool HasRead;
            public int NeutralReps, Poses;
            public readonly List<string> ItemOrder = new List<string>();
            public readonly Dictionary<string, ItemAcc> Items = new Dictionary<string, ItemAcc>(StringComparer.Ordinal);
        }

        // A face's repetitions, and per key its range and how many repetitions had it (the others had it at 0).
        sealed class ItemAcc
        {
            public int Count;
            public readonly Dictionary<string, (float min, float max, int n)> Keys = new Dictionary<string, (float, float, int)>(StringComparer.Ordinal);

            public void Add(string key, float min, float max, int n) =>
                Keys[key] = Keys.TryGetValue(key, out var r) ? (Math.Min(r.min, min), Math.Max(r.max, max), r.n + n) : (min, max, n);
        }

        // entries: the avatar table's "measurements" (or null). dir: the avatar table's folder; files are relative to it.
        // Problems go to warnings; nothing here throws for a recording (spec R5).
        public static Measurements Load(string dir, JToken entries, List<string> warnings)
        {
            var result = new Measurements();
            if (entries == null || entries.Type == JTokenType.Null) return result;
            if (!(entries is JArray list))
            {
                warnings.Add("avatar table: 'measurements' must be a list of {\"file\", \"side\"}; recordings are not used");
                return result;
            }
            var raws = new Dictionary<string, Raw>();
            foreach (var t in list)
            {
                var o = t as JObject;
                var file = Text(o?["file"]);
                var side = Text(o?["side"]);
                if (file == null || !Sides.Contains(side))
                {
                    warnings.Add($"avatar table: '{t.Path}' needs \"file\" and \"side\" (\"vr\" or \"desktop\"); skipped");
                    continue;
                }
                var entry = new FileEntry { File = file, Side = side };
                result.Files.Add(entry);
                if (!raws.TryGetValue(side, out var raw)) raw = new Raw();
                int badLines = 0;
                // A recording that has unexpected values must not stop the tables from loading (spec R5).
                try { entry.Problem = Read(Path.Combine(dir ?? "", file), raw, entry, out badLines); }
                catch (Exception e) { entry.Problem = "cannot be read: " + e.Message; }
                entry.BadLines = badLines;
                if (badLines > 0) warnings.Add($"recording {file}: {badLines} lines could not be read and were skipped");
                if (entry.Problem != null) { warnings.Add($"recording {file}: {entry.Problem}; not used"); continue; }
                raws[side] = raw;
            }
            foreach (var kv in raws.Where(x => x.Value.NeutralReps == 0))
                warnings.Add($"recordings for {Label(kv.Key)}: no neutral face; neutral values are 0");
            foreach (var kv in raws)
            {
                var s = Build(kv.Key, kv.Value);
                s.Files.AddRange(result.Files.Where(f => f.Side == kv.Key && f.Problem == null).Select(f => f.File));
                if (kv.Key == Vr) result.VrSide = s; else result.DesktopSide = s;
            }
            return result;
        }

        // Reads one file into raw (only when it can be used). Returns the problem, or null.
        static string Read(string path, Raw raw, FileEntry entry, out int badLines)
        {
            badLines = 0;
            if (!File.Exists(path)) return "file not found";
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { return "cannot be read: " + e.Message; }
            JObject head = null;
            try { head = lines.Length > 0 ? JObject.Parse(lines[0]) : null; }
            catch (JsonException) { }
            if (head == null || Text(head["type"]) != "session") return "the first line is not an ft-measure session";
            if (Whole(head["format_version"]) != FormatVersion)
                return $"format_version {head["format_version"]} is not {FormatVersion}";
            entry.Device = Text(head["device"]);
            // Read into a separate raw first, so that a file that turns out unusable leaves the side as it was.
            var mine = new Raw();
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject o;
                try { o = JObject.Parse(line); }
                catch (JsonException) { badLines++; continue; }
                var type = Text(o["type"]);
                var id = Text(o["id"]);
                if (!(o["values"] is JObject values) || id == null || (type != "pose" && type != "sample")) { badLines++; continue; }
                var vals = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var p in values.Properties())
                    if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer) vals[p.Name] = p.Value.Value<float>();
                foreach (var k in vals.Keys) mine.Sent.Add(k);
                if (!mine.Items.TryGetValue(id, out var acc)) { mine.Items[id] = acc = new ItemAcc(); mine.ItemOrder.Add(id); }
                acc.Count++;
                foreach (var kv in vals) acc.Add(kv.Key, kv.Value, kv.Value, 1);
                if (type == "sample")
                {
                    if (id != ReadNeutralId) continue;
                    mine.HasRead = true;
                    foreach (var kv in vals) mine.ReadMax[kv.Key] = Math.Max(mine.ReadMax.TryGetValue(kv.Key, out var m) ? m : 0, kv.Value);
                    continue;
                }
                mine.Poses++;
                if (id == NeutralId)
                {
                    mine.NeutralReps++;
                    foreach (var kv in vals)
                    {
                        if (!mine.Neutral.TryGetValue(kv.Key, out var l)) mine.Neutral[kv.Key] = l = new List<float>();
                        l.Add(kv.Value);
                    }
                }
                else
                    foreach (var kv in vals) mine.Max[kv.Key] = Math.Max(mine.Max.TryGetValue(kv.Key, out var m) ? m : 0, kv.Value);
            }
            if (mine.Poses == 0 && !mine.HasRead) return "no recorded faces";
            entry.Keys = mine.Sent.Count;
            Merge(raw, mine);
            return null;
        }

        // A recording's value only when it has the expected type (null otherwise), so that no value throws.
        public static string Text(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        static int? Whole(JToken t) => t != null && t.Type == JTokenType.Integer && t.Value<long>() >= int.MinValue && t.Value<long>() <= int.MaxValue ? (int)t.Value<long>() : (int?)null;

        static void Merge(Raw into, Raw from)
        {
            into.Sent.UnionWith(from.Sent);
            // A neutral repetition without a key has it at 0: pad the lists to the repetitions read so far.
            foreach (var k in into.Neutral.Keys.Union(from.Neutral.Keys).ToList())
            {
                if (!into.Neutral.TryGetValue(k, out var a)) into.Neutral[k] = a = Enumerable.Repeat(0f, into.NeutralReps).ToList();
                var b = from.Neutral.TryGetValue(k, out var x) ? x : new List<float>();
                a.AddRange(b);
                a.AddRange(Enumerable.Repeat(0f, from.NeutralReps - b.Count));
            }
            into.NeutralReps += from.NeutralReps;
            foreach (var kv in from.Max) into.Max[kv.Key] = Math.Max(into.Max.TryGetValue(kv.Key, out var m) ? m : 0, kv.Value);
            foreach (var kv in from.ReadMax) into.ReadMax[kv.Key] = Math.Max(into.ReadMax.TryGetValue(kv.Key, out var m) ? m : 0, kv.Value);
            into.HasRead |= from.HasRead;
            into.Poses += from.Poses;
            foreach (var id in from.ItemOrder)
            {
                var a = from.Items[id];
                if (!into.Items.TryGetValue(id, out var b)) { into.Items[id] = b = new ItemAcc(); into.ItemOrder.Add(id); }
                b.Count += a.Count;
                foreach (var kv in a.Keys) b.Add(kv.Key, kv.Value.min, kv.Value.max, kv.Value.n);
            }
        }

        static Side Build(string name, Raw raw)
        {
            var s = new Side { Name = name };
            foreach (var k in raw.Sent.OrderBy(NativeKeys.Index).ThenBy(x => x, StringComparer.Ordinal))
            {
                var n = raw.Neutral.TryGetValue(k, out var l) ? l.Concat(Enumerable.Repeat(0f, raw.NeutralReps - l.Count)).ToList() : new List<float>();
                s.Keys[k] = new KeyStat
                {
                    Sent = true,
                    Neutral = Median(n),
                    Max = raw.Max.TryGetValue(k, out var m) ? m : 0,
                    ReadMax = raw.HasRead ? (raw.ReadMax.TryGetValue(k, out var r) ? r : 0) : (float?)null,
                };
            }
            foreach (var id in raw.ItemOrder)
            {
                var acc = raw.Items[id];
                var item = new Item { Id = id };
                // A key missing in some repetitions was 0 there.
                foreach (var kv in acc.Keys)
                    item.Keys[kv.Key] = (kv.Value.n < acc.Count ? Math.Min(0, kv.Value.min) : kv.Value.min, Math.Max(kv.Value.n < acc.Count ? 0 : kv.Value.max, kv.Value.max));
                s.Items.Add(item);
            }
            return s;
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0;
            var s = v.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }
    }
}
