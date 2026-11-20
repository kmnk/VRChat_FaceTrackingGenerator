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

        // The warnings for the table's references (prefab, menu, AFK clips), as generating and Check print them.
        public static List<string> Warnings(TableModel m)
        {
            var log = new List<string>();
            Path(m.Prefab, "prefab", log);
            Path(m.Menu, "menu", log);
            var c = m.AfkClips;
            foreach (var (r, what) in new[] { (c.ActionIntro, "AFK clip actionIntro"), (c.ActionLoop, "AFK clip actionLoop"), (c.ActionOutro, "AFK clip actionOutro"), (c.FxIntro, "AFK clip fxIntro"), (c.FxLoop, "AFK clip fxLoop"), (c.FxOutro, "AFK clip fxOutro") })
                Path(r, what, log);
            return log;
        }
    }
}
