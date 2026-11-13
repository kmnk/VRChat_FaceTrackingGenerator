using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // What Face View shows. The table editor calls Show; Face View draws it.
    public static class FaceView
    {
        public sealed class Content
        {
            public TableModel Model;
            public GameObject Prefab;
            public Dictionary<string, float> Weights;  // "mesh/shape" -> weight; other parts of the tables are 0
            public string Caption;
        }

        public static Content Current { get; private set; }
        public static event Action Changed;

        public static void Show(TableModel model, GameObject prefab, IDictionary<string, float> weights, string caption)
        {
            Current = new Content { Model = model, Prefab = prefab, Weights = new Dictionary<string, float>(weights ?? new Dictionary<string, float>()), Caption = caption };
            Changed?.Invoke();
        }

        public static void Clear()
        {
            Current = null;
            Changed?.Invoke();
        }
    }

    // Tools/Kmnk/Face Tracking Face View: the face of the table's prefab, in front of the head, drawn apart from the
    // scene (FaceRenderer). Drag left and right to turn the face; Height moves the camera up and down.
    public sealed class FaceViewWindow : EditorWindow
    {
        FaceRenderer renderer;
        Texture2D image;
        bool stale = true;
        float lift = 0.03f, yaw;
        int lastSize;

        [MenuItem("Tools/Kmnk/Face Tracking Face View")]
        public static FaceViewWindow Open()
        {
            var w = GetWindow<FaceViewWindow>("FT Face View");
            w.minSize = new Vector2(220, 260);
            return w;
        }

        void OnEnable()
        {
            titleContent = new GUIContent("FT Face View");
            FaceView.Changed += OnChanged;
            BuildGuard.Changed += Repaint;
        }

        void OnDisable()
        {
            FaceView.Changed -= OnChanged;
            BuildGuard.Changed -= Repaint;
            DisposeRenderer();
        }

        void OnChanged()
        {
            stale = true;
            Repaint();
        }

        // Removes the avatar copy from the preview scene when a build starts; OnGUI makes it again after the build.
        internal void ReleaseForBuild()
        {
            DisposeRenderer();
            stale = true;
            Repaint();
        }

        void DisposeRenderer()
        {
            if (image != null) DestroyImmediate(image);
            image = null;
            renderer?.Dispose();
            renderer = null;
        }

        void OnGUI()
        {
            if (BuildGuard.Building) { BuildGuard.DrawPaused(); return; }  // no prefab instance in a preview scene during a build
            var c = FaceView.Current;
            var caption = c?.Caption ?? "Neutral";
            if (c != null && c.Weights.Any(kv => kv.Value > 0 && renderer != null && !renderer.HasShape(kv.Key))) caption += " (not in mesh)";
            EditorGUILayout.LabelField(caption, EditorStyles.miniLabel);

            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (c == null || c.Model == null)
                EditorGUI.LabelField(rect, EditorText.Get("faceview.noTable"), EditorStyles.centeredGreyMiniLabel);
            else if (c.Prefab == null)
                EditorGUI.LabelField(rect, EditorText.Get("faceview.noPrefab"), EditorStyles.centeredGreyMiniLabel);
            else
            {
                DrawFace(c, rect);
                HandleDrag(rect);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Height", GUILayout.ExpandWidth(false));
                lift = GUILayout.HorizontalSlider(lift, -0.1f, 0.2f, GUILayout.MinWidth(80));
                if (GUILayout.Button("Reset view", GUILayout.Width(90))) { lift = 0.03f; yaw = 0; }
                if (EditorGUI.EndChangeCheck()) stale = true;
            }
            EditorGUILayout.LabelField(EditorText.Get("faceview.note"), EditorStyles.wordWrappedMiniLabel);
        }

        void DrawFace(FaceView.Content c, Rect rect)
        {
            var size = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(rect.width, rect.height) * EditorGUIUtility.pixelsPerPoint), 64, 1024);
            if (renderer != null && renderer.Prefab != c.Prefab) DisposeRenderer();
            if (renderer == null)
            {
                renderer = new FaceRenderer(c.Prefab, c.Model.Parts.Values.Select(p => p.Mesh).Append(c.Model.DefaultMesh));
                stale = true;
            }
            if (stale || image == null || size != lastSize)
            {
                if (image != null) DestroyImmediate(image);
                renderer.SetWeights(Targets(c), c.Weights);
                image = renderer.Render(size, size, lift, yaw);
                lastSize = size;
                stale = false;
            }
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
        }

        static IEnumerable<string> Targets(FaceView.Content c) => c.Model.Parts.Values.Select(p => p.Mesh + "/" + p.Shape);

        void HandleDrag(Rect rect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDrag && rect.Contains(e.mousePosition))
            {
                yaw = Mathf.Repeat(yaw + e.delta.x * 0.5f + 180, 360) - 180;
                stale = true;
                e.Use();
                Repaint();
            }
        }

        // For TableEditorChecks.Capture.
        internal static FaceViewWindow Find() => Resources.FindObjectsOfTypeAll<FaceViewWindow>().FirstOrDefault();
    }

    // Input -> the parts it moves, for the line under each slider of the preview: a tracking key ("key:<key>") moves
    // the parts of its rows. The keys the transitions compare only choose states and move no part themselves.
    public static class InputParts
    {
        public static Dictionary<string, string> Of(TableModel m)
        {
            var direct = new Dictionary<string, SortedSet<string>>();
            void Add(string input, IEnumerable<string> parts)
            {
                if (!direct.TryGetValue(input, out var set)) direct[input] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.UnionWith(parts);
            }
            // Inputs move no part themselves: they only choose states. Tracking keys move the parts of their rows.
            foreach (var kv in m.Neutral) foreach (var r in kv.Value) Add(InputDef.TrackPrefix + r.Key, new[] { r.Part });
            foreach (var st in m.StateMachines.SelectMany(x => x.States))
                foreach (var r in st.Regions.Values.SelectMany(x => x.Rows).Where(x => x.Part != null)) Add(InputDef.TrackPrefix + r.Key, new[] { r.Part });

            var lines = new Dictionary<string, string>();
            foreach (var input in m.Inputs)
            {
                var pieces = new List<string>();
                if (direct.TryGetValue(input.Name, out var own) && own.Count > 0) pieces.Add(string.Join(", ", own));
                if (pieces.Count > 0) lines[input.Name] = string.Join(", ", pieces);
            }
            return lines;
        }
    }
}
