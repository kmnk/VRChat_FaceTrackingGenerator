using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // The face movements of the Avatar tab: adds them with a blendshape and removes unused ones, as the tab does.
        //   -ftRenamePart "<from>=<to>;..."
        //   -ftNames "<name>,..."   prints [FTG] parts name <name>: none|form|used (a new name, as + Add Face Movement checks it)
        //   -ftAddPart "<name>=<shape>;..."   -ftRemovePart "<name>;..." (refused when an expression uses it)   -ftSave 1
        // Lines: [FTG] parts add|remove <name>: done=<bool> ; [FTG] parts <name>: shape=<s> used=<bool> ; [FTG] parts tuning-choices: ...
        public static void Parts() => Cli.Run(() =>
        {
            var doc = TableDocument.Open(CliArgs.Require("-ftTable"));
            HashSet<string> Used()
            {
                return new HashSet<string>(PartNames.InExpression(doc.Json(TableDocument.Expression)));
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
            foreach (var item in (CliArgs.Get("-ftRenamePart") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.IndexOf('=');
                var from = item.Substring(0, k);
                var rest = new[] { item.Substring(k + 1) };
                var together = false;
                var before = doc.Model();
                var problem = PartRename.Check(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression), from, rest[0], together);
                var map = PartRename.Apply(doc.Json(TableDocument.Avatar), doc.Json(TableDocument.Expression), from, rest[0], together);
                var diff = map != null ? MaxWeightDiff(before, doc.Model()) : 0f;
                Debug.Log($"[FTG] parts rename {item}: problem={problem} map={(map == null ? "-" : string.Join(",", map.Select(kv => kv.Key + ">" + kv.Value)))} max-weight-diff={F(diff)}");
            }
            var used = Used();
            var avatar = doc.Json(TableDocument.Avatar);
            foreach (var p in (avatar["parts"] as Newtonsoft.Json.Linq.JObject)?.Properties().Select(x => x.Name) ?? Enumerable.Empty<string>())
                Debug.Log($"[FTG] parts {p}: shape={TableEdits.PartShape(avatar, p)} used={used.Contains(p).ToString().ToLower()}");
            var all = PartNames.Used(avatar, doc.Json(TableDocument.Expression));
            Debug.Log($"[FTG] parts all: {string.Join(",", all)}");
            foreach (var n in (CliArgs.Get("-ftNames") ?? "").Split(',').Where(x => x != ""))
                Debug.Log($"[FTG] parts name {n}: {PartNames.Check(n, all).ToString().ToLower()}");
            if (CliArgs.Get("-ftSave") == "1") foreach (var path in doc.Save()) Debug.Log("[FTG] parts saved " + path);
            return 0;
        });

        static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // The largest change of a blendshape's weight between two models, with every tracking key at 1 in each state
        // (a rename must not change the face).
        static float MaxWeightDiff(TableModel a, TableModel b)
        {
            var keys = a.Inputs.Concat(b.Inputs).Where(i => i.IsTrack).Select(i => i.Name).Distinct().ToDictionary(n => n, n => 1f);
            var worst = 0f;
            var combos = new List<Dictionary<string, string>> { new Dictionary<string, string>() };
            foreach (var sm in a.StateMachines) foreach (var st in sm.States) combos.Add(new Dictionary<string, string> { [sm.Name] = st.Name });
            foreach (var states in combos)
            {
                var wa = FaceEval.Evaluate(a, keys, states);
                var wb = FaceEval.Evaluate(b, keys, states);
                foreach (var k in wa.Keys.Union(wb.Keys))
                    worst = System.Math.Max(worst, System.Math.Abs((wa.TryGetValue(k, out var x) ? x : 0) - (wb.TryGetValue(k, out var y) ? y : 0)));
            }
            return worst;
        }
    }
}
