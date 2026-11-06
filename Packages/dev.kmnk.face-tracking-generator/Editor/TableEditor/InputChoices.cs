using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The Inputs tab without drawing: the inputs of the input table and the values a device sends, as rows to check or
    // uncheck, with the expression each input is made from. Works on the JSON input table, so a left/right pair stays one row.
    public static class InputChoices
    {
        public sealed class Row
        {
            public string Name;                 // as written in the table, or the name to write ("BrowInnerUp{side}")
            public string Display;              // "BrowInnerUp (left and right)"
            public bool InTable, Added, Sided, NotSent;
            public List<string> Expanded = new List<string>();  // input names after the sides
            public List<string> Keys = new List<string>();      // v2 keys after the sides
            public List<string> Expressions = new List<string>();  // per side: the table's or the built-in expression (null: none)
            public bool ExpressionFromTable;
            public SortedSet<string> UsedBy = new SortedSet<string>(StringComparer.Ordinal);   // expressions
            public SortedSet<string> Through = new SortedSet<string>(StringComparer.Ordinal);  // average inputs on the way
            public bool Locked => UsedBy.Count > 0;
        }

        public sealed class Result
        {
            public List<Row> InTable = new List<Row>();
            public List<Row> NotInTable = new List<Row>();
        }

        static string Sub(string s, Dictionary<string, string> vars) => Tuning.Sub(s, vars);

        // Rows for the tables. device is null for "Settings"; saved is the input table as last saved (for "added").
        public static Result Rows(JObject input, JObject expression, TableModel model, Devices.Device device, JObject saved)
        {
            var r = new Result();
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
                        ExpressionFromTable = o["expression"] != null,
                    };
                    row.Display = Tuning.Label(p.Name) + (row.Sided ? " (left and right)" : "");
                    foreach (var v in vars)
                    {
                        row.Expanded.Add(Sub(p.Name, v));
                        if (o["key"] != null) row.Keys.Add(Sub((string)o["key"], v));
                        row.Expressions.Add(o["expression"] != null ? Sub((string)o["expression"], v) : o["key"] != null ? NativeInputs.Expression(Sub((string)o["key"], v)) : null);
                    }
                    foreach (var n in row.Expanded)
                        if (used.TryGetValue(n, out var u))
                        {
                            row.UsedBy.UnionWith(u.trees);
                            row.Through.UnionWith(u.through);
                        }
                    row.NotSent = device != null && row.Keys.Count > 0 && row.Keys.Where((k, i) => !device.Sends(k, row.Expressions[i])).Any();
                    foreach (var k in row.Keys) tableKeys.Add(Devices.NameOf(k));
                    r.InTable.Add(row);
                }
            if (device != null)
            {
                var names = device.Values().Where(n => !tableKeys.Contains(n)).ToList();
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
                            Expanded = { n, right }, Keys = { "v2/" + n, "v2/" + right }, Expressions = { NativeInputs.Expression(n), NativeInputs.Expression(right) } });
                        done.Add(right);
                    }
                    else if (n.Contains("Right") && set.Contains(n.Replace("Right", "Left"))) continue;
                    else r.NotInTable.Add(new Row { Name = n, Display = n, Expanded = { n }, Keys = { "v2/" + n }, Expressions = { NativeInputs.Expression(n) } });
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

        // The device's inputs not in the table that a box can be split on (the expression graph's "Add from <device>"):
        // in a tree with sides every row, in a tree without sides only the rows without sides (as ExpressionGraph.SplitInputs).
        public static List<Row> SplitChoices(Result r, bool sided) => r.NotInTable.Where(x => sided || !x.Sided).ToList();

        // ---- edits (on the input table's JSON)

        // Adds the row's input where it falls in the device's order of keys, so that the order of the table does not
        // depend on the order the inputs were checked in. Without a device, or after the inputs the device does not
        // know, it goes after the last input that is not an average.
        public static void Add(JObject input, Row row, Devices.Device device = null)
        {
            if (!(input["inputs"] is JObject inputs)) input["inputs"] = inputs = new JObject();
            if (inputs[row.Name] != null) return;
            var o = new JObject();
            if (row.Sided) o["sides"] = new JArray("Left", "Right");
            o["key"] = "v2/" + row.Name;
            if (input["smoothing"]?["default"] != null) o["smoothing"] = "default";

            int Rank(IEnumerable<string> keys)
            {
                if (device == null) return -1;
                var ranks = keys.Select(k => device.Keys.FindIndex(x => Devices.NameOf(x.key) == Devices.NameOf(k))).Where(i => i >= 0).ToList();
                return ranks.Count == 0 ? -1 : ranks.Min();
            }
            List<string> KeysOf(JProperty p)
            {
                if (!(p.Value is JObject v) || v["key"] == null) return new List<string>();
                var sides = Tuning.SidesOf(v["sides"]);
                return (sides ?? new List<Dictionary<string, string>> { null }).Select(x => Tuning.Sub((string)v["key"], x)).ToList();
            }
            var props = inputs.Properties().ToList();
            var mine = Rank(row.Keys);
            var at = -1;
            if (mine >= 0)
                at = props.FindIndex(p => p.Value is JObject v && v["average"] == null && Rank(KeysOf(p)) > mine);
            if (at < 0)
            {
                var lastPlain = props.FindLastIndex(p => p.Value is JObject v && v["average"] == null);
                at = lastPlain + 1;
            }
            var added = new JProperty(row.Name, o);
            if (at >= props.Count) inputs.Add(added);
            else props[at].AddBeforeSelf(added);
        }

        public static bool Remove(JObject input, Row row)
        {
            if (row.Locked || !(input["inputs"] is JObject inputs)) return false;
            return inputs.Remove(row.Name);
        }

        // ---- texts

        // The tooltip of a row: what the value is, its v2 keys, and why it cannot be unchecked.
        public static string Tooltip(Row row)
        {
            var describe = row.Keys.FirstOrDefault();
            if (row.Sided && describe != null && describe.Contains("Left")) describe = describe.Replace("Left", "{side}");
            var tip = ParameterHelp.Tooltip(row.Keys, describe);
            if (row.InTable && row.Locked)
            {
                var ja = EditorText.Current == EditorText.Language.Japanese;
                var used = string.Format(EditorText.Get("inputs.usedBy"), string.Join(ja ? "・" : ", ", row.UsedBy));
                tip = (tip == null ? "" : tip + "\n") + string.Format(EditorText.Get("inputs.locked"), used);
            }
            return tip;
        }

        // The expression of a row, with {side} for a left/right pair; null when there is none.
        public static string ExpressionText(Row row)
        {
            var e = row.Expressions.FirstOrDefault();
            if (e == null) return null;
            return row.Sided && row.Expanded.Count == 2 && row.Expressions.Count == 2 && e.Replace("Left", "Right") == row.Expressions[1]
                ? e.Replace("Left", "{side}") : string.Join(" / ", row.Expressions);
        }

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
            var expr = ExpressionText(row);
            parts.Add(expr == null ? EditorText.Get("inputs.noExpression")
                : string.Format(EditorText.Get(row.ExpressionFromTable ? "inputs.expressionTable" : "inputs.expression"), expr));
            return parts.Count == 0 ? null : string.Join(ja ? "　" : "; ", parts);
        }
    }
}
