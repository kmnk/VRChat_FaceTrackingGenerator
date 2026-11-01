using System.IO;
using UnityEditor;

namespace Kmnk.FaceTrackingGenerator
{
    static class AssetCopy
    {
        // Copies the asset file to dest. When dest already exists, its .meta (GUID) is kept,
        // so references to the generated asset stay valid across regenerations.
        public static void CopyKeepingGuid(string src, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (File.Exists(dest))
            {
                File.Copy(src, dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            }
            else if (!AssetDatabase.CopyAsset(src, dest))
            {
                throw new IOException($"copy failed: {src} -> {dest}");
            }
            // CopyAsset names the main object after the file; a plain file copy keeps the source name.
            // Use the file name both times, so regenerating does not change the asset.
            var main = AssetDatabase.LoadMainAssetAtPath(dest);
            var name = Path.GetFileNameWithoutExtension(dest);
            if (main != null && main.name != name)
            {
                main.name = name;
                EditorUtility.SetDirty(main);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
