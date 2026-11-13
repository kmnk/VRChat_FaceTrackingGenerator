using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Face movement (part) names: "region[.state].movement" (spec R3, R4). The region is the first part, the movement the
    // last; the middle is only a mark that the settings editor shows (any part can be used in any state).
    public static class PartNames
    {
        static readonly Regex Rule = new Regex(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+){1,2}$");

        public static bool IsForm(string name) => !string.IsNullOrEmpty(name) && Rule.IsMatch(name);

        public static string Region(string name) => name.Substring(0, name.IndexOf('.'));

        public static string Movement(string name) => name.Substring(name.LastIndexOf('.') + 1);

        // The middle part ("joy" in "eye.joy.tear"), or null for "region.movement".
        public static string Middle(string name)
        {
            var a = name.IndexOf('.');
            var b = name.LastIndexOf('.');
            return a == b ? null : name.Substring(a + 1, b - a - 1);
        }

        public enum Problem { None, Form, Used }

        // A new name: the form, and not used yet. Names that differ only in capitals count as used (spec R4).
        public static Problem Check(string name, IEnumerable<string> used)
        {
            if (!IsForm(name)) return Problem.Form;
            return new HashSet<string>(used, StringComparer.OrdinalIgnoreCase).Contains(name) ? Problem.Used : Problem.None;
        }

        // Every part name of the tables as written: the avatar layer's parts and missingParts, and the expression
        // layer's neutral rows, override rows and fixed amounts.
        public static SortedSet<string> Used(JObject avatar, JObject expression)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (avatar?["parts"] is JObject parts) foreach (var p in parts.Properties()) names.Add(p.Name);
            if (avatar?["missingParts"] is JArray mp) foreach (var x in mp.Where(x => x.Type == JTokenType.String)) names.Add((string)x);
            foreach (var n in InExpression(expression)) names.Add(n);
            return names;
        }

        // The part names the expression layer uses.
        public static IEnumerable<string> InExpression(JObject expression)
        {
            IEnumerable<string> Rows(JToken rows) =>
                rows is JObject o ? o.Properties().Select(p => p.Value.Type == JTokenType.String ? (string)p.Value : (p.Value as JObject)?["part"]?.Value<string>()).Where(x => x != null)
                                  : Enumerable.Empty<string>();
            if (expression?["neutral"] is JObject neutral)
                foreach (var r in neutral.Properties()) foreach (var n in Rows(r.Value)) yield return n;
            if (expression?["stateMachines"] is JObject sms)
                foreach (var sm in sms.Properties())
                    if (sm.Value["states"] is JObject states)
                        foreach (var st in states.Properties().Select(x => x.Value as JObject).Where(x => x != null))
                        {
                            if (st["regions"] is JObject regions)
                                foreach (var r in regions.Properties()) foreach (var n in Rows(r.Value)) yield return n;
                            if (st["fixed"] is JObject fixedParts)
                                foreach (var f in fixedParts.Properties()) yield return f.Name;
                        }
        }
    }

    // Left/right pairs of the input table ("sides" with "{side}" in names), for the settings editor.
    public static class Sides
    {
        public static string Sub(string s, Dictionary<string, string> vars)
        {
            if (vars != null) foreach (var kv in vars) s = s.Replace("{" + kv.Key + "}", kv.Value);
            return s;
        }

        public static List<Dictionary<string, string>> Of(JToken sides)
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

        static readonly Regex Marks = new Regex(@"_?\{[^}]*\}");

        // A name without the side marks: "EyeLid{side}" -> "EyeLid".
        public static string Label(string name) => Marks.Replace(name ?? "", "");

        // A number for the file: whole numbers as integers ("1", not "1.0"), others as they are.
        public static JToken Num(double v) => Math.Abs(v - Math.Round(v)) < 1e-9 ? (JToken)(long)Math.Round(v) : new JValue(v);
    }
}
