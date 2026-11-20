using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // The table's recordings without the windows: per side the files and keys, per key of -ftKeys "K1,K2" its values,
        // and the sets and transitions the generator makes per device (the recordings do not change them).
        public static void Measurements() => Cli.Run(() =>
        {
            var m = TableLoader.Load(CliArgs.Require("-ftTable"));
            var ms = m.Measurements;
            string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            foreach (var w in m.Warnings) Debug.Log("[FTG] warning: " + w);
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
                    Debug.Log($"[FTG] measurements key {key} side={name} sent={k.Sent.ToString().ToLowerInvariant()} neutral={N(k.Neutral)} max={N(k.Max)} usable={(u == null ? "-" : N(u.Value))} readMax={(k.ReadMax == null ? "-" : N(k.ReadMax.Value))}");
                }
            var sets = NativeSets.Build(m);
            var trs = m.StateMachines.SelectMany(sm => sm.Transitions).ToList();
            Debug.Log($"[FTG] measurements sets={sets.Count} desktopSets={sets.Count(x => x.Side == Device.Desktop)} rows={sets.Sum(x => x.Rows.Count)} transitions={trs.Count} vrTransitions={trs.Count(t => t.Device == Device.Vr)} desktopTransitions={trs.Count(t => t.Device == Device.Desktop)}");
            return 0;
        });
    }
}
