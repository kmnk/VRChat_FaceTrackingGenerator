using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // The table's recordings without the windows (design.md "確かめの入口"): per side the files and keys, per key of
        // -ftKeys "K1,K2" its values and the side's factor, the remap of -ftRemap "<key>:<value>:<vr|desktop>;...", each
        // condition's values on the main and the other side (Thresholds), and the sets and transitions the generator makes.
        public static void Measurements() => Cli.Run(() =>
        {
            var m = TableLoader.Load(CliArgs.Require("-ftTable"));
            var ms = m.Measurements;
            string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            foreach (var w in m.Warnings) Debug.Log("[FTG] warning: " + w);
            Debug.Log($"[FTG] measurements main={ms.Main}");
            foreach (var f in ms.Files)
                Debug.Log($"[FTG] measurements file {f.File} side={f.Side} device={f.Device ?? "-"} keys={f.Keys} problem={f.Problem ?? "none"}");
            foreach (var name in Kmnk.FaceTrackingGenerator.Measurements.Sides)
            {
                var s = ms.Of(name);
                // warnings: the side's files that were not used or had lines skipped.
                var warned = ms.Files.Count(f => f.Side == name && (f.Problem != null || f.BadLines > 0));
                Debug.Log(s == null
                    ? $"[FTG] measurements side={name} files=0 keys=0 usable=0 warnings={warned}"
                    : $"[FTG] measurements side={name} files={s.Files.Count} keys={s.Keys.Count(k => k.Value.Sent)} usable={s.Keys.Keys.Count(k => s.Usable(k) != null)} warnings={warned}");
            }
            foreach (var key in (CliArgs.Get("-ftKeys") ?? "").Split(',').Where(x => x != ""))
                foreach (var name in Kmnk.FaceTrackingGenerator.Measurements.Sides)
                {
                    var s = ms.Of(name);
                    if (s == null) continue;
                    var k = s.Key(key);
                    var u = s.Usable(key);
                    Debug.Log($"[FTG] measurements key {key} side={name} sent={k.Sent.ToString().ToLowerInvariant()} neutral={N(k.Neutral)} max={N(k.Max)} usable={(u == null ? "-" : N(u.Value))} readMax={(k.ReadMax == null ? "-" : N(k.ReadMax.Value))} factor={N(ms.Factor(key, name))}");
                }
            foreach (var r in (CliArgs.Get("-ftRemap") ?? "").Split(';').Where(x => x != ""))
            {
                var p = r.Split(':');
                var from = p[2];
                var (v, reason) = ms.Remap(p[0], float.Parse(p[1], CultureInfo.InvariantCulture), from, Kmnk.FaceTrackingGenerator.Measurements.Other(from));
                Debug.Log($"[FTG] measurements remap {p[0]} {from}->{Kmnk.FaceTrackingGenerator.Measurements.Other(from)} {p[1]} -> {(v != null ? N(v.Value) : "reason=" + reason)}");
            }
            foreach (var sm in m.StateMachines)
                for (var ti = 0; ti < sm.Transitions.Count; ti++)
                {
                    var t = sm.Transitions[ti];
                    for (var ci = 0; ci < t.When.Count; ci++)
                    {
                        var c = t.When[ci];
                        var r = Thresholds.For(ms, c, ms.OtherSide);
                        var talk = Kmnk.FaceTrackingGenerator.Measurements.Sides.Select(sd => (sd, v: ms.HasBoth ? Thresholds.Talk(ms, c, sd) : null))
                            .Where(x => x.v != null).Select(x => x.sd + ":" + N(x.v.Value)).ToList();
                        var kind = r.Kind == Thresholds.Kind.OutOfRange ? (r.Never ? "out" : "always") : r.Kind.ToString().ToLowerInvariant();
                        Debug.Log($"[FTG] measurements threshold {sm.Name}/{t.From}->{t.To}#{ti}/{ci} {c.Input}{c.Op} main={N(c.Value)} other={(r.Value == null ? "never" : N(r.Value.Value))} kind={kind} talk={(talk.Count > 0 ? string.Join(",", talk) : "-")}");
                    }
                }
            var sets = NativeSets.Build(m);
            var over = sets.Where(x => x.Side == ms.OtherSide).SelectMany(x => x.Rows).Select(x => x.Key).Distinct()
                .Where(k => ms.Factor(k, ms.OtherSide) > 1).OrderBy(NativeKeys.Index).ToList();
            var trs = m.StateMachines.SelectMany(sm => sm.Transitions).ToList();
            Debug.Log($"[FTG] measurements sets={sets.Count} otherSets={sets.Count(x => x.Side == ms.OtherSide)} rows={sets.Sum(x => x.Rows.Count)} transitions={trs.Count} splitTransitions={trs.Count(t => Thresholds.Splits(ms, t))} factorsOver1={(over.Count > 0 ? string.Join(",", over) : "none")}");
            return 0;
        });
    }
}
