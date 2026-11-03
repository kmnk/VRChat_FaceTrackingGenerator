using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Renaming a face movement (part) in both tables: the avatar table's "parts" and "missingParts", and every pose of
    // the expression table. A left/right pair ("x_L" and "x_R") can be renamed together; then "x_{s}" in left/right
    // trees follows. The blendshapes and the generated weights do not change.
    public static class PartRename
    {
        public enum Problem { None, Form, Used, NeedsSide, PairWritten }

        static string Base(string name) => name.EndsWith("_L") || name.EndsWith("_R") ? name.Substring(0, name.Length - 2) : null;
        static string Side(string name) => name.EndsWith("_L") ? "_L" : name.EndsWith("_R") ? "_R" : null;

        // The other side of a pair, when the table has it: "eye.close_L" -> "eye.close_R".
        public static string Partner(IEnumerable<string> allNames, string name)
        {
            var b = Base(name);
            if (b == null) return null;
            var other = b + (Side(name) == "_L" ? "_R" : "_L");
            return allNames.Contains(other) ? other : null;
        }

        static readonly Regex PairForm = new Regex(@"^(.*)_\{(\w+)\}$");

        // Part names written in the expression table's poses (with "_{s}" forms as written).
        static IEnumerable<JObject> Poses(JToken t)
        {
            if (t is JObject o)
            {
                if (o.Properties().Any(p => p.Name.Contains("."))) yield return o;
                foreach (var p in o.Properties()) foreach (var x in Poses(p.Value)) yield return x;
            }
            else if (t is JArray a) foreach (var x in a) foreach (var y in Poses(x)) yield return y;
        }

        // Is the part used in a left/right tree as "<base>_{var}"?
        static bool WrittenAsPair(JObject expression, string name)
        {
            var b = Base(name);
            if (b == null) return false;
            return Poses(expression?["trees"]).Any(o => o.Properties().Any(p => { var m = PairForm.Match(p.Name); return m.Success && m.Groups[1].Value == b; }));
        }

        // The old -> new names this rename writes (the pair's partner too when "together").
        public static Dictionary<string, string> Map(IEnumerable<string> allNames, string from, string to, bool together)
        {
            var map = new Dictionary<string, string> { [from] = to };
            var partner = together ? Partner(allNames, from) : null;
            if (partner != null && Side(to) != null)
                map[partner] = Base(to) + Side(partner);
            return map;
        }

        public static Problem Check(JObject avatar, JObject expression, string from, string to, bool together, ICollection<string> extraNames = null)
        {
            var all = Tuning.AllPartNames(avatar, expression).Concat(extraNames ?? new string[0]).ToList();
            var partner = together ? Partner(all, from) : null;
            if (partner != null && Side(to) == null) return Problem.NeedsSide;
            if (partner == null && WrittenAsPair(expression, from)) return Problem.PairWritten;
            var map = Map(all, from, to, together);
            var others = all.Where(n => !map.ContainsKey(n)).ToList();
            foreach (var n in map.Values)
            {
                // The name rule has no capitals, so a side's "_L" / "_R" is checked as the name before it.
                var p = Tuning.CheckName(Base(n) ?? n, false, new string[0]);
                if (p == Tuning.NameProblem.Form) return Problem.Form;
                if (others.Contains(n) || others.Contains((Base(n) ?? n) + "_{s}")) return Problem.Used;
            }
            if (map.Values.Distinct().Count() != map.Count) return Problem.Used;
            return Problem.None;
        }

        // Renames in both tables. Returns the map of names it wrote, or null when Check finds a problem.
        public static Dictionary<string, string> Apply(JObject avatar, JObject expression, string from, string to, bool together, ICollection<string> extraNames = null)
        {
            if (Check(avatar, expression, from, to, together, extraNames) != Problem.None) return null;
            var all = Tuning.AllPartNames(avatar, expression).Concat(extraNames ?? new string[0]).ToList();
            var map = Map(all, from, to, together);
            var pairMap = new Dictionary<string, string>();
            if (map.Count == 2) pairMap[Base(from)] = Base(to);

            string New(string key)
            {
                if (map.TryGetValue(key, out var n)) return n;
                var m = PairForm.Match(key);
                if (m.Success && pairMap.TryGetValue(m.Groups[1].Value, out var b)) return b + "_{" + m.Groups[2].Value + "}";
                return null;
            }
            void RenameKeys(JObject o)
            {
                foreach (var p in o.Properties().ToList())
                {
                    var n = New(p.Name);
                    if (n == null || n == p.Name) continue;
                    p.AddBeforeSelf(new JProperty(n, p.Value));
                    p.Remove();
                }
            }
            if (avatar["parts"] is JObject parts) RenameKeys(parts);
            if (avatar["missingParts"] is JArray missing)
                for (int i = 0; i < missing.Count; i++)
                    if (missing[i].Type == JTokenType.String && New((string)missing[i]) is string n) missing[i] = n;
            foreach (var pose in Poses(expression?["trees"]).ToList()) RenameKeys(pose);
            return map;
        }
    }
}
