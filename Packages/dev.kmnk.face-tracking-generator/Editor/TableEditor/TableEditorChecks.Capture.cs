using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Opens a window, lets it draw for a while, and writes what it drew to a PNG (-ftOut). Run from an editor
        // started with graphics and without -batchmode (scripts/unity-gui.sh): batchmode does not draw windows.
        //   -ftWindow generator | table | statemachine   (default table; statemachine also opens the table editor)
        //   -ftSmMachine <state machine>  -ftSmSelect <state>|<from>-><to>  -ftSmOpen <region>  -ftSmEdits <edits.jsonl>  -ftSmZoom <factor>   (statemachine only;
        //   the edits are StateMachineEdit operations, as TableEditorChecks.Edit takes them, made before drawing)
        //   -ftSize 560x1000               window size in points
        //   -ftOpenScene <scene path>      opens the scene before the window opens
        // After drawing it prints "[FTG] capture: selection window=<generator|table> table=... avatar=... source=... preview=..."
        // (what the window shows of this project's saved choices, ProjectSelection).
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
                var openScene = CliArgs.Get("-ftOpenScene");
                if (openScene != null) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(openScene);
                EditorWindow window;
                var help = CliArgs.Get("-ftHelp");
                if (help != null) EditorText.Override = help == "ja" ? EditorText.Language.Japanese : help == "off" ? EditorText.Language.Off : EditorText.Language.English;
                if (which == "generator")
                {
                    var g = EditorWindow.GetWindow<GeneratorWindow>("Face Tracking Generator");
                    var tablePath = CliArgs.Get("-ftTable");
                    if (tablePath != null) g.SetTableForCapture(tablePath);
                    var avatarName = CliArgs.Get("-ftAvatarName");  // as typing in "Avatar name" does
                    if (avatarName != null) g.SetAvatarName(avatarName);
                    var chooseAvatar = CliArgs.Get("-ftChooseAvatar");  // as the "Avatar in scene" field does ("none" clears)
                    if (chooseAvatar != null) g.ChooseAvatar(chooseAvatar == "none" ? null : GameObject.Find(chooseAvatar) ?? throw new ArgumentException("not in the scene: " + chooseAvatar));
                    if (CliArgs.Get("-ftOpenTableEditor") == "1")
                    {
                        var te = TableEditorWindow.OpenTable(tablePath);
                        Debug.Log($"[FTG] capture: table editor opened {te.OpenedTablePath}");
                    }
                    window = g;
                }
                else if (which == "statemachine")
                {
                    var t = EditorWindow.GetWindow<TableEditorWindow>("FT Table Editor");
                    t.ApplyCaptureArgs();
                    t.position = new Rect(80 + w + 20, 80, 560, 1000);
                    var sm = StateMachineWindow.Open();
                    sm.ApplyCaptureArgs();
                    window = sm;
                }
                else
                {
                    var t = EditorWindow.GetWindow<TableEditorWindow>("FT Table Editor");
                    t.ApplyCaptureArgs();
                    window = t;
                }
                window.position = new Rect(80, 80, w, h);
                window.Show();
                var fv = FaceViewWindow.Find();
                if (fv != null) fv.position = new Rect(80 + w + 20, 80, 420, 420);
                var frames = 0;
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    window.Repaint();
                    if (window is TableEditorWindow tw) tw.CaptureTick(frames);
                    CommonTick(frames);
                    if (++frames < 60) return;
                    EditorApplication.update -= tick;
                    try
                    {
                        Grab(window, outPath);
                        Debug.Log($"[FTG] capture: wrote {outPath} ({window.position.width}x{window.position.height})");
                        if (window is GeneratorWindow gen) Debug.Log($"[FTG] capture: selection window=generator {gen.SelectionState()}");
                        var shown = window as TableEditorWindow ?? TableEditorWindow.Current;
                        if (shown != null) Debug.Log($"[FTG] capture: selection window=table {shown.SelectionState()}");
                        var face = FaceViewWindow.Find();
                        if (face != null)
                        {
                            var facePath = Path.ChangeExtension(outPath, null) + ".faceview.png";
                            Grab(face, facePath);
                            Debug.Log($"[FTG] capture: wrote {facePath}");
                        }
                        if (window is StateMachineWindow smw) Debug.Log($"[FTG] capture: state machine {smw.CaptureState()}");
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

        // The avatars the VRChat SDK panel offers to build: active descriptors found by the SDK's own search
        // (VRC.Tools.FindSceneObjectsOfTypeAll, as VRCSdkControlPanelAvatarBuilder.FindAvatars). -1 when not found.
        static int SdkAvatars()
        {
            var tools = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.Tools", false)).FirstOrDefault(t => t != null);
            var find = tools?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "FindSceneObjectsOfTypeAll" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            if (find == null) return -1;
            var found = (System.Collections.IEnumerable)find.MakeGenericMethod(typeof(VRC.SDKBase.VRC_AvatarDescriptor)).Invoke(null, null);
            return found.Cast<VRC.SDKBase.VRC_AvatarDescriptor>().Count(d => d != null && d.gameObject.activeInHierarchy);
        }

        // Changes made at set frames, after the windows are open, as a user would.
        static void CommonTick(int frame)
        {
            TableEditorWindow.Current?.Repaint();
            if (frame == 30)
            {
                // "-ftHelpAfter en|ja|off": change the language after the window is open, as the settings editor's Help does.
                var after = CliArgs.Get("-ftHelpAfter");
                if (after != null) EditorText.SetForCapture(after == "ja" ? EditorText.Language.Japanese : after == "off" ? EditorText.Language.Off : EditorText.Language.English);
            }
            // "-ftBuildBegin 1" / "-ftBuildEnd 1": the VRChat SDK's build callbacks, called through the SDK's own dispatcher
            if (frame == 25 && CliArgs.Get("-ftBuildBegin") == "1") SdkCallback("OnVRCSDKBuildRequested", true);
            if (frame == 45 && CliArgs.Get("-ftBuildEnd") == "1") SdkCallback("OnPostprocessAvatar", false);
            if ((frame == 20 || frame == 35 || frame == 55) && CliArgs.Get("-ftBuildBegin") == "1")
            {
                // Avatar copies in preview scenes (Face View's): before the build, during it, and after it.
                var copies = Resources.FindObjectsOfTypeAll<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>()
                    .Count(d => d.gameObject.scene.IsValid() && UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(d.gameObject.scene));
                var te = TableEditorWindow.Current;
                Debug.Log($"[FTG] capture: frame={frame} building={BuildGuard.Building.ToString().ToLower()} preview-running={(te != null && te.PreviewRunning).ToString().ToLower()} preview-scene-avatars={copies} sdk-avatars={SdkAvatars()}");
            }
        }

        static void SdkCallback(string name, bool withType)
        {
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks")).FirstOrDefault(x => x != null);
            var methods = t?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == name).ToList();
            Debug.Log($"[FTG] capture: sdk {name}: {(methods == null ? "no dispatcher" : string.Join(", ", methods.Select(m => m.ToString())))}");
            var m0 = methods?.FirstOrDefault();
            if (m0 == null) return;
            var ps = m0.GetParameters();
            var args = ps.Select(p => p.ParameterType.IsEnum ? Enum.GetValues(p.ParameterType).GetValue(0) : p.ParameterType == typeof(GameObject) ? null : (object)null).ToArray();
            var result = m0.Invoke(null, args);
            Debug.Log($"[FTG] capture: sdk {name} returned {result}");
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
