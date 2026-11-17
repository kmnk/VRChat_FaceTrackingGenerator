using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The Devices tab (spec R14-R17, design.md "Devices タブ"): the recordings and the main side, the conditions' values
    // on both sides, and how the strength changes. The Preview's sliders with the device's range (spec R19, R20).
    public sealed partial class TableEditorWindow
    {
        static readonly Color NoteDark = new Color(0.90f, 0.71f, 0f), NoteLight = new Color(0.54f, 0.43f, 0f);
        static readonly Color OrangeDark = new Color(0.75f, 0.50f, 0.25f), OrangeLight = new Color(0.60f, 0.35f, 0.06f);
        static Color NoteColor => EditorGUIUtility.isProSkin ? NoteDark : NoteLight;
        static Color OrangeColor => EditorGUIUtility.isProSkin ? OrangeDark : OrangeLight;
        static Color BlockColor => EditorGUIUtility.isProSkin ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.80f, 0.80f, 0.80f);
        static Color LineColor => EditorGUIUtility.isProSkin ? new Color(0.33f, 0.33f, 0.33f) : new Color(0.62f, 0.62f, 0.62f);

        static GUIStyle Colored(GUIStyle baseStyle, Color c)
        {
            var s = new GUIStyle(baseStyle);
            s.normal.textColor = s.hover.textColor = s.focused.textColor = s.active.textColor = c;
            return s;
        }

        const float ValueWidth = 92, Indent = 16;

        void DrawDevicesTab()
        {
            var avatar = doc.Json(TableDocument.Avatar);
            DrawRecordings(avatar);
            if (model == null) return;
            var ms = model.Measurements;
            DrawThresholds(ms);
            EditorGUILayout.Space(8);
            Heading("Strength");
            EditorGUILayout.LabelField(StrengthText(ms), EditorStyles.wordWrappedMiniLabel);
        }

        // ---- Recordings: the files assigned to VR or desktop, and the main side (spec R1, R2, R6)

        void DrawRecordings(JObject avatar)
        {
            Heading("Recordings");
            Help("devices.recordings");
            var main = Measurements.Sides.Contains(Measurements.Text(avatar["measurementsMain"])) ? Measurements.Text(avatar["measurementsMain"]) : Measurements.Vr;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Main", GUILayout.Width(70));
                EditorGUI.BeginChangeCheck();
                var i = GUILayout.Toolbar(Array.IndexOf(Measurements.Sides, main), Measurements.Sides.Select(Measurements.Label).ToArray(), GUILayout.Width(220));
                if (EditorGUI.EndChangeCheck()) { var side = Measurements.Sides[i]; Edit("Set FT main device", a => TableEdits.SetMeasurementsMain(a, side)); }
                GUILayout.FlexibleSpace();
            }
            var list = avatar["measurements"] as JArray ?? new JArray();
            var files = model?.Measurements.Files ?? new List<Measurements.FileEntry>();
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i] as JObject;
                var file = Measurements.Text(o?["file"]) ?? "?";
                var side = Measurements.Text(o?["side"]) ?? "?";
                var entry = files.FirstOrDefault(f => f.File == file && f.Side == side);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Measurements.Sides.Contains(side) ? Measurements.Label(side) : side, GUILayout.Width(70));
                    GUILayout.Label(new GUIContent(Path.GetFileName(file), file), GUILayout.ExpandWidth(true));
                    var info = entry == null ? "not read" : entry.Problem ?? $"{entry.Keys} keys";
                    GUILayout.Label(new GUIContent(info, entry?.Problem ?? entry?.Device), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        var index = i;
                        Edit("Remove FT recording", a => TableEdits.RemoveMeasurement(a, index));
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (list.Count == 0)
                EditorGUILayout.LabelField(string.Format(EditorText.Get("devices.noRecords"), Measurements.Label(main), Measurements.Label(Measurements.Other(main))), EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var side in Measurements.Sides)
                    if (GUILayout.Button("Add " + Measurements.Label(side) + " recording", GUILayout.ExpandWidth(false)))
                    {
                        var src = EditorUtility.OpenFilePanel("ft-measure recording (" + Measurements.Label(side) + ")", "", "jsonl");
                        if (!string.IsNullOrEmpty(src))
                        {
                            var dir = Path.GetDirectoryName(TablePath);
                            try { Edit("Add FT recording", a => TableEdits.AddMeasurement(a, dir, src, side)); }
                            catch (Exception e) { EditorUtility.DisplayDialog("FT Settings Editor", e.Message, "OK"); }
                            AssetDatabase.Refresh();
                        }
                        GUIUtility.ExitGUI();
                    }
                GUILayout.FlexibleSpace();
            }
        }

        // ---- Thresholds: every condition with its value on the main and the other side (spec R15-R17)

        // The transitions of a state machine by (from, to), in the order they first appear.
        static List<List<TransitionDef>> Groups(StateMachineDef sm) =>
            sm.Transitions.GroupBy(t => (t.From, t.To)).Select(g => g.ToList()).ToList();

        void DrawThresholds(Measurements ms)
        {
            EditorGUILayout.Space(8);
            Heading("Thresholds");
            var main = Measurements.Label(ms.Main);
            var other = Measurements.Label(ms.OtherSide);
            EditorGUILayout.LabelField(string.Format(EditorText.Get(ms.HasBoth ? "devices.thresholds" : "devices.thresholdsSame"), main, other), EditorStyles.wordWrappedMiniLabel);
            if (model.StateMachines.Count == 0) { EditorGUILayout.LabelField(EditorText.Get("devices.noMachines"), EditorStyles.miniLabel); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Condition", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                var head = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
                GUILayout.Label(main + " (Main)", head, GUILayout.Width(ValueWidth));
                GUILayout.Label(other + " (" + EditorText.Get("devices.computed") + ")", head, GUILayout.Width(ValueWidth));
                GUILayout.Space(16);
            }
            var right = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
            var faint = Colored(right, new Color(right.normal.textColor.r, right.normal.textColor.g, right.normal.textColor.b, 0.5f));
            var note = Colored(EditorStyles.wordWrappedMiniLabel, NoteColor);
            foreach (var sm in model.StateMachines)
            {
                if (model.StateMachines.Count > 1) EditorGUILayout.LabelField(sm.Name, EditorStyles.miniBoldLabel);
                foreach (var group in Groups(sm))
                {
                    // A block per (from, to) on a lighter background; a short line between its transitions (spec R15).
                    var block = EditorGUILayout.BeginVertical();
                    if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(block, BlockColor);
                    GUILayout.Space(3);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(6);
                        GUILayout.Label($"{group[0].From} → {group[0].To}", EditorStyles.boldLabel);
                    }
                    for (var ti = 0; ti < group.Count; ti++)
                    {
                        if (ti > 0)
                        {
                            var line = GUILayoutUtility.GetRect(1, 7, GUILayout.ExpandWidth(true));
                            if (Event.current.type == EventType.Repaint)
                                EditorGUI.DrawRect(new Rect(block.x + Indent + 6, line.y + 3, block.width - Indent - 6, 1), LineColor);
                        }
                        foreach (var c in group[ti].When)
                        {
                            var r = Thresholds.For(ms, c, ms.OtherSide);
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(Indent + 6);
                                GUILayout.Label($"{c.Input} {c.Op}");
                                GUILayout.FlexibleSpace();
                                GUILayout.Label(ThresholdText.Value(c.Value), right, GUILayout.Width(ValueWidth));
                                GUILayout.Label(r.Value == null ? "—" : ThresholdText.Value(r.Value.Value), ms.HasBoth ? right : faint, GUILayout.Width(ValueWidth));
                                GUILayout.Space(16);
                            }
                            foreach (var n in ThresholdText.Notes(ms, c))
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    GUILayout.Space(Indent + 6);
                                    GUILayout.Label("⚠ " + n, note);
                                    GUILayout.Space(8);
                                }
                        }
                    }
                    GUILayout.Space(4);
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(6);
                }
            }
        }

        // ---- Strength (spec R8)

        string StrengthText(Measurements ms)
        {
            if (!ms.HasBoth) return EditorText.Get("devices.strengthSame");
            var other = Measurements.Label(ms.OtherSide);
            // The example: the key the other side's sets scale most (either way), without Left/Right.
            var keys = NativeSets.Build(model).Where(s => s.Side == ms.OtherSide).SelectMany(s => s.Rows).Select(r => r.Key).Distinct()
                .Where(k => Math.Abs(ms.Factor(k, ms.OtherSide) - 1) > 1e-3f)
                .OrderByDescending(k => Math.Abs(Math.Log(ms.Factor(k, ms.OtherSide)))).ThenBy(k => k, StringComparer.Ordinal).ToList();
            if (keys.Count == 0) return string.Format(EditorText.Get("devices.strengthPlain"), Measurements.Label(ms.Main), other);
            var k0 = keys[0];
            var name = k0.EndsWith("Left") ? k0.Substring(0, k0.Length - 4) : k0.EndsWith("Right") ? k0.Substring(0, k0.Length - 5) : k0;
            return string.Format(EditorText.Get("devices.strength"), Measurements.Label(ms.Main), other, name, ms.Factor(k0, ms.OtherSide).ToString("0.0", CultureInfo.InvariantCulture));
        }

        // ---- Preview: the device's range under a slider (spec R19)

        enum InputMark { None, Over, Under, NotSent }

        // The device's range of an input (null: not sent) and how the value stands against it.
        (InputMark mark, (float lo, float hi)? range) Mark(InputDef input, Devices.Device device, float v)
        {
            if (device == null) return (InputMark.None, null);
            var r = Devices.InputRange(model, input, device);
            if (r == null) return (InputMark.NotSent, null);
            const float eps = 0.005f;  // the number field shows two places
            // Under: only a value that was moved (0 is where every slider starts, and many keys rest above 0).
            return (v > r.Value.hi + eps ? InputMark.Over : v > eps && v < r.Value.lo - eps ? InputMark.Under : InputMark.None, r);
        }

        static string ShortName(Devices.Device d) =>
            d.Id == Devices.MeasuredVr ? Measurements.Label(Measurements.Vr) : d.Id == Devices.MeasuredDesktop ? Measurements.Label(Measurements.Desktop) : d.Name;

        // A tracking key's slider: always the thresholds' range (0 to 1 at least), the number colored when the device
        // cannot send the value, and below it the parts it moves and the device's range.
        void DrawInputSlider(InputDef input, Devices.Device device, string parts, ref bool changed)
        {
            var (lo, hi) = SliderRange(input.Name);
            var v = inputs.TryGetValue(input.Name, out var x) ? x : 0f;
            var (mark, range) = Mark(input, device, v);
            var number = mark == InputMark.Over || mark == InputMark.Under ? Colored(EditorStyles.numberField, NoteColor)
                : mark == InputMark.NotSent && v > 0.005f ? Colored(EditorStyles.numberField, OrangeColor) : EditorStyles.numberField;
            using (new EditorGUI.DisabledScope(LiveRunning))
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PrefixLabel(new GUIContent(input.Name, ParameterHelp.Tooltip(model, input)));
                var nv = GUILayout.HorizontalSlider(v, lo, hi);
                nv = EditorGUILayout.FloatField(nv, number, GUILayout.Width(50));
                if (EditorGUI.EndChangeCheck()) { inputs[input.Name] = (float)Math.Round(Mathf.Clamp(nv, lo, hi), 2); changed = true; }
            }
            if (device == null) { if (parts != null) PartsLine(parts); return; }
            var name = ShortName(device);
            string text; GUIStyle style = PartsStyle;
            switch (mark)
            {
                case InputMark.NotSent: text = string.Format(EditorText.Get("input.notSent"), name); style = Colored(PartsStyle, OrangeColor); break;
                case InputMark.Over: text = string.Format(EditorText.Get("input.over"), name, range.Value.hi.ToString("0.00", CultureInfo.InvariantCulture)); style = Colored(PartsStyle, NoteColor); break;
                case InputMark.Under: text = string.Format(EditorText.Get("input.under"), name, range.Value.lo.ToString("0.00", CultureInfo.InvariantCulture)); style = Colored(PartsStyle, NoteColor); break;
                default: text = string.Format(EditorText.Get("input.range"), name, range.Value.lo.ToString("0.00", CultureInfo.InvariantCulture), range.Value.hi.ToString("0.00", CultureInfo.InvariantCulture)); break;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (parts != null) GUILayout.Label(parts + (EditorText.Current == EditorText.Language.Japanese ? " ・" : " ·"), PartsStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label(text, style, GUILayout.ExpandWidth(true));
            }
        }

        // The line of what the Preview runs as (spec R20).
        string SideLine()
        {
            var ms = model.Measurements;
            var side = PreviewDesktop ? Measurements.Desktop : Measurements.Vr;
            var key = side == ms.Main ? "preview.sideMain" : ms.HasBoth ? "preview.sideComputed" : "preview.sideWaiting";
            return string.Format(EditorText.Get(key), Measurements.Label(side), Measurements.Label(ms.Main));
        }

        // A line on a bluish background (the Preview's side).
        static void SideBox(string text)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { padding = new RectOffset(6, 6, 4, 4) };
            var r = GUILayoutUtility.GetRect(new GUIContent(text), style, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.23f, 0.29f, 0.35f) : new Color(0.78f, 0.85f, 0.93f));
            GUI.Label(r, text, style);
        }

        // ---- TableEditorChecks.Capture

        internal IEnumerable<string> DevicesCaptureLines()
        {
            if (model == null) yield break;
            var ms = model.Measurements;
            if (tab == 2)
            {
                var conds = model.StateMachines.SelectMany(sm => sm.Transitions.SelectMany(t => t.When)).ToList();
                yield return $"[FTG] capture devices main={ms.Main} groups={model.StateMachines.Sum(sm => Groups(sm).Count)} notes={conds.Sum(c => ThresholdText.Notes(ms, c).Count)} other={string.Join(",", conds.Select(c => { var r = Thresholds.For(ms, c, ms.OtherSide); return r.Value == null ? "never" : ThresholdText.Value(r.Value.Value); }))} strength=\"{StrengthText(ms)}\"";
            }
            if (tab == 1)
            {
                var devices = Devices.Load(model);
                var device = devices.FirstOrDefault(d => d.Id == (captureDevice ?? EditorPrefs.GetString(DevicePrefKey, "")));
                yield return $"[FTG] capture side \"{SideLine()}\"";
                foreach (var input in model.Inputs.Where(i => i.IsTrack))
                {
                    var (lo, hi) = SliderRange(input.Name);
                    var v = inputs.TryGetValue(input.Name, out var x) ? x : 0f;
                    var (mark, range) = Mark(input, device, v);
                    if (v == 0 && mark == InputMark.None) continue;
                    yield return $"[FTG] capture input {input.Key} value={v.ToString("0.##", CultureInfo.InvariantCulture)} slider={lo.ToString("0.##", CultureInfo.InvariantCulture)}-{hi.ToString("0.##", CultureInfo.InvariantCulture)} range={(range == null ? "-" : range.Value.lo.ToString("0.##", CultureInfo.InvariantCulture) + "-" + range.Value.hi.ToString("0.##", CultureInfo.InvariantCulture))} mark={mark.ToString().ToLowerInvariant()}";
                }
            }
        }
    }
}
