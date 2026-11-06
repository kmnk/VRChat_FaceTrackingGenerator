using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints what the settings editor shows as guidance, in Japanese (-ftHelp en for English):
        //   [FTG] usage <movement>: <what moves it>
        //   [FTG] preset <name>: <input>=<value> ...
        //   [FTG] overlap <preset>: <message on one line> | none
        //   [FTG] device <id>: keys=<n> settings-inputs-sent=<a>/<b> not-sent=<names> unused=<n>
        public static void Guidance() => Cli.Run(() =>
        {
            EditorText.Override = CliArgs.Get("-ftHelp") == "en" ? EditorText.Language.English : EditorText.Language.Japanese;
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var usage = PartUsage.Of(model);
            foreach (var kv in usage) Debug.Log($"[FTG] usage {kv.Key}: {PartUsage.Text(kv.Value)}");
            var tracker = new SwitchTracker();
            foreach (var (preset, values) in Presets.For(model))
            {
                Debug.Log($"[FTG] preset {preset.Label}: " + string.Join(" ", values.Select(kv => $"{kv.Key}={kv.Value}")));
                tracker.Reset();
                var states = tracker.Update(model, values);
                var found = Overlaps.Find(CurveEval.EvaluateParts(model, values, states));
                Debug.Log($"[FTG] overlap {preset.Label}: " + (found.Count == 0 ? "none" : string.Join(" || ", found.Select(o => Overlaps.Message(o, usage).Replace("\n", " ")))));
            }
            var set = CliArgs.Get("-ftSet");
            if (set != null)
            {
                var values = set.Split(',').Select(x => x.Split('=')).ToDictionary(x => x[0].Trim(), x => float.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture));
                tracker.Reset();
                var found = Overlaps.Find(CurveEval.EvaluateParts(model, values, tracker.Update(model, values)));
                Debug.Log($"[FTG] overlap -ftSet {set}: " + (found.Count == 0 ? "none" : string.Join(" || ", found.Select(o => Overlaps.Message(o, usage).Replace("\n", " ")))));
            }
            foreach (var d in Devices.Load())
            {
                var direct = model.Inputs.Where(i => !i.IsDerived).ToList();
                var sent = direct.Where(i => d.Sends(i.Key, i.Expression)).ToList();
                var notSent = direct.Where(i => !d.Sends(i.Key, i.Expression)).Select(i => i.Name).ToList();
                var unused = d.Keys.Count(k => !Devices.Device.UsedBy(model, k.key));
                Debug.Log($"[FTG] device {d.Id}: keys={d.Keys.Count} settings-inputs-sent={sent.Count}/{direct.Count} not-sent={string.Join(",", notSent)} unused={unused} tongue-xy={d.Has("v2/TongueX") && d.Has("v2/TongueY")}");
                string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                foreach (var i in model.Inputs)
                {
                    var t = Devices.ThresholdRange(model, i.Name);
                    var s = Devices.Slider(model, i, d, t);
                    Debug.Log($"[FTG] device {d.Id} input {i.Name}: sent={s.sent.ToString().ToLower()}" + (s.sent ? $" range={F(s.device.lo)}..{F(s.device.hi)} slider={F(s.lo)}..{F(s.hi)} clipped={s.clipped.ToString().ToLower()}" : ""));
                }
            }
            return 0;
        });
    }
}
