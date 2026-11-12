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

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints the line under each slider of the preview (InputParts) and whether the preview shows the reason
        // that the face does not move (no blendshape of the tables is on the prefab's meshes).
        //   [FTG] input-parts <input>: <parts>
        //   [FTG] preview-reason: show=<true|false> on-avatar=<n> targets=<n>
        public static void InputParts() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            foreach (var kv in Kmnk.FaceTrackingGenerator.InputParts.Of(model)) Debug.Log($"[FTG] input-parts {kv.Key}: {kv.Value}");
            var prefabPath = !string.IsNullOrEmpty(model.Prefab.Guid) ? UnityEditor.AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab.Path;
            var prefab = string.IsNullOrEmpty(prefabPath) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var targets = model.Parts.Values.Select(p => p.Mesh + "/" + p.Shape).Distinct().ToList();
            var onAvatar = 0;
            if (prefab != null)
                foreach (var key in targets)
                {
                    var (mesh, shape) = FacePreview.Split(key);
                    var t = prefab.transform.Find(mesh);
                    var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr != null && smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex(shape) >= 0) onAvatar++;
                }
            Debug.Log($"[FTG] preview-reason: show={(onAvatar == 0).ToString().ToLower()} on-avatar={onAvatar} targets={targets.Count}");
            return 0;
        });
    }
}

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints the items of the settings editor's Share menu for -ftTable and whether each can be pressed, as the
        // window builds it: "[FTG] share-menu: <item>=on|off | ...". -ftDirty 1 makes an unsaved change first.
        public static void ShareMenu() => Cli.Run(() =>
        {
            var doc = TableDocument.Open(CliArgs.Require("-ftTable"));
            if (CliArgs.Get("-ftDirty") == "1")
            {
                var avatar = doc.Json(TableDocument.Avatar);
                avatar["name"] = (string)avatar["name"] + " (edited)";
            }
            var items = TableEditorWindow.ShareMenuItems(doc.IsDirty());
            Debug.Log($"[FTG] share-menu: " + string.Join(" | ", items.Select(i => i.label + "=" + (i.enabled ? "on" : "off"))));
            return 0;
        });
    }
}
