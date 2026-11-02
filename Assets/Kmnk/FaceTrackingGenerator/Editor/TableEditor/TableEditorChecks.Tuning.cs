using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints what the Tuning tab shows for -ftTable, applies its edits the way the tab does, and saves with -ftSave 1.
        //   -ftSetPosition "<tree>/<node>/<position>=<value>;..."   node and position count from 0 in the order printed
        //   -ftSetSmoothing "<name>/local=<ms>;<name>/remote=<ms>"
        //   -ftTry "<value>,..."                                     weights of the face for the first node at the values
        // Lines:
        //   [FTG] tuning tree <name>: title=<text> nodes=<n> has2d=<bool>
        //   [FTG] tuning node <tree>/<i>: <title> | positions=<p,...> | <movements at each position>
        //   [FTG] tuning 2d: <names>
        //   [FTG] tuning smoothing <name>: local=<ms> remote=<ms> | <users>
        //   [FTG] tuning set <tree>/<i>/<j>: asked=<v> written=<v>
        //   [FTG] tuning try <tree>/<i> x=<v>: <mesh/shape>=<weight> ...
        //   [FTG] tuning saved <path>
        //   [FTG] tuning reloaded <tree>/<i>: positions=<p,...> ; smoothing <name>: localTau=<t> remoteTau=<t>
        public static void Tuning() => Cli.Run(() =>
        {
            EditorText.Override = CliArgs.Get("-ftHelp") == "en" ? EditorText.Language.English : EditorText.Language.Japanese;
            var path = CliArgs.Require("-ftTable");
            var doc = TableDocument.Open(path);
            var model = doc.Model();
            var trees = Kmnk.FaceTrackingGenerator.Tuning.Trees(doc.Json(TableDocument.Expression));
            Print(trees, model, doc);

            var set = CliArgs.Get("-ftSetPosition");
            if (set != null)
                foreach (var item in set.Split(';').Where(x => x.Contains("=")))
                {
                    var (key, value) = Split(item);
                    var parts = key.Split('/');
                    var tree = trees.First(t => t.Name == parts[0]);
                    var node = tree.Nodes[int.Parse(parts[1])];
                    var written = Kmnk.FaceTrackingGenerator.Tuning.SetPosition(node, int.Parse(parts[2]), value);
                    Debug.Log($"[FTG] tuning set {key}: asked={F(value)} written={F(written)}");
                }
            var smooth = CliArgs.Get("-ftSetSmoothing");
            if (smooth != null)
                foreach (var item in smooth.Split(';').Where(x => x.Contains("=")))
                {
                    var (key, value) = Split(item);
                    var parts = key.Split('/');
                    var def = Kmnk.FaceTrackingGenerator.Tuning.Smoothings(doc.Json(TableDocument.Input)).First(s => s.name == parts[0]).def;
                    var tau = Kmnk.FaceTrackingGenerator.Tuning.SetMs(def, parts[1] == "local", (int)value);
                    Debug.Log($"[FTG] tuning set smoothing {key}: ms={(int)value} tau={tau.ToString(CultureInfo.InvariantCulture)}");
                }
            model = doc.Model();  // the edited tables must still load

            var tryValues = CliArgs.Get("-ftTry");
            if (tryValues != null)
            {
                var tree = trees.First(t => t.Nodes.Count > 0);
                foreach (var s in tryValues.Split(','))
                {
                    var x = float.Parse(s, CultureInfo.InvariantCulture);
                    var w = Kmnk.FaceTrackingGenerator.Tuning.TryWeights(model, tree, tree.Nodes[0], x, null);
                    Debug.Log($"[FTG] tuning try {tree.Name}/0 x={F(x)}: " + string.Join(" ", w.Where(kv => kv.Value != 0).Select(kv => $"{kv.Key}={F(kv.Value)}")));
                }
            }

            if (CliArgs.Get("-ftSave") == "1")
            {
                foreach (var p in doc.Save()) Debug.Log("[FTG] tuning saved " + p);
                var again = TableDocument.Open(path);
                var reloaded = TableLoader.Load(path);
                foreach (var t in Kmnk.FaceTrackingGenerator.Tuning.Trees(again.Json(TableDocument.Expression)))
                    for (int i = 0; i < t.Nodes.Count; i++)
                        Debug.Log($"[FTG] tuning reloaded {t.Name}/{i}: positions=" + string.Join(",", Enumerable.Range(0, t.Nodes[i].Children.Count).Select(j => F(t.Nodes[i].Position(j)))));
                foreach (var s in reloaded.Smoothings)
                    Debug.Log($"[FTG] tuning reloaded smoothing {s.Name}: localTau={F(s.LocalTau)} remoteTau={F(s.RemoteTau)}");
                // The expanded model: the first position of each 1D node per tree, to see that both sides took the change.
                foreach (var t in reloaded.Trees)
                {
                    var firsts = new List<string>();
                    void Walk(Node n, string at)
                    {
                        switch (n)
                        {
                            case Blend1DNode b: firsts.Add($"{at}{b.Input}@{F(b.Children[0].threshold)}"); foreach (var c in b.Children) Walk(c.node, at); break;
                            case Blend2DNode b: foreach (var c in b.Children) Walk(c.node, at); break;
                            case SwitchNode sw: foreach (var c in sw.Cases) Walk(c.node, $"{at}{c.state}:"); break;
                        }
                    }
                    Walk(t.Root, "");
                    if (firsts.Count > 0) Debug.Log($"[FTG] tuning model {t.Name}: " + string.Join(" ", firsts));
                }
            }
            return 0;
        });

        static void Print(List<Kmnk.FaceTrackingGenerator.Tuning.Tree> trees, TableModel model, TableDocument doc)
        {
            foreach (var t in trees)
            {
                Debug.Log($"[FTG] tuning tree {t.Name}: title={Kmnk.FaceTrackingGenerator.Tuning.TreeTitle(t)} nodes={t.Nodes.Count} has2d={t.Has2D.ToString().ToLower()}");
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    var n = t.Nodes[i];
                    var positions = Enumerable.Range(0, n.Children.Count).ToList();
                    Debug.Log($"[FTG] tuning node {t.Name}/{i}: {Kmnk.FaceTrackingGenerator.Tuning.NodeTitle(n)} | positions=" +
                              string.Join(",", positions.Select(j => F(n.Position(j)))) + " | " +
                              string.Join(" / ", positions.Select(j => Kmnk.FaceTrackingGenerator.Tuning.ChildText(n.Child(j)))));
                }
            }
            var only2d = trees.Where(t => t.Nodes.Count == 0 && t.Has2D).Select(t => Kmnk.FaceTrackingGenerator.Tuning.Label(t.Name)).ToList();
            Debug.Log("[FTG] tuning 2d: " + string.Join(",", only2d));
            foreach (var (name, def) in Kmnk.FaceTrackingGenerator.Tuning.Smoothings(doc.Json(TableDocument.Input)))
            {
                var users = model.Inputs.Where(i => i.Smoothing == name).Select(i => i.Name);
                Debug.Log($"[FTG] tuning smoothing {name}: local={Kmnk.FaceTrackingGenerator.Tuning.Ms(def["localTau"]?.Value<float>() ?? 0)} " +
                          $"remote={Kmnk.FaceTrackingGenerator.Tuning.Ms(def["remoteTau"]?.Value<float>() ?? 0)} | {Kmnk.FaceTrackingGenerator.Tuning.Uses(users)}");
            }
        }

        static (string key, float value) Split(string item)
        {
            var k = item.LastIndexOf('=');
            return (item.Substring(0, k).Trim(), float.Parse(item.Substring(k + 1), CultureInfo.InvariantCulture));
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
