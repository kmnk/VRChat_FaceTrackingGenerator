using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Kmnk.FaceTrackingGenerator
{
    // Carries the expression table between projects through a folder (expression.jsonc; 0.12.1 removed the input table).
    // Files are copied byte for byte, so comments and order stay. The avatar table is never copied.
    // Used by the settings editor's Share menu, the generator window and Cli.ExportTables / ImportTables.
    public static class TableShare
    {
        public const string ExpressionFile = "expression.jsonc";
        static readonly string[] Files = { ExpressionFile };

        static Dictionary<string, string> Sources(string avatarPath)
        {
            return new Dictionary<string, string> { [ExpressionFile] = TableLoader.ExpressionPath(avatarPath) };
        }

        // The tables already in the folder, which Export would overwrite.
        public static List<string> Existing(string folder) => Files.Where(f => File.Exists(Path.Combine(folder, f))).ToList();

        public static void Export(string avatarPath, string folder)
        {
            var src = Sources(avatarPath);
            foreach (var kv in src) TableLoader.Read(kv.Value);  // it must read before anything is written
            Directory.CreateDirectory(folder);
            foreach (var f in Files) File.Copy(src[f], Path.Combine(folder, f), true);
        }

        // The folder's table must read; throws TableException naming the file otherwise.
        static void CheckFolder(string folder)
        {
            foreach (var f in Files) TableLoader.Read(Path.Combine(folder, f));
        }

        // The tables of the avatar table that the folder would change (bytes differ).
        public static List<string> Changes(string avatarPath, string folder)
        {
            CheckFolder(folder);
            var src = Sources(avatarPath);
            return Files.Where(f => !File.Exists(src[f]) || !File.ReadAllBytes(src[f]).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, f)))).ToList();
        }

        // Overwrites the changed tables with the folder's; returns their names. The avatar table is left as it is.
        public static List<string> Import(string avatarPath, string folder)
        {
            var changed = Changes(avatarPath, folder);
            var src = Sources(avatarPath);
            foreach (var f in changed)
            {
                File.Copy(Path.Combine(folder, f), src[f], true);
                ImportIfAsset(src[f]);
            }
            return changed;
        }

        // Settings/<name>/ from the templates, with the folder's expression table.
        public static string CreateFromFolder(string name, string folder)
        {
            CheckFolder(folder);
            var avatarPath = TableWriter.CreateFromTemplates(name);
            var src = Sources(avatarPath);
            foreach (var f in Files) File.Copy(Path.Combine(folder, f), src[f], true);
            AssetDatabase.Refresh();
            return avatarPath;
        }

        static void ImportIfAsset(string path)
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');
            var root = Path.GetFullPath("Assets").Replace('\\', '/') + "/";
            if (full.StartsWith(root)) AssetDatabase.ImportAsset("Assets/" + full.Substring(root.Length));
        }
    }
}
