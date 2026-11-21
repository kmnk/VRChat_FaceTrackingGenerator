using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The offline manual in the installed package (Documentation~/manual-ja/index.html, made by
    // scripts/build-package.sh from docs/manual-ja/README.md). Unity does not import folders ending in ~.
    static class ManualFiles
    {
        internal const string RelativePath = "Documentation~/manual-ja/index.html";

        // The full path of the offline manual, or null when the package has none (the development copy).
        internal static string LocalPath()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ManualFiles).Assembly);
            if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;
            var path = Path.Combine(info.resolvedPath, RelativePath);
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }

        // Opens the offline manual in the default browser, or the manual on GitHub when there is none.
        internal static void Open()
        {
            var local = LocalPath();
            if (local != null) EditorUtility.OpenWithDefaultApp(local);
            else Application.OpenURL(FtGeneratorSettings.ManualUrl);
        }
    }
}
