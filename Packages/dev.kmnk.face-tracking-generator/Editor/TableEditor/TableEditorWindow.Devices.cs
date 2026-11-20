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
    // The Devices tab (spec R19, R19a, design.md "Devices タブ"): the recordings, and a list of a recorded face's keys.
    // The Preview's sliders with the device's range.
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
            DrawRecordedFaces(model.Measurements);
        }

        // ---- Recordings: the files assigned to VR or desktop

        void DrawRecordings(JObject avatar)
        {
            Heading("Recordings");
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
            if (list.Count == 0) EditorGUILayout.LabelField(EditorText.Get("devices.noRecords"), EditorStyles.wordWrappedLabel);
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

        // ---- Recorded faces: per key of a recorded face, its range there and with a neutral face (spec R19a)

        string facesId;  // the list's face
        // The list's device: the State Machine window's input mode (spec R19a), or the other one when it has no recording.
        string facesSide;
        static readonly Color FaceBar = new Color(0x5b / 255f, 0x8f / 255f, 0xc9 / 255f);
        static readonly Color NeutralBar = new Color(0x8a / 255f, 0x8a / 255f, 0x8a / 255f);
        // Darker than the window (0x38 in the dark skin), so that the 0 to 1 range shows.
        static readonly Color Groove = new Color(0x22 / 255f, 0x22 / 255f, 0x22 / 255f);
        static readonly Color FaintBar = new Color(0x4a / 255f, 0x4a / 255f, 0x4a / 255f);
        const float FacesMinGap = 0.02f;  // a key whose face is less than this above neutral is faint (as Measurements.MinUsable)

        // The faces of a recording to list: the poses in the order they were recorded, without neutral and the readings.
        static List<string> FaceIds(Measurements.Side s) =>
            s.Items.Select(i => i.Id).Where(id => id != Measurements.NeutralId && id != Measurements.ReadNeutralId && id != Measurements.ReadSmileId).ToList();

        // The rows of a face: sent keys with the neutral and the face's range, by how far the face's smallest value is above
        // the neutral face's largest (the room for a threshold), largest first.
        internal static List<(string key, (float min, float max) neutral, (float min, float max) face, float gap)> FaceRows(Measurements.Side s, string id)
        {
            var face = s.ItemOf(id);
            var neutral = s.ItemOf(Measurements.NeutralId);
            if (face == null) return new List<(string, (float, float), (float, float), float)>();
            return s.Keys.Where(kv => kv.Value.Sent).Select(kv =>
            {
                var n = neutral?.Of(kv.Key) ?? (0f, 0f);
                var f = face.Of(kv.Key);
                return (kv.Key, n, f, f.min - n.max);
            }).OrderByDescending(r => r.Item4).ThenBy(r => NativeKeys.Index(r.Key)).ToList();
        }

        // The list's device: the one chosen, or the first with a recording.
        Measurements.Side FacesSide(Measurements ms)
        {
            var mode = StateMachineWindow.SharedInputMode;
            facesSide = ms.Of(mode) != null ? mode : ms.Of(Measurements.Other(mode)) != null ? Measurements.Other(mode) : null;
            var side = facesSide == null ? null : ms.Of(facesSide);
            var ids = side == null ? new List<string>() : FaceIds(side);
            if (facesId == null || !ids.Contains(facesId)) facesId = ids.FirstOrDefault();
            return side;
        }

        void DrawRecordedFaces(Measurements ms)
        {
            var side = FacesSide(ms);
            if (side == null) return;
            EditorGUILayout.Space(10);
            Heading("Recorded faces");
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var d in Measurements.Sides)
                    using (new EditorGUI.DisabledScope(ms.Of(d) == null))
                        if (GUILayout.Toggle(d == facesSide, Measurements.Label(d), EditorStyles.miniButtonMid, GUILayout.Width(130)) && d != facesSide) { StateMachineWindow.SharedInputMode = d; side = FacesSide(ms); }
                GUILayout.FlexibleSpace();
            }
            var ids = FaceIds(side);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Face", GUILayout.Width(40));
                var i = EditorGUILayout.Popup(Math.Max(0, ids.IndexOf(facesId)), ids.ToArray(), GUILayout.Width(180));
                if (ids.Count > 0) facesId = ids[i];
                GUILayout.FlexibleSpace();
            }
            if (facesId == null) return;
            var inv = CultureInfo.InvariantCulture;
            string R((float min, float max) r) => r.min.ToString("0.00", inv) + "~" + r.max.ToString("0.00", inv);
            // The reading columns (spec R19a): only the readings the recording has.
            var talks = TalkColumns(side);
            var showBar = position.width >= FacesBarMinWindow;
            // No margins, so that the columns add up to the window's width (the scroll view's bar and padding aside).
            var small = new GUIStyle(EditorStyles.label) { fontSize = 11, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(2, 2, 2, 2) };
            var right = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            var faintColor = new Color(0x6a / 255f, 0x6a / 255f, 0x6a / 255f);
            var faint = Colored(small, faintColor);
            var faintRight = Colored(right, faintColor);
            // The key column takes what is left, at least FacesKeyMin; a longer name is cut with "…" (tooltip: the whole).
            // Narrower: the key column first (down to FacesKeyMin), then the value columns (down to FacesValueMinW).
            var avail = position.width - FacesWindowInset - (showBar ? FacesBarW + 6 : 0);
            var cols = 2 + talks.Count;
            var valueW = Mathf.Clamp((avail - FacesKeyMin) / cols, FacesValueMinW, FacesValueW);
            var keyW = Math.Max(FacesKeyMin, avail - valueW * cols);
            using (new EditorGUILayout.HorizontalScope())
            {
                var head = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(2, 2, 2, 2) };
                GUILayout.Label("Key", new GUIStyle(head) { alignment = TextAnchor.MiddleLeft }, GUILayout.Width(keyW));
                // Headings longer than the column end with "…"; the tooltip has the whole.
                void Head(string text) => GUILayout.Label(new GUIContent(Fit(text, head, valueW), text), head, GUILayout.Width(valueW));
                Head(EditorText.Get("range.neutral"));
                foreach (var (_, text) in talks) Head(EditorText.Get(text));
                Head(facesId);
                GUILayout.FlexibleSpace();
            }
            foreach (var (key, n, f, gap) in FaceRows(side, facesId))
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(20)))
                {
                    var dim = gap < FacesMinGap;
                    var keyStyle = new GUIStyle(dim ? faint : small) { clipping = TextClipping.Clip };
                    GUILayout.Label(new GUIContent(Fit(key, keyStyle, keyW), key), keyStyle, GUILayout.Width(keyW));
                    GUILayout.Label(R(n), dim ? faintRight : right, GUILayout.Width(valueW));
                    foreach (var (id, _) in talks) GUILayout.Label(R(side.ItemOf(id).Of(key)), dim ? faintRight : right, GUILayout.Width(valueW));
                    GUILayout.Label(R(f), dim ? faintRight : right, GUILayout.Width(valueW));
                    if (!showBar) { GUILayout.FlexibleSpace(); continue; }
                    GUILayout.Space(6);
                    var bar = GUILayoutUtility.GetRect(FacesBarW, 20, GUILayout.Width(FacesBarW));
                    if (Event.current.type == EventType.Repaint)
                    {
                        var groove = new Rect(bar.x, bar.center.y - 4, bar.width, 8);
                        EditorGUI.DrawRect(groove, Groove);
                        // At least 2 px wide, and inside the groove also at 1.
                        Rect Span((float min, float max) r, float h)
                        {
                            var w = Math.Max(2, (Mathf.Clamp01(r.max) - Mathf.Clamp01(r.min)) * groove.width);
                            var x = Math.Min(groove.x + Mathf.Clamp01(r.min) * groove.width, groove.xMax - w);
                            return new Rect(x, groove.center.y - h / 2, w, h);
                        }
                        EditorGUI.DrawRect(Span(f, 8), dim ? FaintBar : FaceBar);
                        EditorGUI.DrawRect(Span(n, 4), dim ? FaintBar : NeutralBar);
                    }
                    GUILayout.FlexibleSpace();
                }
        }

        // FacesWindowInset: the tab's scroll bar and the window's side padding.
        const float FacesValueW = 64, FacesValueMinW = 56, FacesBarW = 90, FacesKeyMin = 100, FacesBarMinWindow = 480, FacesWindowInset = 28;

        // The reading columns of a recording (read_neutral, read_smile), with their headings' text keys.
        static List<(string id, string text)> TalkColumns(Measurements.Side s) =>
            new[] { (Measurements.ReadNeutralId, "range.talk"), (Measurements.ReadSmileId, "range.smile") }.Where(x => s.ItemOf(x.Item1) != null).ToList();

        // The text cut to the width with "…" at the end.
        static string Fit(string text, GUIStyle style, float width)
        {
            if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            for (var n = text.Length - 1; n > 1; n--)
            {
                var t = text.Substring(0, n) + "…";
                if (style.CalcSize(new GUIContent(t)).x <= width) return t;
            }
            return "…";
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

        // The line of what the Preview runs as (spec R20): the device's transitions and strengths.
        string SideLine() => string.Format(EditorText.Get("preview.side"), Measurements.Label(PreviewDesktop ? Measurements.Desktop : Measurements.Vr));

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
                var side = FacesSide(ms);
                var rows = side == null || facesId == null ? null : FaceRows(side, facesId);
                yield return $"[FTG] capture devices recordings={ms.Files.Count} faces={(side == null ? "-" : facesSide + ":" + facesId)} rows={rows?.Count ?? 0} faint={rows?.Count(r => r.gap < FacesMinGap) ?? 0} first={(rows == null || rows.Count == 0 ? "-" : rows[0].key)} talk={(side == null ? "-" : string.Join(",", TalkColumns(side).Select(x => x.id)))} bar={(position.width >= FacesBarMinWindow).ToString().ToLowerInvariant()}";
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
