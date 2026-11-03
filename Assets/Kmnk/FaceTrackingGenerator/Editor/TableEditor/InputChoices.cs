using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The Inputs tab without drawing: the inputs of the input table and the values a device sends, as rows to check or
    // uncheck, with the synced bits. Works on the JSON input table, so a left/right pair stays one row.
    public static class InputChoices
    {
        public sealed class Row
        {
            public string Name;                 // as written in the table, or the name to write ("BrowInnerUp{side}")
            public string Display;              // "BrowInnerUp (left and right)"
            public bool InTable, Added, Sided, NotSent;
            public List<string> Expanded = new List<string>();  // input names after the sides
            public List<string> Keys = new List<string>();      // v2 keys after the sides
            public string Sync;                 // "float8" or "none" (rows in the table)
            public int Bits;
            public SortedSet<string> UsedBy = new SortedSet<string>(StringComparer.Ordinal);   // expressions
            public SortedSet<string> Through = new SortedSet<string>(StringComparer.Ordinal);  // average inputs on the way
            public bool Locked => UsedBy.Count > 0;
        }

        public sealed class Result
        {
            public List<Row> InTable = new List<Row>();
            public List<Row> NotInTable = new List<Row>();
            public int SavedBits, AddedBits;
            public int AvatarBits = -1;         // -1: not counted
            public int Total => Math.Max(0, AvatarBits) + SavedBits + AddedBits;
            public bool Over => Total > StaticChecker.MaxSyncedBits;
        }

        static string Sub(string s, Dictionary<string, string> vars) => Tuning.Sub(s, vars);

        // Rows for the tables. device is null for "Settings"; saved is the input table as last saved (for "added").
        public static Result Rows(JObject input, JObject expression, TableModel model, Devices.Device device, JObject saved, int avatarBits)
        {
            var r = new Result { AvatarBits = avatarBits };
            var savedNames = new HashSet<string>((saved?["inputs"] as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
            var used = Usage(expression, model);
            var tableKeys = new HashSet<string>();
            if (input?["inputs"] is JObject inputs)
                foreach (var p in inputs.Properties())
                {
                    if (!(p.Value is JObject o) || o["average"] != null) continue;
                    var sides = Tuning.SidesOf(o["sides"]);
                    var vars = sides ?? new List<Dictionary<string, string>> { null };
                    var row = new Row
                    {
                        Name = p.Name, InTable = true, Added = !savedNames.Contains(p.Name), Sided = sides != null,
                        Sync = (string)o["sync"] ?? "none",
                    };
                    row.Display = Tuning.Label(p.Name) + (row.Sided ? " (left and right)" : "");
                    foreach (var v in vars)
                    {
                        row.Expanded.Add(Sub(p.Name, v));
                        if (o["key"] != null) row.Keys.Add(Sub((string)o["key"], v));
                    }
                    row.Bits = row.Sync == "float8" ? 8 * row.Expanded.Count : 0;
                    foreach (var n in row.Expanded)
                        if (used.TryGetValue(n, out var u))
                        {
                            row.UsedBy.UnionWith(u.trees);
                            row.Through.UnionWith(u.through);
                        }
                    row.NotSent = device != null && row.Keys.Count > 0 && row.Keys.Any(k => !device.Has(k));
                    foreach (var k in row.Keys) tableKeys.Add(k);
                    r.InTable.Add(row);
                    if (row.Added) r.AddedBits += row.Bits; else r.SavedBits += row.Bits;
                }
            if (device != null)
            {
                var names = device.Keys.Select(k => k.key).Where(k => !tableKeys.Contains(k)).Select(Devices.NameOf).ToList();
                var set = new HashSet<string>(names);
                var done = new HashSet<string>();
                foreach (var n in names.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (done.Contains(n)) continue;
                    var right = n.Contains("Left") ? n.Replace("Left", "Right") : null;
                    if (right != null && set.Contains(right))
                    {
                        var name = n.Replace("Left", "{side}");
                        r.NotInTable.Add(new Row { Name = name, Display = Tuning.Label(name) + " (left and right)", Sided = true,
                            Expanded = { n, right }, Keys = { "v2/" + n, "v2/" + right }, Bits = 16 });
                        done.Add(right);
                    }
                    else if (n.Contains("Right") && set.Contains(n.Replace("Right", "Left"))) continue;
                    else r.NotInTable.Add(new Row { Name = n, Display = n, Expanded = { n }, Keys = { "v2/" + n }, Bits = 8 });
                    done.Add(n);
                }
                r.NotInTable.Sort((a, b) => string.CompareOrdinal(a.Display, b.Display));
            }
            return r;
        }

        // Input name -> (expressions that use it, average inputs it is used through). Expressions are the expression
        // table's tree names without side marks; switches count through their transitions' conditions.
        static Dictionary<string, (HashSet<string> trees, HashSet<string> through)> Usage(JObject expression, TableModel m)
        {
            var result = new Dictionary<string, (HashSet<string>, HashSet<string>)>();
            if (m == null) return result;
            var labels = new Dictionary<string, string>();
            foreach (var t in Tuning.Trees(expression))
                foreach (var n in Tuning.Expanded(t, t.Name)) labels[n] = Tuning.Label(t.Name);
            void Add(string input, string tree, string through)
            {
                if (input == null) return;
                if (!result.TryGetValue(input, out var u)) result[input] = u = (new HashSet<string>(), new HashSet<string>());
                u.Item1.Add(tree);
                if (through != null) u.Item2.Add(through);
                var def = m.Input(input);
                if (def != null && def.IsDerived) foreach (var a in def.Average) Add(a, tree, input);
            }
            void Walk(Node node, string tree)
            {
                switch (node)
                {
                    case Blend1DNode b: Add(b.Input, tree, null); foreach (var c in b.Children) Walk(c.node, tree); break;
                    case Blend2DNode b: Add(b.InputX, tree, null); Add(b.InputY, tree, null); foreach (var c in b.Children) Walk(c.node, tree); break;
                    case SwitchNode s:
                        var sw = m.Switch(s.Switch);
                        if (sw != null) foreach (var c in sw.Transitions.SelectMany(x => x.When)) Add(c.Input, tree, null);
                        foreach (var c in s.Cases) Walk(c.node, tree);
                        break;
                }
            }
            foreach (var t in m.Trees) Walk(t.Root, labels.TryGetValue(t.Name, out var l) ? l : Tuning.Label(t.Name));
            return result;
        }

        // ---- edits (on the input table's JSON)

        public static void Add(JObject input, Row row)
        {
            if (!(input["inputs"] is JObject inputs)) input["inputs"] = inputs = new JObject();
            if (inputs[row.Name] != null) return;
            var o = new JObject();
            if (row.Sided) o["sides"] = new JArray("Left", "Right");
            o["key"] = "v2/" + row.Name;
            if (input["smoothing"]?["default"] != null) o["smoothing"] = "default";
            o["sync"] = "float8";
            inputs.Add(row.Name, o);
        }

        public static bool Remove(JObject input, Row row)
        {
            if (row.Locked || !(input["inputs"] is JObject inputs)) return false;
            return inputs.Remove(row.Name);
        }

        public static void SetSync(JObject input, string name, bool synced)
        {
            if (input["inputs"]?[name] is JObject o) o["sync"] = synced ? "float8" : "none";
        }

        // ---- texts

        public static string Note(Row row)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var parts = new List<string>();
            if (row.Locked)
            {
                var s = string.Format(EditorText.Get("inputs.usedBy"), string.Join(ja ? "・" : ", ", row.UsedBy));
                if (row.Through.Count > 0) s += string.Format(EditorText.Get("inputs.through"), string.Join(ja ? "・" : ", ", row.Through.Select(Tuning.Label)));
                parts.Add(row.Added ? EditorText.Get("inputs.addedPrefix") + s : s);
            }
            else if (row.Added) parts.Add(EditorText.Get("inputs.added"));
            if (row.NotSent) parts.Add(EditorText.Get("inputs.notSent"));
            return parts.Count == 0 ? null : string.Join(ja ? "　" : " ", parts);
        }
    }
}
