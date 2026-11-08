using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // How a node of the expression table is written, in one place. The table loader reads the trees with it, and the
    // settings editor reads and edits the JSON trees with it (the editor keeps left/right pairs as written).
    //   1D      {"1d": input, "children": [[position, content], ...]}
    //   2D      {"2d": [inputX, inputY], "children": [[[x, y], content], ...]}
    //   switch  {"switch": name, "cases": {state: content, ...}}
    //   pose    {"<region>.<name>": amount, ...}   (null is an empty pose)
    // Keys with a dot are face movements (parts). They are an object's own parts: a pose has only those.
    public static class ExpressionSyntax
    {
        public const string Key1D = "1d", Key2D = "2d", KeySwitch = "switch", KeyChildren = "children", KeyCases = "cases";

        public enum Kind { Pose, Blend1D, Blend2D, Switch, Other }

        // Null is an empty pose; an object is a node by its 1d, 2d or switch key (checked in that order), else a pose.
        public static Kind KindOf(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return Kind.Pose;
            if (!(t is JObject o)) return Kind.Other;
            if (o[Key1D] != null) return Kind.Blend1D;
            if (o[Key2D] != null) return Kind.Blend2D;
            if (o[KeySwitch] != null) return Kind.Switch;
            return Kind.Pose;
        }

        public static bool IsBranch(JToken t)
        {
            var k = KindOf(t);
            return k == Kind.Blend1D || k == Kind.Blend2D || k == Kind.Switch;
        }

        public static bool IsPartKey(string key) => key.Contains('.');

        static bool IsNumber(JToken v) => v.Type == JTokenType.Integer || v.Type == JTokenType.Float;

        // The object's own parts with number values, in table order.
        public static IEnumerable<JProperty> PartProperties(JObject o) =>
            o.Properties().Where(p => IsPartKey(p.Name) && IsNumber(p.Value));

        // Part -> amount of the object's own parts (empty for null and for what is not an object).
        public static Dictionary<string, float> OwnParts(JToken t) =>
            t is JObject o ? PartProperties(o).ToDictionary(p => p.Name, p => p.Value.Value<float>()) : new Dictionary<string, float>();

        // Writes an own part. A new part of a node goes before its rows, so that a node reads as its parts, then its rows.
        public static void SetPart(JObject o, string part, JToken value)
        {
            if (o[part] != null) { o[part] = value; return; }
            var rows = o.Property(KeyChildren) ?? o.Property(KeyCases);
            if (IsBranch(o) && rows != null) rows.AddBeforeSelf(new JProperty(part, value));
            else o.Add(part, value);
        }

        // Part -> amount of a pose, or null when the token is a node.
        public static Dictionary<string, float> Pose(JToken t) => KindOf(t) == Kind.Pose ? OwnParts(t) : null;

        // What a node moves with, as written: the input of a 1D node, the two inputs of a 2D node, the switch of a switch node.
        public static string Input1D(JObject o) => (string)o[Key1D];
        public static (string x, string y) Inputs2D(JObject o) => o[Key2D] is JArray a ? ((string)a[0], (string)a[1]) : (null, null);
        public static string SwitchOf(JObject o) => (string)o[KeySwitch];

        // New nodes from their rows.
        public static JObject New1D(string input, IEnumerable<(JToken position, JToken content)> rows) =>
            new JObject { [Key1D] = input, [KeyChildren] = new JArray(rows.Select(r => new JArray(r.position, r.content))) };

        public static JObject New2D(string inputX, string inputY, IEnumerable<(JToken position, JToken content)> rows) =>
            new JObject { [Key2D] = new JArray(inputX, inputY), [KeyChildren] = new JArray(rows.Select(r => new JArray(r.position, r.content))) };

        public static JObject NewSwitch(string name, JObject cases) => new JObject { [KeySwitch] = name, [KeyCases] = cases };

        // A row of a node: a position of a 1D or 2D node ([position, content]) or a state of a switch (state: content).
        public struct Row
        {
            public JToken Position;   // the number (1D) or [x, y] (2D); null for a switch
            public string State;      // the state name; null for 1D and 2D
            public JToken Content;    // a pose, null or a node
        }

        // The rows of a node, in table order. Rows that are not written as [position, content] are left out.
        public static List<Row> Rows(JObject node)
        {
            var rows = new List<Row>();
            var k = KindOf(node);
            if (k == Kind.Blend1D || k == Kind.Blend2D)
            {
                if (node[KeyChildren] is JArray ch)
                    foreach (var c in ChildRows(ch)) rows.Add(new Row { Position = c[0], Content = c[1] });
            }
            else if (k == Kind.Switch && node[KeyCases] is JObject cases)
                foreach (var p in cases.Properties()) rows.Add(new Row { State = p.Name, Content = p.Value });
            return rows;
        }

        static IEnumerable<JArray> ChildRows(JArray children) => children.OfType<JArray>().Where(c => c.Count == 2);

        // Replaces the content of a row (counted as Rows counts).
        public static void SetContent(JObject node, int row, JToken content)
        {
            if (KindOf(node) == Kind.Switch) ((JObject)node[KeyCases]).Properties().ElementAt(row).Value = content;
            else ChildRows((JArray)node[KeyChildren]).ElementAt(row)[1] = content;
        }

        // Every node and pose object under the token (the token too), parents first, each with the switches above it as
        // (switch, state) as written. A null content has no object and is left out.
        public static IEnumerable<(JObject obj, List<(string sw, string state)> when)> Walk(JToken t) =>
            Walk(t, new List<(string, string)>());

        static IEnumerable<(JObject obj, List<(string sw, string state)> when)> Walk(JToken t, List<(string sw, string state)> when)
        {
            if (!(t is JObject o)) yield break;
            yield return (o, when);
            var sw = KindOf(o) == Kind.Switch ? SwitchOf(o) : null;
            foreach (var r in Rows(o))
            {
                var below = sw != null ? when.Append((sw, r.State)).ToList() : when;
                foreach (var x in Walk(r.Content, below)) yield return x;
            }
        }
    }
}
