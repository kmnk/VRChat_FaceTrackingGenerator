using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Shows weights on an avatar in the scene with AnimationMode, the mechanism of the Animation window's preview.
    // Each change samples a temporary clip that sets every blendshape the tables use (0 when not moving).
    // Stopping AnimationMode puts the original values back; the scene and the prefab keep nothing.
    public sealed class FacePreview : IDisposable
    {
        static AnimationModeDriver driver;
        static AnimationModeDriver Driver
        {
            get
            {
                if (driver == null)
                {
                    driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
                    driver.name = "Face Tracking Table Editor";
                    driver.hideFlags = HideFlags.HideAndDontSave;
                }
                return driver;
            }
        }

        public GameObject Avatar { get; private set; }
        public bool Running => Avatar != null && AnimationMode.InAnimationMode(Driver);

        // Another tool (the Animation window) is using AnimationMode.
        public static bool Busy => AnimationMode.InAnimationMode() && !AnimationMode.InAnimationMode(Driver);

        public bool Start(GameObject avatar)
        {
            if (avatar == null || Busy || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            Avatar = avatar;
            if (!AnimationMode.InAnimationMode(Driver)) AnimationMode.StartAnimationMode(Driver);
            return true;
        }

        public void Stop()
        {
            if (AnimationMode.InAnimationMode(Driver)) AnimationMode.StopAnimationMode(Driver);
            Avatar = null;
            SceneView.RepaintAll();
        }

        public void Dispose() => Stop();

        // weights: "mesh/shape" -> weight (FaceEval). Returns the keys that the avatar does not have.
        public HashSet<string> Apply(TableModel model, IDictionary<string, float> weights)
        {
            var missing = new HashSet<string>();
            if (!Running) return missing;
            // One clip per mesh, sampled on the mesh's own GameObject (path ""). Sampling on the avatar root would
            // let its humanoid Animator pose the skeleton too (the head moved from 1.13 m to 0.34 m on 2026-11-02).
            var clips = new Dictionary<SkinnedMeshRenderer, AnimationClip>();
            foreach (var key in Targets(model, weights))
            {
                var (mesh, shape) = Split(key);
                var t = Avatar.transform.Find(mesh);
                var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null || smr.sharedMesh == null || smr.sharedMesh.GetBlendShapeIndex(shape) < 0)
                {
                    missing.Add(key);
                    continue;
                }
                if (!clips.TryGetValue(smr, out var clip)) clips[smr] = clip = new AnimationClip { name = "FT table preview" };
                var v = weights.TryGetValue(key, out var w) ? w : 0f;
                clip.SetCurve("", typeof(SkinnedMeshRenderer), "blendShape." + shape, AnimationCurve.Constant(0, 1, v));
            }
            AnimationMode.BeginSampling();
            foreach (var kv in clips) AnimationMode.SampleAnimationClip(kv.Key.gameObject, kv.Value, 0);
            AnimationMode.EndSampling();
            foreach (var clip in clips.Values) UnityEngine.Object.DestroyImmediate(clip);
            SceneView.RepaintAll();
            return missing;
        }

        // Every blendshape the tables can move, and any other key in the weights.
        public static IEnumerable<string> Targets(TableModel model, IDictionary<string, float> weights) =>
            model.Parts.Values.Select(p => p.Mesh + "/" + p.Shape).Concat(weights.Keys).Distinct();

        public static (string mesh, string shape) Split(string key)
        {
            var i = key.IndexOf('/');
            return i < 0 ? ("", key) : (key.Substring(0, i), key.Substring(i + 1));
        }
    }

    // The rows of the Weights list: blendshapes at 0.05 or more, largest first, with the trees that move them.
    public static class PreviewWeights
    {
        public sealed class Row
        {
            public string Key;     // "mesh/shape"
            public float Amount;
            public List<string> From = new List<string>();
        }

        public static List<Row> Rows(TableModel model, IDictionary<string, float> inputs, IDictionary<string, string> states)
        {
            var rows = new Dictionary<string, Row>();
            var total = FaceEval.Evaluate(model, inputs, states);
            foreach (var src in FaceEval.Sources(model, inputs, states))
            {
                if (Math.Abs(src.Value) < 0.05f) continue;
                if (!rows.TryGetValue(src.Shape, out var r)) rows[src.Shape] = r = new Row { Key = src.Shape, Amount = total[src.Shape] };
                var from = SourceText(src);
                if (!r.From.Contains(from)) r.From.Add(from);
            }
            return rows.Values.Where(r => Math.Abs(r.Amount) >= 0.05f).OrderByDescending(r => r.Amount).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
        }

        // "EyeClosedLeft (neutral)", "EyeClosedLeft (Emotion=joy)" or "joy fixed" (design.md "設定エディタ").
        public static string SourceText(FaceEval.Source s)
        {
            var ja = EditorText.Current == EditorText.Language.Japanese;
            if (s.Key == null) return ja ? $"{s.State} の固定" : $"{s.State} fixed";
            return s.StateMachine == null ? $"{s.Key} (neutral)" : $"{s.Key} ({s.StateMachine}={s.State})";
        }
    }
}
