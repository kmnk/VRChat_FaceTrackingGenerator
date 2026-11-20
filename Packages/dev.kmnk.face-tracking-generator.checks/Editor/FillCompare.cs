using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Fills two copies of the templates from the same avatar: one as Cli.FillFromAvatar does (text edits of the file),
    // one as the settings editor's "Fill from Prefab" does (the table held in memory, then Save), and compares the
    // two avatar tables as JSON.
    //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.FillCompare.Run -ftAvatar <prefab>
    // Prints "[FTG] fill-compare: same=<bool>" (and the first differing key); the copies are deleted at the end.
    public static class FillCompare
    {
        public static void Run() => Cli.Run(() =>
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CliArgs.Require("-ftAvatar")) ?? throw new System.ArgumentException("no prefab");
            var a = Fresh("FillCompareCli");
            var b = Fresh("FillCompareEditor");
            AvatarSetup.FillTable(a, prefab);
            var doc = TableDocument.Open(b);
            var r = AvatarSetup.Fill(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression), prefab);
            AvatarSetup.Apply(doc.Json(TableDocument.Avatar), r);
            doc.Save();
            var ja = TableLoader.Read(a); var jb = TableLoader.Read(b);
            ja["name"] = jb["name"] = "x";
            var same = JToken.DeepEquals(ja, jb);
            Debug.Log($"[FTG] fill-compare: same={same.ToString().ToLower()}");
            if (!same)
                foreach (var p in ja.Properties())
                    if (!JToken.DeepEquals(p.Value, jb[p.Name])) { Debug.Log($"[FTG] fill-compare: differs at {p.Name}: {p.Value.ToString(Newtonsoft.Json.Formatting.None)} vs {jb[p.Name]?.ToString(Newtonsoft.Json.Formatting.None)}"); break; }
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(a));
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(b));
            return same ? 0 : 1;
        });

        static string Fresh(string name)
        {
            AssetDatabase.DeleteAsset(Paths.SettingsRoot + "/" + name);
            return TableWriter.CreateFromTemplates(name);
        }
    }
}
