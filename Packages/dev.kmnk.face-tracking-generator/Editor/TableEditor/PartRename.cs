using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Renaming a face movement (part) in both tables: the avatar table's "parts" and "missingParts", and the expression
    // table's neutral rows, override rows (values) and fixed amounts (keys). The blendshapes and the generated weights
    // do not change.
    public static class PartRename
    {
        public enum Problem { None, Form, Used, Region }

        // The old -> new names this rename writes. (The "together" of left/right pairs was removed in 0.12.0.)
        public static Dictionary<string, string> Map(IEnumerable<string> allNames, string from, string to, bool together = false) =>
            new Dictionary<string, string> { [from] = to };

        public static Problem Check(JObject avatar, JObject expression, string from, string to, bool together = false, ICollection<string> extraNames = null)
        {
            var others = PartNames.Used(avatar, expression).Concat(extraNames ?? new string[0]).Where(n => n != from);
            switch (PartNames.Check(to, others))
            {
                case PartNames.Problem.Form: return Problem.Form;
                case PartNames.Problem.Used: return Problem.Used;
            }
            // A part in rows of neutral or an override must stay in that region (the rows are written per region).
            if (PartNames.IsForm(from) && PartNames.Region(to) != PartNames.Region(from) && InRows(expression, from)) return Problem.Region;
            return Problem.None;
        }

        // Whether a part is the part of a row of neutral or of a state's override.
        static bool InRows(JObject expression, string part)
        {
            bool In(JObject rows) => rows != null && rows.Properties().Any(p =>
                p.Value.Type == JTokenType.String && (string)p.Value == part || p.Value is JObject ro && (string)ro["part"] == part);
            if (expression?["neutral"] is JObject neutral && neutral.Properties().Any(r => In(r.Value as JObject))) return true;
            if (expression?["stateMachines"] is JObject sms)
                foreach (var st in sms.Properties().SelectMany(sm => (sm.Value["states"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()).Select(x => x.Value as JObject).Where(x => x != null))
                    if (st["regions"] is JObject regions && regions.Properties().Any(r => In(r.Value as JObject))) return true;
            return false;
        }

        // Renames in both tables. Returns the map of names it wrote, or null when Check finds a problem.
        public static Dictionary<string, string> Apply(JObject avatar, JObject expression, string from, string to, bool together = false, ICollection<string> extraNames = null)
        {
            if (Check(avatar, expression, from, to, together, extraNames) != Problem.None) return null;
            void RenameKey(JObject o)
            {
                var p = o?.Property(from);
                if (p == null) return;
                p.AddBeforeSelf(new JProperty(to, p.Value));
                p.Remove();
            }
            void RenameValues(JObject rows)
            {
                if (rows == null) return;
                foreach (var p in rows.Properties())
                {
                    if (p.Value.Type == JTokenType.String && (string)p.Value == from) p.Value = to;
                    else if (p.Value is JObject ro && (string)ro["part"] == from) ro["part"] = to;
                }
            }
            RenameKey(avatar["parts"] as JObject);
            if (avatar["missingParts"] is JArray missing)
                for (int i = 0; i < missing.Count; i++)
                    if (missing[i].Type == JTokenType.String && (string)missing[i] == from) missing[i] = to;
            if (expression?["neutral"] is JObject neutral)
                foreach (var r in neutral.Properties()) RenameValues(r.Value as JObject);
            if (expression?["stateMachines"] is JObject sms)
                foreach (var st in sms.Properties().SelectMany(sm => (sm.Value["states"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()).Select(x => x.Value as JObject).Where(x => x != null))
                {
                    if (st["regions"] is JObject regions) foreach (var r in regions.Properties()) RenameValues(r.Value as JObject);
                    RenameKey(st["fixed"] as JObject);
                }
            return Map(null, from, to);
        }
    }
}
