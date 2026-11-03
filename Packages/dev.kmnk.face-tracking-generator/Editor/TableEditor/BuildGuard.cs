using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Kmnk.FaceTrackingGenerator
{
    // While the VRChat SDK builds an avatar, the settings editor, Face View and the expression graph pause: the preview
    // (AnimationMode) stops, Face View does not render the prefab, and the windows do not redraw their content. They
    // resume when the avatar build has been processed, or with their Resume button (if a failed build never says so).
    public sealed class BuildGuard : IVRCSDKBuildRequestedCallback, IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => -10000;

        public static bool Building { get; private set; }
        public static event Action Changed;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            Begin();
            return true;
        }

        public void OnPostprocessAvatar() => End();

        internal static void Begin()
        {
            Building = true;
            foreach (var w in Resources.FindObjectsOfTypeAll<TableEditorWindow>()) w.StopPreviewForBuild();
            Changed?.Invoke();
        }

        internal static void End()
        {
            if (!Building) return;
            Building = false;
            Changed?.Invoke();
        }

        // The note and the Resume button the windows show instead of their content.
        internal static void DrawPaused()
        {
            EditorGUILayout.HelpBox(EditorText.Get("build.paused"), MessageType.Info);
            if (GUILayout.Button("Resume", GUILayout.ExpandWidth(false))) End();
        }
    }
}
