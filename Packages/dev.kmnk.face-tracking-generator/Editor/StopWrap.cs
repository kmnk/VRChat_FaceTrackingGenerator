using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Makes the trees a switch state stops ("stops" in the expression table) add nothing in that state: after Resolve,
    // wraps each such tree's root in a node of that switch whose stopping states are empty poses and whose other states
    // keep the root. Everything that reads switch nodes (the generator, CurveEval, the preview) then stops the tree the
    // same way. Stops of a switch, state or tree that Resolve dropped are not applied and are listed in Dropped.
    public static class StopWrap
    {
        public static void Apply(TableModel m)
        {
            foreach (var t in m.Trees) t.BaseRoot = t.Root;

            var live = new List<StopDef>();
            void Drop(string msg) { if (!m.Dropped.Contains(msg)) m.Dropped.Add(msg); }
            foreach (var s in m.Stops)
            {
                var sw = m.Switch(s.Switch);
                if (sw == null) Drop($"switch {s.Switch}: stops are not applied: the switch was dropped");
                else if (!sw.States.Contains(s.State)) Drop($"switch {s.Switch}: stops of state {s.State} are not applied: the state was dropped");
                else if (m.Trees.All(t => t.Name != s.Tree)) Drop($"switch {s.Switch}: stops {s.Tree} is not applied: the tree was dropped");
                else live.Add(s);
            }

            // Switches in table order; a tree stopped by several switches is wrapped once per switch (nested).
            foreach (var sw in m.Switches)
                foreach (var g in live.Where(s => s.Switch == sw.Name).GroupBy(s => s.Tree))
                {
                    var tree = m.Trees.First(t => t.Name == g.Key);
                    var stopped = new HashSet<string>(g.Select(s => s.State));
                    var wrap = new SwitchNode { Switch = sw.Name };
                    foreach (var st in sw.States) wrap.Cases.Add((st, stopped.Contains(st) ? new PoseNode() : tree.Root));
                    tree.Root = wrap;
                }
        }
    }
}
