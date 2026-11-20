using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Small text edits of the tables that keep comments and the other lines.
    public static class TableWriter
    {
        static readonly string[] AfkKeys = { "actionIntro", "actionLoop", "actionOutro", "fxIntro", "fxLoop", "fxOutro" };

        public static void SetAfkClips(string avatarTable, AnimationClip[] clips)
        {
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < 6; i++)
            {
                var c = clips[i];
                sb.Append($"    \"{AfkKeys[i]}\": {(c == null ? "null" : AssetRefJson(c))}{(i < 5 ? "," : "")}\n");
            }
            sb.Append("  }");
            SetTopLevel(avatarTable, "afkClips", sb.ToString());
        }

        public static string AssetRefJson(UnityEngine.Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            return $"{{\"guid\": {StringJson(AssetDatabase.AssetPathToGUID(path))}, \"path\": {StringJson(path)}}}";
        }

        public static string StringJson(string s) => Newtonsoft.Json.JsonConvert.ToString(s);

        // Replaces the value of a top-level key ("  \"key\": value" at two spaces), keeping comments and the other
        // lines. Adds the key at the end when it is missing. valueJson must be valid JSON.
        public static void SetTopLevel(string avatarTable, string key, string valueJson)
        {
            var text = File.ReadAllText(avatarTable);
            var m = System.Text.RegularExpressions.Regex.Match(text, "(?m)^  \"" + System.Text.RegularExpressions.Regex.Escape(key) + "\":[ \\t]*");
            if (m.Success)
            {
                var start = m.Index + m.Length;
                var end = ValueEnd(text, start);
                text = text.Substring(0, start) + valueJson + text.Substring(end);
            }
            else
            {
                var last = text.LastIndexOf('}');
                text = text.Substring(0, last).TrimEnd() + ",\n  \"" + key + "\": " + valueJson + "\n}\n";
            }
            TableLoader.Parse(text, avatarTable);  // the new text must still be a valid table before it is written
            File.WriteAllText(avatarTable, text);
            AssetDatabase.ImportAsset(avatarTable);
        }

        // End index (exclusive) of the JSON value that starts at i.
        static int ValueEnd(string t, int i)
        {
            if (t[i] == '{' || t[i] == '[')
            {
                int depth = 0; var inStr = false;
                for (int k = i; k < t.Length; k++)
                {
                    var c = t[k];
                    if (inStr) { if (c == '\\') k++; else if (c == '"') inStr = false; continue; }
                    // Comments inside the value (the tables are JSON with comments) do not count.
                    if (c == '/' && k + 1 < t.Length && t[k + 1] == '/') { while (k < t.Length && t[k] != '\n') k++; continue; }
                    if (c == '/' && k + 1 < t.Length && t[k + 1] == '*') { var close = t.IndexOf("*/", k + 2, StringComparison.Ordinal); k = close < 0 ? t.Length : close + 1; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return k + 1;
                }
                throw new FormatException("unbalanced value");
            }
            if (t[i] == '"')
            {
                for (int k = i + 1; k < t.Length; k++)
                {
                    if (t[k] == '\\') { k++; continue; }
                    if (t[k] == '"') return k + 1;
                }
                throw new FormatException("unterminated string");
            }
            int e = i;
            while (e < t.Length && t[e] != ',' && t[e] != '\n' && t[e] != '}') e++;
            return e;
        }

        // Copies the two templates from the package to Settings/<name>/ and returns the avatar table path.
        public static string CreateFromTemplates(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("invalid name: " + name);
            var dir = $"{Paths.SettingsRoot}/{name}";
            if (Directory.Exists(dir)) throw new IOException(dir + " already exists");
            Directory.CreateDirectory(dir);
            foreach (var t in new[] { "expression", "avatar" })
            {
                var text = File.ReadAllText($"{Paths.Templates}/{t}.template.jsonc");
                if (t == "avatar") text = text.Replace("\"name\": \"MyAvatar\"", $"\"name\": \"{name}\"");
                File.WriteAllText($"{dir}/{t}.jsonc", text);
            }
            AssetDatabase.Refresh();
            return dir + "/avatar.jsonc";
        }
    }
}
