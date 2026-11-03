using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints what the expression graph shows for a tree of -ftTable, splits positions the way the graph does, and
        // saves with -ftSave 1.
        //   -ftTree <name>                        default: every tree
        //   -ftSplit "<tree>|<box path>|<row>=<input>;..."   e.g. "Mouth|root/0/0|0=SmileFrown"
        //   -ftOpen "<box path>;..."           boxes shown open (face movements one per line)
        //   -ftSplit2D "<tree>|<box path>|<row>=<x>,<y>;..."   split with 2D
        //   -ftRemove "<tree>|<box path>|<keep row>;..."      remove a box, keeping one row's content
        //   -ftInsert "<tree>|<box path>=<x>[,<y>];..."      insert a 1D or 2D box above (weights compared with the new inputs at 0)
        //   -ftNewTree "<name>=<x>[,<y>];..."  -ftRemoveTree "<name>;..."
        // Lines:
        //   [FTG] graph box <tree> <path>: <title> | x=<x> y=<y> | tuning=<key> | <row>; <row>; ...   (rows marked "->" hold a box)
        //   [FTG] graph edge <tree> <path>#<row> -> <child path>
        //   [FTG] graph inputs <tree>: <names>
        //   [FTG] graph split <tree> <path>#<row> <input>: can=<bool> new=<path> node=<json> max-weight-diff=<v>
        //   [FTG] graph saved <path>
        public static void Graph() => Cli.Run(() =>
        {
            var path = CliArgs.Require("-ftTable");
            var doc = TableDocument.Open(path);
            var expression = doc.Json(TableDocument.Expression);
            var only = CliArgs.Get("-ftTree");
            var open = new HashSet<string>((CliArgs.Get("-ftOpen") ?? "").Split(';').Where(x => x != ""));
            foreach (var tree in ExpressionGraph.TreeNames(expression).Where(t => only == null || t == only))
                PrintGraph(expression, doc.Json(TableDocument.Input), tree, open);

            var split = CliArgs.Get("-ftSplit");
            if (split != null)
                foreach (var item in split.Split(';').Where(x => x.Contains("=")))
                {
                    var k = item.LastIndexOf('=');
                    var head = item.Substring(0, k).Split('|');
                    var input = item.Substring(k + 1);
                    var row = int.Parse(head[2]);
                    var before = doc.Model();
                    var can = ExpressionGraph.CanSplit(expression, head[0], head[1], row);
                    var made = ExpressionGraph.Split(expression, head[0], head[1], row, input);
                    var after = doc.Model();
                    var diff = MaxWeightDiff(before, after);
                    var node = made != null ? ExpressionGraph.Find(expression, head[0], made).ToString(Newtonsoft.Json.Formatting.None) : "-";
                    Debug.Log($"[FTG] graph split {head[0]} {head[1]}#{row} {input}: can={can.ToString().ToLower()} new={made ?? "-"} node={node} max-weight-diff={F(diff)}");
                    if (made != null) PrintGraph(expression, doc.Json(TableDocument.Input), head[0]);
                }
            foreach (var item in (CliArgs.Get("-ftSplit2D") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.LastIndexOf('=');
                var head = item.Substring(0, k).Split('|');
                var xy = item.Substring(k + 1).Split(',');
                var row = int.Parse(head[2]);
                var before = doc.Model();
                var made = ExpressionGraph.Split2D(expression, head[0], head[1], row, xy[0], xy[1]);
                var after = doc.Model();
                var node = made != null ? ExpressionGraph.Find(expression, head[0], made).ToString(Newtonsoft.Json.Formatting.None) : "-";
                Debug.Log($"[FTG] graph split2d {head[0]} {head[1]}#{row} {xy[0]},{xy[1]}: new={made ?? "-"} node={node} max-weight-diff={F(MaxWeightDiff(before, after))}");
                if (made != null) PrintGraph(expression, doc.Json(TableDocument.Input), head[0]);
            }
            foreach (var item in (CliArgs.Get("-ftRemove") ?? "").Split(';').Where(x => x.Contains("|")))
            {
                var head = item.Split('|');
                var can = ExpressionGraph.CanRemove(expression, head[0], head[1]);
                var kept = ExpressionGraph.Remove(expression, head[0], head[1], int.Parse(head[2]));
                var content = kept != null ? ExpressionGraph.Find(expression, head[0], kept)?.ToString(Newtonsoft.Json.Formatting.None) : null;
                Debug.Log($"[FTG] graph remove {head[0]} {head[1]} keep={head[2]}: can={can.ToString().ToLower()} done={(kept != null).ToString().ToLower()} now={(content ?? "(face movements)")}");
                if (kept != null) PrintGraph(expression, doc.Json(TableDocument.Input), head[0]);
            }
            foreach (var item in (CliArgs.Get("-ftInsert") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.LastIndexOf('=');
                var head = item.Substring(0, k).Split('|');
                var xy = item.Substring(k + 1).Split(',');
                var before = doc.Model();
                var made = ExpressionGraph.InsertAbove(expression, head[0], head[1], xy[0], xy.Length > 1 ? xy[1] : null);
                var after = doc.Model();
                var zero = xy.ToDictionary(x => x, x => 0f);
                Debug.Log($"[FTG] graph insert {head[0]} {head[1]} {item.Substring(k + 1)}: new={made ?? "-"} max-weight-diff-at-0={F(MaxWeightDiff(before, after, zero))} max-weight-diff={F(MaxWeightDiff(before, after))}");
                if (made != null) PrintGraph(expression, doc.Json(TableDocument.Input), head[0]);
            }
            foreach (var item in (CliArgs.Get("-ftNewTree") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.LastIndexOf('=');
                var xy = item.Substring(k + 1).Split(',');
                var made = ExpressionGraph.NewTree(expression, item.Substring(0, k), xy[0], xy.Length > 1 ? xy[1] : null);
                Debug.Log($"[FTG] graph new-tree {item.Substring(0, k)} {item.Substring(k + 1)}: done={(made != null).ToString().ToLower()}");
                if (made != null) PrintGraph(expression, doc.Json(TableDocument.Input), made);
            }
            foreach (var name in (CliArgs.Get("-ftRemoveTree") ?? "").Split(';').Where(x => x != ""))
                Debug.Log($"[FTG] graph remove-tree {name}: done={ExpressionGraph.RemoveTree(expression, name).ToString().ToLower()} trees={string.Join(",", ExpressionGraph.TreeNames(expression))}");
            if (CliArgs.Get("-ftSave") == "1")
                foreach (var p in doc.Save()) Debug.Log($"[FTG] graph saved {p}");
            return 0;
        });

        static void PrintGraph(Newtonsoft.Json.Linq.JObject expression, Newtonsoft.Json.Linq.JObject input, string tree, ICollection<string> open = null)
        {
            var boxes = ExpressionGraph.Build(expression, tree, open);
            foreach (var b in boxes)
                Debug.Log($"[FTG] graph box {tree} {b.Path}: {b.Title} | x={F(b.X)} y={F(b.Y)} h={F(b.Height)} open={b.Open.ToString().ToLower()} | tuning={b.TuningKey ?? "-"} | " +
                          string.Join("; ", b.Rows.Select(r => (r.Nested ? "-> " : "") + r.Label + (r.Lines.Count > 0 ? " [" + string.Join(" / ", r.Lines) + "]" : ""))));
            foreach (var b in boxes) foreach (var r in b.Rows.Where(r => r.FullLabel != r.Label && !b.Open))
                Debug.Log($"[FTG] graph tip {tree} {b.Path}: {r.FullLabel}");
            foreach (var b in boxes.Where(b => b.ParentPath != null))
                Debug.Log($"[FTG] graph edge {tree} {b.ParentPath}#{b.ParentRow} -> {b.Path}");
            Debug.Log($"[FTG] graph inputs {tree}: {string.Join(",", ExpressionGraph.SplitInputs(input, expression, tree))}");
        }

        // The largest difference of any blendshape weight between two models, over a fixed set of input values
        // (the ends, 0 and evenly spaced values of every input, each alone and all together at random).
        static float MaxWeightDiff(TableModel a, TableModel b, Dictionary<string, float> fixedInputs = null)
        {
            var names = a.Inputs.Select(x => x.Name).Union(b.Inputs.Select(x => x.Name)).ToList();
            var samples = new List<Dictionary<string, float>>();
            var values = new[] { -1f, -0.9f, -0.5f, -0.25f, 0f, 0.15f, 0.3f, 0.5f, 0.7f, 0.9f, 1f };
            foreach (var n in names) foreach (var v in values) samples.Add(new Dictionary<string, float> { [n] = v });
            var rnd = new System.Random(1);
            for (int s = 0; s < 400; s++) samples.Add(names.ToDictionary(n => n, n => (float)(rnd.NextDouble() * 2 - 1)));
            if (fixedInputs != null) foreach (var x in samples) foreach (var kv in fixedInputs) x[kv.Key] = kv.Value;
            float max = 0;
            foreach (var x in samples)
            {
                var wa = CurveEval.Evaluate(a, x);
                var wb = CurveEval.Evaluate(b, x);
                foreach (var key in wa.Keys.Union(wb.Keys))
                    max = Math.Max(max, Math.Abs((wa.TryGetValue(key, out var va) ? va : 0) - (wb.TryGetValue(key, out var vb) ? vb : 0)));
            }
            return max;
        }
    }
}
