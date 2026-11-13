using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The three tables as JSON trees, for the table editor. Edits change the trees; nothing is written until Save.
    // The trees are edited, not TableModel: the model has left/right pairs expanded and rows dropped.
    public sealed class TableDocument
    {
        public const string Avatar = "avatar", Expression = "expression";
        public static readonly string[] Kinds = { Avatar, Expression };

        sealed class Entry
        {
            public string Path;
            public JObject Json;
            public string Saved;  // one-line form of the file as last read or saved
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public string AvatarPath { get; }

        TableDocument(string avatarPath) { AvatarPath = avatarPath; }

        public static TableDocument Open(string avatarPath)
        {
            var d = new TableDocument(avatarPath);
            d.Add(Avatar, avatarPath);
            var tables = d.Json(Avatar)["tables"] as JObject ?? throw new TableLoader.TableException($"{avatarPath}: 'tables' must be an object");
            var dir = System.IO.Path.GetDirectoryName(avatarPath);
            foreach (var kind in new[] { Expression })
            {
                var rel = tables[kind]?.Type == JTokenType.String ? (string)tables[kind] : throw new TableLoader.TableException($"{avatarPath}: 'tables.{kind}' must be a string");
                d.Add(kind, System.IO.Path.Combine(dir, rel));
            }
            return d;
        }

        void Add(string kind, string path)
        {
            var json = TableLoader.Read(path);
            entries[kind] = new Entry { Path = path, Json = json, Saved = JsonTableWriter.OneLine(json) };
        }

        public JObject Json(string kind) => entries[kind].Json;
        public string PathOf(string kind) => entries[kind].Path;

        public bool IsDirty(string kind) => JsonTableWriter.OneLine(entries[kind].Json) != entries[kind].Saved;
        public bool IsDirty() => Kinds.Any(IsDirty);

        // Resolved model of the tables as they are now (saved or not).
        public TableModel Model()
        {
            var byPath = entries.Values.ToDictionary(e => Full(e.Path), e => e.Json);
            return TableLoader.Load(AvatarPath, p => byPath.TryGetValue(Full(p), out var j) ? j : TableLoader.Read(p));
        }

        static string Full(string p) => System.IO.Path.GetFullPath(p);

        // Writes the changed tables (all three with all = true). Returns the paths written.
        public List<string> Save(bool all = false)
        {
            var written = new List<string>();
            foreach (var kind in Kinds)
            {
                var e = entries[kind];
                if (!all && !IsDirty(kind)) continue;
                File.WriteAllText(e.Path, JsonTableWriter.Write(e.Json));
                e.Saved = JsonTableWriter.OneLine(e.Json);
                written.Add(e.Path);
            }
            return written;
        }

        // The table as last read or saved.
        public JObject SavedJson(string kind) => JObject.Parse(entries[kind].Saved);

        // For Undo: the trees as text, and back.
        public string[] Snapshot() => Kinds.Select(k => JsonTableWriter.OneLine(entries[k].Json)).ToArray();

        public void Restore(string[] snapshot)
        {
            if (snapshot == null || snapshot.Length != Kinds.Length) return;
            for (int i = 0; i < Kinds.Length; i++)
                if (!string.IsNullOrEmpty(snapshot[i])) entries[Kinds[i]].Json = JObject.Parse(snapshot[i]);
        }
    }
}
