using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Repeats window operations that the settings editor's fixes are about, and prints the state as [FTG] repro lines.
    // Run with scripts/unity-gui.sh (an editor with windows):
    //   Kmnk.FaceTrackingGenerator.WindowRepro.Run -ftRepro <reload|switch-undo|state-face> -ftTable <avatar.jsonc>
    //     [-ftTable2 <another avatar.jsonc> (switch-undo)] [-ftPart <part> (state-face)]
    //   Kmnk.FaceTrackingGenerator.WindowRepro.ManualBake -ftAvatar <prefab with FtGeneratorSettings>  (batchmode works)
    // The tables are not saved, except switch-undo, which saves the first table unchanged.
    [InitializeOnLoad]
    public static class WindowRepro
    {
        const string StepKey = "Kmnk.FaceTrackingGenerator.WindowRepro.Step";

        static WindowRepro()
        {
            // The second half of "reload": after the domain reload.
            if (SessionState.GetString(StepKey, "") != "reload-after") return;
            SessionState.EraseString(StepKey);
            EditorApplication.delayCall += () => Finish(() =>
            {
                var w = TableEditorWindow.Current ?? EditorWindow.GetWindow<TableEditorWindow>();
                Debug.Log($"[FTG] repro reload: after dirty={w.hasUnsavedChanges.ToString().ToLower()} name={Name(w)}");
            });
        }

        public static void Run()
        {
            Finish(() =>
            {
                var which = CliArgs.Require("-ftRepro");
                var table = CliArgs.Require("-ftTable");
                var w = TableEditorWindow.OpenTable(table);
                w.Show();
                switch (which)
                {
                    case "reload": Reload(w); return false;
                    case "switch-undo": SwitchUndo(w, table, CliArgs.Require("-ftTable2")); break;
                    case "state-face": StateFace(w, CliArgs.Get("-ftPart") ?? "eye.eyeBlinkLeft"); break;
                    default: throw new ArgumentException("unknown -ftRepro " + which);
                }
                return true;
            });
        }

        // An unsaved edit, then a domain reload (as entering Play Mode or compiling does).
        static void Reload(TableEditorWindow w)
        {
            w.EditAvatar("Set FT avatar name", a => a["name"] = "ReproReload");
            Debug.Log($"[FTG] repro reload: before dirty={w.hasUnsavedChanges.ToString().ToLower()} name={Name(w)}");
            SessionState.SetString(StepKey, "reload-after");
            EditorUtility.RequestScriptReload();
        }

        // An edit of the first table, saved; then the second table opened; then Undo.
        static void SwitchUndo(TableEditorWindow w, string first, string second)
        {
            var name = (string)w.AvatarJson["name"];
            Undo.IncrementCurrentGroup();
            w.EditAvatar("Set FT avatar name", a => a["name"] = name + "X");
            Undo.IncrementCurrentGroup();
            w.SaveChanges();
            var w2 = TableEditorWindow.OpenTable(second);
            Debug.Log($"[FTG] repro switch-undo: opened {w2.OpenedTablePath} name={Name(w2)} dirty={w2.hasUnsavedChanges.ToString().ToLower()}");
            Undo.PerformUndo();
            Debug.Log($"[FTG] repro switch-undo: undo table={w2.OpenedTablePath} name={Name(w2)} dirty={w2.hasUnsavedChanges.ToString().ToLower()}");
        }

        // A state's face shown (as choosing a state in the State Machine window), then a row selected, then an edit.
        static void StateFace(TableEditorWindow w, string part)
        {
            var sm = w.Model.StateMachines.First();
            var st = sm.States.Last();
            w.ShowState(sm.Name, st.Name);
            Debug.Log($"[FTG] repro state-face: shown {sm.Name}/{st.Name} state={StateShown(w)}");
            // As clicking a row of Face movements.
            typeof(TableEditorWindow).GetField("selectedPart", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(w, part);
            typeof(TableEditorWindow).GetMethod("ShowSelected", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
            w.EditAvatar("Set FT avatar name", a => a["name"] = (string)a["name"]);
            Debug.Log($"[FTG] repro state-face: after selecting {part} and an edit state={StateShown(w)}");
        }

        // NDMF's Manual Bake on an instance of the prefab.
        public static void ManualBake()
        {
            Finish(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CliArgs.Require("-ftAvatar")) ?? throw new ArgumentException("no prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var errors = new List<string>();
                    void OnLog(string msg, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors.Add(msg.Split('\n')[0]); }
                    Application.logMessageReceived += OnLog;
                    GameObject result = null;
                    try { result = nadena.dev.ndmf.AvatarProcessor.ManualProcessAvatar(go); }
                    catch (Exception e) { errors.Add(e.Message); }
                    finally { Application.logMessageReceived -= OnLog; }
                    var desc = result != null ? result.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() : null;
                    var fx = desc?.baseAnimationLayers.FirstOrDefault(l => l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                    Debug.Log($"[FTG] repro manual-bake: errors={errors.Count} result={(result != null).ToString().ToLower()} fx={(fx != null ? AssetDatabase.GetAssetPath(fx) : "-")} faceTracking={(desc != null && desc.enableFaceTracking).ToString().ToLower()}");
                    if (result != null) UnityEngine.Object.DestroyImmediate(result);
                    foreach (var e in errors.Distinct().Take(5)) Debug.Log("[FTG] repro manual-bake error: " + e);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                return true;
            });
        }

        static string StateShown(TableEditorWindow w) =>
            typeof(TableEditorWindow).GetField("stateShown", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w)?.ToString() ?? "none";

        static string Name(TableEditorWindow w) => (string)w.AvatarJson?["name"] ?? "-";

        static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

        // Runs a step and exits Unity (0, or 2 on an exception) unless the step returns false (it continues later).
        static void Finish(Func<bool> body)
        {
            try { if (body()) EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); Debug.Log("[FTG] error: " + e.Message); EditorApplication.Exit(2); }
        }

        static void Finish(Action body) => Finish(() => { body(); return true; });
    }
}
