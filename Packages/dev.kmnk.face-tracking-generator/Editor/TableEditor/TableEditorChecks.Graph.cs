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
        //   -ftDevice <id>  prints the device's inputs not in the table that each tree's split list offers ("Add from <device>")
        //   -ftSplitAdd "<tree>|<box path>|<row>=<device input>|<x>[,<y>];..."   adds the device input as the list does, then
        //                splits with 1D (<x>) or 2D (<x>,<y>); one of x and y is the added input
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
            var deviceId = CliArgs.Get("-ftDevice");
            var device = deviceId == null ? null : Devices.Load().FirstOrDefault(d => d.Id == deviceId);
            if (deviceId != null && device == null) throw new System.InvalidOperationException("no device " + deviceId);
            InputChoices.Result DeviceRows()
            {
                var m = doc.Model();
                return InputChoices.Rows(doc.Json(TableDocument.Input), expression, m, device, doc.SavedJson(TableDocument.Input));
            }
            if (device != null)
                foreach (var tree in ExpressionGraph.TreeNames(expression).Where(t => only == null || t == only))
                {
                    var rows = DeviceRows();
                    var sided = expression["trees"]?[tree]?["sides"] != null;
                    Debug.Log($"[FTG] graph device {tree}: device={device.Name} choices={string.Join(",", InputChoices.SplitChoices(rows, sided).Select(c => c.Write))}");
                }
            foreach (var item in (CliArgs.Get("-ftSplitAdd") ?? "").Split(';').Where(x => x.Contains("=")))
            {
                var k = item.IndexOf('=');
                var head = item.Substring(0, k).Split('|');
                var rest = item.Substring(k + 1).Split('|');
                var xy = rest[1].Split(',');
                var row = int.Parse(head[2]);
                var choice = InputChoices.SplitChoices(DeviceRows(), expression["trees"]?[head[0]]?["sides"] != null).FirstOrDefault(c => c.Write == rest[0]);
                if (choice == null) { Debug.Log($"[FTG] graph split-add {item}: offered=false"); continue; }
                var before = doc.Model();
                InputChoices.Add(doc.Json(TableDocument.Input), choice.Row, device);
                var made = xy.Length > 1 ? ExpressionGraph.Split2D(expression, head[0], head[1], row, xy[0], xy[1]) : ExpressionGraph.Split(expression, head[0], head[1], row, xy[0]);
                var after = doc.Model();
                var written = doc.Json(TableDocument.Input)["inputs"]?[choice.Row.Name]?.ToString(Newtonsoft.Json.Formatting.None) ?? "-";
                var node = made != null ? ExpressionGraph.Find(expression, head[0], made).ToString(Newtonsoft.Json.Formatting.None) : "-";
                Debug.Log($"[FTG] graph split-add {head[0]} {head[1]}#{row} {rest[0]} {rest[1]}: offered=true input={written} new={made ?? "-"} node={node} max-weight-diff={F(MaxWeightDiff(before, after))}");
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
                var nm = item.Substring(0, k);
                var sided = nm.EndsWith("{side}");  // "Brow{side}=..." makes a left/right expression
                if (sided)
                {
                    nm = nm.Substring(0, nm.Length - 6);
                    Debug.Log($"[FTG] graph inputs-sided: {string.Join(",", ExpressionGraph.SplitInputs(doc.Json(TableDocument.Input), expression, null, true))}");
                }
                var made = ExpressionGraph.NewTree(expression, nm, xy[0], xy.Length > 1 ? xy[1] : null, sided);
                Debug.Log($"[FTG] graph new-tree {item.Substring(0, k)} {item.Substring(k + 1)}: done={(made != null).ToString().ToLower()}");
                if (made != null) PrintGraph(expression, doc.Json(TableDocument.Input), made);
            }
            foreach (var name in (CliArgs.Get("-ftRemoveTree") ?? "").Split(';').Where(x => x != ""))
                Debug.Log($"[FTG] graph remove-tree {name}: done={ExpressionGraph.RemoveTree(expression, name).ToString().ToLower()} trees={string.Join(",", ExpressionGraph.TreeNames(expression))}");
            // "-ftSwitchBox "<kind>|<tree>|<path>|<row>|<switch or new>;..."": kind split, insert or tree (tree: "<name>[{side}]" as <tree>)
            foreach (var item in (CliArgs.Get("-ftSwitchBox") ?? "").Split(';').Where(x => x.Contains("|")))
            {
                var f = item.Split('|');
                var tree = f[1];
                var sided = f[0] == "tree" ? tree.EndsWith("{side}") : expression["trees"]?[tree]?["sides"] != null;
                var first = ExpressionGraph.SplitInputs(doc.Json(TableDocument.Input), expression, f[0] == "tree" ? null : tree, sided).FirstOrDefault();
                var before = doc.Model();
                var r = f[4] == "new" ? SwitchEdit.NewSwitch(expression, sided, first) : f[4];
                var made = f[0] == "tree" ? ExpressionGraph.NewTreeSwitch(expression, sided ? tree.Substring(0, tree.Length - 6) : tree, r, sided)
                    : f[0] == "insert" ? ExpressionGraph.InsertSwitchAbove(expression, tree, f[2], r)
                    : ExpressionGraph.SplitSwitch(expression, tree, f[2], int.Parse(f[3]), r);
                var after = doc.Model();
                Debug.Log($"[FTG] graph switch-box {item}: switch={r} new={made ?? "-"} max-weight-diff={F(MaxWeightDiff(before, after))}");
                Debug.Log($"[FTG] graph switches: {expression["switches"]?.ToString(Newtonsoft.Json.Formatting.None)}");
                if (made != null && f[0] != "tree") PrintGraph(expression, doc.Json(TableDocument.Input), tree);
            }
            // "-ftSwitchEdit "<op>|<switch>|<args...>;..."": rename|<sw>|<name>, state|<sw>|<old>|<new>, add-state|<sw>,
            // remove-state|<sw>|<state>, add-tr|<sw>|<input>, remove-tr|<sw>|<i>, set-tr|<sw>|<i>|<from>|<to>|<duration>,
            // add-cond|<sw>|<i>|<input>, remove-cond|<sw>|<i>|<j>, set-cond|<sw>|<i>|<j>|<input>|<op>|<value>,
            // new|-, remove|<sw> (only when unused), add-stop|<sw>|<state>|<tree>, remove-stop|<sw>|<state>|<tree>
            foreach (var item in (CliArgs.Get("-ftSwitchEdit") ?? "").Split(';').Where(x => x.Contains("|")))
            {
                var f = item.Split('|');
                double D(string v) => double.Parse(v, CultureInfo.InvariantCulture);
                object result = f[0] switch
                {
                    "rename" => SwitchEdit.Rename(expression, f[1], f[2]),
                    "state" => SwitchEdit.RenameState(expression, f[1], f[2], f[3]),
                    "add-state" => SwitchEdit.AddState(expression, f[1]),
                    "remove-state" => SwitchEdit.RemoveState(expression, f[1], f[2]),
                    "add-tr" => Do(() => SwitchEdit.AddTransition(expression, f[1], f[2])),
                    "remove-tr" => Do(() => SwitchEdit.RemoveTransition(expression, f[1], int.Parse(f[2]))),
                    "set-tr" => Do(() => SwitchEdit.SetTransition(expression, f[1], int.Parse(f[2]), f[3], f[4], D(f[5]))),
                    "add-cond" => Do(() => SwitchEdit.AddCondition(expression, f[1], int.Parse(f[2]), f[3])),
                    "remove-cond" => SwitchEdit.RemoveCondition(expression, f[1], int.Parse(f[2]), int.Parse(f[3])),
                    "set-cond" => Do(() => SwitchEdit.SetCondition(expression, f[1], int.Parse(f[2]), int.Parse(f[3]), f[4], f[5], D(f[6]))),
                    "new" => SwitchEdit.NewSwitch(expression, false, ExpressionGraph.SplitInputs(doc.Json(TableDocument.Input), expression, null).FirstOrDefault()),
                    "remove" => SwitchEdit.Remove(expression, f[1]),
                    "add-stop" => SwitchEdit.AddStop(expression, f[1], f[2], f[3]),
                    "remove-stop" => SwitchEdit.RemoveStop(expression, f[1], f[2], f[3]),
                    _ => "unknown op",
                };
                Debug.Log($"[FTG] graph switch-edit {item}: {result}");
            }
            if (CliArgs.Get("-ftSwitchEdit") != null)
            {
                Debug.Log($"[FTG] graph switches: {expression["switches"]?.ToString(Newtonsoft.Json.Formatting.None)}");
                foreach (var sw in SwitchEdit.Written(expression))
                    Debug.Log($"[FTG] graph switch-list {sw}: users={string.Join(",", SwitchEdit.Users(expression, sw))}");
                foreach (var t in ExpressionGraph.TreeNames(expression))
                    foreach (var b in ExpressionGraph.Build(expression, t).Where(b => b.Kind == "switch"))
                        Debug.Log($"[FTG] graph switch-box-now {t} {b.Path}: {b.Title} | {string.Join("; ", b.Rows.Select(r => r.Label))}");
            }
            // The stops of each switch state as the switch panel lists them (also with no edit, to check a saved table).
            foreach (var sw in SwitchEdit.Written(expression))
                foreach (var (state, trees) in SwitchEdit.Stops(expression, sw))
                    Debug.Log($"[FTG] graph switch-stops {sw} {state}: {string.Join(",", trees)}");
            if (CliArgs.Get("-ftSave") == "1")
                foreach (var p in doc.Save()) Debug.Log($"[FTG] graph saved {p}");
            return 0;
        });

        static string Do(System.Action a) { a(); return "done"; }

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
