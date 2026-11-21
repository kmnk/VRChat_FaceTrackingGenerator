using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static class CurvePresetCheck
    {
        // Opens a row's curve field in the State Machine window with a click, then clicks two presets of Unity's curve
        // editor one after the other (the swatches along its bottom) and checks that the table follows the curve editor
        // after each. The first edit of unsaved settings adds "Unsaved changes" and Save to the toolbar; the curve editor
        // must stay linked to the row's field after that (the second preset reaches the table).
        // Run from an editor with graphics:
        //   scripts/unity-gui.sh <project> Kmnk.FaceTrackingGenerator.CurvePresetCheck.Run -ftTable <avatar.jsonc>
        //     -ftSmMachine Emotion -ftSmSelect joy -ftSmOpen eye [-ftShot <path prefix>]
        // The row is Emotion/joy/eye/EyeClosedLeft on VR. The presets are the second and the fourth swatch (x 112 and 202).
        // -ftShot writes the curve editor and the window after each preset (<prefix>-editor-1.png, ...).
        // Prints "[FTG] curve-check preset-<item> ok|NG <value>" and exits 0 when every item is ok, 1 otherwise, 2 on errors.
        public static void Run()
        {
            const string Id = "Emotion/joy/eye/EyeClosedLeft/vr";
            int fails = 0;
            void Item(string name, bool ok, string value = "")
            {
                Debug.Log($"[FTG] curve-check preset-{name} {(ok ? "ok" : "NG")} {value}".TrimEnd());
                if (!ok) fails++;
            }
            try
            {
                var t = EditorWindow.GetWindow<TableEditorWindow>("FT Table Editor");
                t.ApplyCaptureArgs();
                var sm = StateMachineWindow.Open();
                sm.ApplyCaptureArgs();
                sm.position = new Rect(80, 80, 1100, 720);
                sm.Show();
                var editorType = typeof(Editor).Assembly.GetType("UnityEditor.CurveEditorWindow");
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                EditorWindow CurveWindow() => (EditorWindow)editorType.GetProperty("instance", flags).GetValue(null);
                AnimationCurve EditorCurve() => (AnimationCurve)editorType.GetProperty("curve", flags).GetValue(null);
                JToken Row() => (StateMachineEdit.States(TableEditorWindow.Current?.ExpressionJson, "Emotion")?["joy"] as JObject)?["regions"]?["eye"]?["EyeClosedLeft"];
                AnimationCurve TableCurve() => StateMachineEdit.ReadRow(Row()).strength.CurveOf(Device.Vr);
                string Keys(AnimationCurve c) => c == null ? "null" : string.Join(" ", c.keys.Select(k => $"({k.time:0.###},{k.value:0.###})"));
                float Diff(AnimationCurve a, AnimationCurve b)
                {
                    if (a == null || b == null) return float.MaxValue;
                    float d = 0;
                    for (int i = 0; i <= 100; i++) d = Math.Max(d, Math.Abs(Mathf.Clamp01(a.Evaluate(i / 100f)) - Mathf.Clamp01(b.Evaluate(i / 100f))));
                    return d;
                }
                var grab = typeof(TableEditorChecks).GetMethod("Grab", BindingFlags.NonPublic | BindingFlags.Static);
                void Shot(int n)
                {
                    if (CliArgs.Get("-ftShot") == null) return;
                    grab.Invoke(null, new object[] { CurveWindow(), $"{CliArgs.Get("-ftShot")}-editor-{n}.png" });
                    grab.Invoke(null, new object[] { sm, $"{CliArgs.Get("-ftShot")}-window-{n}.png" });
                }
                void ClickPreset(float x)
                {
                    var ew = CurveWindow();
                    var at = new Vector2(x, ew.position.height - 23);
                    ew.SendEvent(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 });
                    ew.SendEvent(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 });
                }
                var frames = 0;
                string first = null;
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    sm.Repaint();
                    frames++;
                    try
                    {
                        if (frames == 30)
                        {
                            var dirty = TableEditorWindow.Current != null && TableEditorWindow.Current.hasUnsavedChanges;
                            if (!sm.CurveCellRects.TryGetValue(Id, out var rect)) { Item("field-drawn", false, string.Join(",", sm.CurveCellRects.Keys)); return; }
                            Item("field-drawn", true, $"{rect} unsaved-before={dirty.ToString().ToLower()}");
                            sm.SendEvent(new Event { type = EventType.MouseDown, mousePosition = rect.center - sm.position.position, button = 0, clickCount = 1 });
                        }
                        if (frames == 45) Item("editor-open", ReferenceEquals(EditorCurve(), sm.CurveCellCurve(Id)), $"editor curve={Keys(EditorCurve())}");
                        if (frames == 50) ClickPreset(112);
                        if (frames == 70)
                        {
                            first = Row()?.ToString(Newtonsoft.Json.Formatting.None);
                            Item("first-preset-in-table", Diff(TableCurve(), EditorCurve()) < 0.01f, $"table={Keys(TableCurve())} editor={Keys(EditorCurve())}");
                            Shot(1);
                            ClickPreset(202);
                        }
                        if (frames == 95)
                        {
                            var row = Row()?.ToString(Newtonsoft.Json.Formatting.None);
                            Item("second-preset-in-table", row != first && Diff(TableCurve(), EditorCurve()) < 0.01f, $"table={Keys(TableCurve())} editor={Keys(EditorCurve())}");
                            Item("field-linked", ReferenceEquals(EditorCurve(), sm.CurveCellCurve(Id)), $"field={Keys(sm.CurveCellCurve(Id))}");
                            Shot(2);
                        }
                        if (frames >= 100)
                        {
                            EditorApplication.update -= tick;
                            EditorApplication.Exit(fails == 0 ? 0 : 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("[FTG] error: " + ex);
                        EditorApplication.update -= tick;
                        EditorApplication.Exit(2);
                    }
                };
                EditorApplication.update += tick;
            }
            catch (Exception ex)
            {
                Debug.Log("[FTG] error: " + ex);
                EditorApplication.Exit(2);
            }
        }
    }
}
