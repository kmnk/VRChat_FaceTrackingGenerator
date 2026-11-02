using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Writes a table for saving from the table editor. An array or object goes on one line when it fits in
    // Width characters (with its indent and key); otherwise each child goes on its own line, and the same rule
    // applies to the children. Comments are not kept (the table editor does not show them).
    public static class JsonTableWriter
    {
        public const int Width = 120;
        const string Indent = "  ";

        public static string Write(JToken token)
        {
            var sb = new StringBuilder();
            WriteToken(token, sb, 0, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        // used: characters already on the line before the value (indent and "key": ).
        static void WriteToken(JToken t, StringBuilder sb, int depth, int used)
        {
            var oneLine = OneLine(t);
            if (!(t is JContainer c) || !c.HasValues || used + oneLine.Length <= Width)
            {
                sb.Append(oneLine);
                return;
            }
            var pad = string.Concat(Enumerable.Repeat(Indent, depth + 1));
            sb.Append(t is JObject ? "{\n" : "[\n");
            var items = c.Children().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(pad);
                if (items[i] is JProperty p)
                {
                    var key = JsonConvert.ToString(p.Name) + ": ";
                    sb.Append(key);
                    WriteToken(p.Value, sb, depth + 1, pad.Length + key.Length);
                }
                else WriteToken(items[i], sb, depth + 1, pad.Length);
                sb.Append(i + 1 < items.Count ? ",\n" : "\n");
            }
            sb.Append(string.Concat(Enumerable.Repeat(Indent, depth))).Append(t is JObject ? "}" : "]");
        }

        // The value on one line, with ", " and ": " between items.
        public static string OneLine(JToken t)
        {
            switch (t)
            {
                case JObject o:
                    return o.HasValues ? "{" + string.Join(", ", o.Properties().Select(p => JsonConvert.ToString(p.Name) + ": " + OneLine(p.Value))) + "}" : "{}";
                case JArray a:
                    return a.HasValues ? "[" + string.Join(", ", a.Select(OneLine)) + "]" : "[]";
                default:
                    return t.ToString(Formatting.None);
            }
        }
    }
}
