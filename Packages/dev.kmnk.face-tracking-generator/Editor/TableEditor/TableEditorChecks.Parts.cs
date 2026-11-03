using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // The face movements of the Avatar tab: adds them with a blendshape and removes unused ones, as the tab does.
        //   -ftAddPart "<name>=<shape>;..."   -ftRemovePart "<name>;..." (refused when an expression uses it)   -ftSave 1
        // Lines: [FTG] parts add|remove <name>: done=<bool> ; [FTG] parts <name>: shape=<s> used=<bool> ; [FTG] parts tuning-choices: ...
        public static void Parts() => Cli.Run(() =>
        {
            var doc = TableDocument.Open(CliArgs.Require("-ftTable"));
            HashSet<string> Used()
            {
                var used = new HashSet<string>();
                foreach (var t in doc.Model().Trees) TableEditorWindow.CollectParts(t.Root, used);
                return used;
            }
            foreach (var item in (CliArgs.Get("-ftAddPart") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.IndexOf('=');
                TableEdits.SetPart(doc.Json(TableDocument.Avatar), item.Substring(0, k), item.Substring(k + 1));
                Debug.Log($"[FTG] parts add {item.Substring(0, k)}: done=true");
            }
            foreach (var name in (CliArgs.Get("-ftRemovePart") ?? "").Split(';').Where(x => x != ""))
            {
                var can = !Used().Contains(name);
                if (can) TableEdits.RemovePart(doc.Json(TableDocument.Avatar), name);
                Debug.Log($"[FTG] parts remove {name}: done={can.ToString().ToLower()}");
            }
            var used = Used();
            var avatar = doc.Json(TableDocument.Avatar);
            foreach (var p in (avatar["parts"] as Newtonsoft.Json.Linq.JObject)?.Properties().Select(x => x.Name) ?? Enumerable.Empty<string>())
                Debug.Log($"[FTG] parts {p}: shape={TableEdits.PartShape(avatar, p)} used={used.Contains(p).ToString().ToLower()}");
            Debug.Log($"[FTG] parts tuning-choices: {string.Join(",", Kmnk.FaceTrackingGenerator.Tuning.AllPartNames(avatar, doc.Json(TableDocument.Expression)))}");
            if (CliArgs.Get("-ftSave") == "1") foreach (var path in doc.Save()) Debug.Log("[FTG] parts saved " + path);
            return 0;
        });
    }
}
