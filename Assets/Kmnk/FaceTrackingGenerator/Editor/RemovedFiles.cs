using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Kmnk.FaceTrackingGenerator
{
    // Importing a .unitypackage does not delete files that the new version no longer has. The package
    // carries FILES.txt (every file it contains); .cs files under Editor/ that are not listed are left over
    // from an older version and can cause duplicate definitions.
    public static class RemovedFiles
    {
        const string Root = "Assets/Kmnk/FaceTrackingGenerator";
        const string ListPath = Root + "/FILES.txt";

        public static List<string> Find()
        {
            if (!File.Exists(ListPath)) return new List<string>();  // a development copy: no list
            var listed = new HashSet<string>(File.ReadAllLines(ListPath).Select(l => l.Trim()).Where(l => l != ""));
            return Directory.GetFiles(Root + "/Editor", "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !listed.Contains(p))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
        }

        public static void PromptIfAny()
        {
            var files = Find();
            if (files.Count == 0) return;
            var ok = EditorUtility.DisplayDialog("Face Tracking Generator",
                "These files are not in the installed version and are left over from an older one:\n\n" + string.Join("\n", files) + "\n\nDelete them?",
                "Delete", "Keep");
            if (ok) Delete(files);
        }

        public static void Delete(IEnumerable<string> files)
        {
            foreach (var f in files) AssetDatabase.DeleteAsset(f);
            AssetDatabase.Refresh();
        }
    }
}
