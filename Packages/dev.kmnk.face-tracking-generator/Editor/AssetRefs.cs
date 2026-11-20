using System.Collections.Generic;
using UnityEditor;

namespace Kmnk.FaceTrackingGenerator
{
    // The tables refer to assets as {"guid", "path"}. Every reader resolves them here, the same way: the GUID while it
    // points to an asset, else the path (an asset moved or copied from another project keeps working by its path).
    public static class AssetRefs
    {
        // The asset path, or null when neither is set. A GUID that points to another path or to nothing adds a
        // "warning:" line to log.
        public static string Path(AssetRef a, string what, List<string> log = null)
        {
            if (a == null) return null;
            var byGuid = string.IsNullOrEmpty(a.Guid) ? null : AssetDatabase.GUIDToAssetPath(a.Guid);
            if (!string.IsNullOrEmpty(byGuid))
            {
                if (!string.IsNullOrEmpty(a.Path) && a.Path != byGuid) log?.Add($"warning: {what} path in the table is {a.Path}, but the guid points to {byGuid}");
                return byGuid;
            }
            if (!string.IsNullOrEmpty(a.Guid) && !string.IsNullOrEmpty(a.Path)) log?.Add($"warning: {what} guid {a.Guid} is not in this project; using the path {a.Path}");
            return string.IsNullOrEmpty(a.Path) ? null : a.Path;
        }
    }
}
