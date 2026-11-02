using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Command line entry points that run the table editor's code the same way the window does, for checking it
    // without drawing the window. Exit code 0: done / passed, 1: a check failed, 2: error. Results are [FTG] lines.
    public static partial class TableEditorChecks
    {
        // Opens the tables, applies the edits (-ftEdits, JSON Lines of TableEdits.Apply operations) to the avatar
        // table, and saves. -ftAll saves all three tables even when they are unchanged.
        public static void Edit() => Cli.Run(() =>
        {
            var doc = TableDocument.Open(CliArgs.Require("-ftTable"));
            var editsPath = CliArgs.Get("-ftEdits");
            var removed = new Dictionary<string, JToken>();
            var n = 0;
            if (editsPath != null)
                foreach (var line in File.ReadAllLines(editsPath).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    TableEdits.Apply(doc.Json(TableDocument.Avatar), JObject.Parse(line), removed);
                    n++;
                }
            var model = doc.Model();  // the edited tables must still load
            var written = doc.Save(CliArgs.Get("-ftAll") == "1");
            Debug.Log($"[FTG] table-editor edit: applied={n} written={written.Count} parts={model.Parts.Count} dropped={model.Dropped.Count}");
            foreach (var w in written) Debug.Log("[FTG] wrote " + w);
            return 0;
        });
    }
}

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints the items of the blendshape list (BlendshapePicker) for a part of -ftTable and a search text
        // (-ftPart, -ftSearch): "[FTG] picker: (None) | -- Mouth -- | <blendshape> ...". Headings start with "-- ".
        public static void Picker() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var part = CliArgs.Require("-ftPart");
            var mesh = model.Parts.TryGetValue(part, out var t) ? t.Mesh : model.DefaultMesh;
            var prefabPath = !string.IsNullOrEmpty(model.Prefab.Guid) ? UnityEditor.AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab.Path;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var m = prefab.transform.Find(mesh).GetComponent<SkinnedMeshRenderer>().sharedMesh;
            var names = Enumerable.Range(0, m.blendShapeCount).Select(m.GetBlendShapeName).ToList();
            var items = BlendshapePicker.Items(names, CliArgs.Get("-ftSearch") ?? "");
            Debug.Log($"[FTG] picker: blendshapes={names.Count} separators={names.Count(BlendshapePicker.IsSeparator)} items={items.Count}");
            Debug.Log("[FTG] picker: " + string.Join(" | ", items.Select(i => i.text)));
            return 0;
        });
    }
}
