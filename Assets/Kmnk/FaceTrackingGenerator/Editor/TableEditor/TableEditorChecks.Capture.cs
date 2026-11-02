using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Opens a window, lets it draw for a while, and writes what it drew to a PNG (-ftOut). Run from an editor
        // started with graphics and without -batchmode (scripts/unity-gui.sh): batchmode does not draw windows.
        //   -ftWindow generator | table   (default table)
        //   -ftSize 560x1000               window size in points
        // Other arguments are passed to the window (see TableEditorWindow.ApplyCaptureArgs).
        // Unity exits when done: 0 written, 2 error.
        public static void Capture()
        {
            try
            {
                var outPath = CliArgs.Require("-ftOut");
                var size = (CliArgs.Get("-ftSize") ?? "560x1000").Split('x');
                var w = float.Parse(size[0]); var h = float.Parse(size[1]);
                var which = CliArgs.Get("-ftWindow") ?? "table";
                EditorWindow window;
                if (which == "generator") window = EditorWindow.GetWindow<GeneratorWindow>("Face Tracking Generator");
                else
                {
                    var t = EditorWindow.GetWindow<TableEditorWindow>("FT Table Editor");
                    t.ApplyCaptureArgs();
                    window = t;
                }
                window.position = new Rect(80, 80, w, h);
                window.Show();
                var frames = 0;
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    window.Repaint();
                    if (window is TableEditorWindow tw) tw.CaptureTick(frames);
                    if (++frames < 60) return;
                    EditorApplication.update -= tick;
                    try
                    {
                        Grab(window, outPath);
                        Debug.Log($"[FTG] capture: wrote {outPath} ({window.position.width}x{window.position.height})");
                        var popup = TableEditorWindow.OpenPicker();
                        if (popup != null)
                        {
                            var popupPath = Path.ChangeExtension(outPath, null) + ".popup.png";
                            Grab(popup, popupPath);
                            Debug.Log($"[FTG] capture: wrote {popupPath} (popup at {popup.position})");
                        }
                        EditorApplication.Exit(0);
                    }
                    catch (Exception e) { Debug.LogException(e); Debug.Log("[FTG] error: " + e.Message); EditorApplication.Exit(2); }
                };
                EditorApplication.update += tick;
            }
            catch (Exception e) { Debug.LogException(e); Debug.Log("[FTG] error: " + e.Message); EditorApplication.Exit(2); }
        }

        // Copies the pixels of the window's own view (internal GUIView.GrabPixels) to a PNG. Only the window is
        // read, not the screen. The rectangle must lie inside the view: a larger one crashed Metal (2026-11-02).
        static void Grab(EditorWindow window, string outPath)
        {
            var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window)
                ?? throw new InvalidOperationException("the window has no view (m_Parent)");
            var grab = parent.GetType().GetMethod("GrabPixels", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(RenderTexture), typeof(Rect) }, null)
                ?? throw new InvalidOperationException("GUIView.GrabPixels is not available in this Unity");
            var view = (Rect)(parent.GetType().GetProperty("position", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(parent) ?? window.position);
            var ppp = EditorGUIUtility.pixelsPerPoint;
            var pw = Mathf.FloorToInt(view.width * ppp);
            var ph = Mathf.FloorToInt(view.height * ppp);
            var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            grab.Invoke(parent, new object[] { rt, new Rect(0, 0, pw, ph) });
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
            // GrabPixels gives the rows bottom to top.
            var px = tex.GetPixels32();
            var flipped = new Color32[px.Length];
            for (int y = 0; y < ph; y++) Array.Copy(px, y * pw, flipped, (ph - 1 - y) * pw, pw);
            tex.SetPixels32(flipped);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log($"[FTG] capture: view={view} pixelsPerPoint={ppp}");
        }
    }
}
