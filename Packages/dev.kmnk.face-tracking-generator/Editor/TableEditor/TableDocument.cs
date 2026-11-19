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
            RemovedRecordings = RemoveUnlistedRecordings();
            return written;
        }

        // The recordings Save deleted last time (paths, for the caller to tell the AssetDatabase).
        public List<string> RemovedRecordings { get; private set; } = new List<string>();

        // Measurements/ next to the avatar table holds the copies TableEdits.AddMeasurement made. A recording there that
        // the saved table no longer lists (removed in the Devices tab, or added and removed before saving) is deleted
        // with its .meta, so the folder holds what the table uses. Deleted at Save, not at removal, so Undo and closing
        // without saving keep the file.
        List<string> RemoveUnlistedRecordings()
        {
            var removed = new List<string>();
            var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AvatarPath), Measurements.Folder);
            if (!Directory.Exists(dir)) return removed;
            var listed = new HashSet<string>((entries[Avatar].Json["measurements"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(o => Measurements.Text(o["file"])).Where(f => f != null)
                .Select(f => Full(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AvatarPath), f))));
            foreach (var file in Directory.GetFiles(dir, "*.jsonl"))
            {
                if (listed.Contains(Full(file))) continue;
                File.Delete(file);
                if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
                removed.Add(file.Replace('\\', '/'));
            }
            return removed;
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
