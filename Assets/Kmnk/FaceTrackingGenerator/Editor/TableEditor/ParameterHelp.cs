using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // What each face tracking value is (Templates/parameters.jsonc), for the tooltips of the settings editor.
    // Entries are keyed by the name after "v2/", with "{side}" for a left/right pair.
    public static class ParameterHelp
    {
        public const string FilePath = "Assets/Kmnk/FaceTrackingGenerator/Templates/parameters.jsonc";
        static Dictionary<string, (string en, string ja)> entries;

        static Dictionary<string, (string en, string ja)> Entries
        {
            get
            {
                if (entries != null) return entries;
                entries = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
                if (!File.Exists(FilePath)) return entries;
                foreach (var p in (TableLoader.Read(FilePath)["parameters"] as JObject ?? new JObject()).Properties())
                    entries[p.Name] = ((string)p.Value["en"], (string)p.Value["ja"]);
                return entries;
            }
        }

        static string Name(string key) => key == null ? null : key.StartsWith("v2/") ? key.Substring(3) : key;

        // The description of a value ("v2/EyeLidLeft", "EyeLidLeft" or a pair "EyeLid{side}"), in the help language, or null
        // (also when the help texts are off).
        public static string Describe(string key)
        {
            var name = Name(key);
            if (name == null || EditorText.Current == EditorText.Language.Off) return null;
            var ja = EditorText.Current == EditorText.Language.Japanese;
            string Pick((string en, string ja) e, string sideEn, string sideJa) =>
                (ja ? e.ja : e.en)?.Replace("{side}", ja ? sideJa : sideEn);
            if (Entries.TryGetValue(name, out var exact))
                return Pick(exact, "left/right", "左・右");  // a pair as one row, or a value without sides
            foreach (var (side, en, jaWord) in new[] { ("Left", "left", "左"), ("Right", "right", "右") })
                if (name.Contains(side) && Entries.TryGetValue(name.Replace(side, "{side}"), out var e))
                    return Pick(e, en, jaWord);
            return null;
        }

        // The tooltip of an input: the description, then the v2 keys; null when there is nothing to show.
        public static string Tooltip(IEnumerable<string> keys, string describeKey = null)
        {
            var list = keys?.Where(k => !string.IsNullOrEmpty(k)).ToList() ?? new List<string>();
            var text = Describe(describeKey ?? list.FirstOrDefault());
            var keysLine = string.Join(EditorText.Current == EditorText.Language.Japanese ? "・" : ", ", list);
            if (text == null) return keysLine == "" ? null : keysLine;
            return keysLine == "" ? text : text + "\n" + keysLine;
        }

        // The tooltip of an input of the model: an average input says what it averages, then the first source's description.
        public static string Tooltip(TableModel m, InputDef input)
        {
            if (input == null) return null;
            if (!input.IsDerived) return Tooltip(new[] { input.Key });
            var ja = EditorText.Current == EditorText.Language.Japanese;
            var head = ja ? string.Join("・", input.Average) + " の平均。" : "The average of " + string.Join(" and ", input.Average) + ". ";
            var first = m.Input(input.Average.FirstOrDefault());
            var key = first?.Key;
            if (key != null && key.Contains("Left") && input.Average.Count == 2) key = key.Replace("Left", "{side}");  // a left/right average
            var rest = key != null ? Describe(key) : null;
            return EditorText.Current == EditorText.Language.Off ? null : head + (rest ?? "");
        }
    }
}
