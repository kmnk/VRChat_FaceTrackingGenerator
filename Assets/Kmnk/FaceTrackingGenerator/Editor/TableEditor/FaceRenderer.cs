using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Draws the face of an avatar prefab with given blendshape weights, for Face View. A copy of the prefab goes
    // into PreviewRenderUtility's own preview scene; the user's scene and the prefab file are not touched.
    // The meshes the tables use are baked (BakeMesh + RecalculateBounds, as in TableEditorChecks.Preview) and drawn
    // with their own materials by a camera in front of the head.
    public sealed class FaceRenderer : IDisposable
    {
        public static readonly Color Background = new Color(0.847f, 0.847f, 0.886f);  // #D8D8E2

        readonly PreviewRenderUtility pru = new PreviewRenderUtility();
        readonly GameObject instance;
        readonly Transform head;
        readonly Dictionary<string, SkinnedMeshRenderer> meshes = new Dictionary<string, SkinnedMeshRenderer>();
        readonly Dictionary<SkinnedMeshRenderer, Mesh> baked = new Dictionary<SkinnedMeshRenderer, Mesh>();

        public GameObject Prefab { get; }

        // meshPaths: renderer paths from the avatar root (the tables' face mesh and part meshes).
        public FaceRenderer(GameObject prefab, IEnumerable<string> meshPaths)
        {
            Prefab = prefab;
            instance = pru.InstantiatePrefabInScene(prefab);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var path in meshPaths.Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                var t = instance.transform.Find(path);
                var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr != null && smr.sharedMesh != null) meshes[path] = smr;
            }
            var anim = instance.GetComponent<Animator>();
            head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) head = meshes.Values.FirstOrDefault()?.transform ?? instance.transform;
            pru.camera.fieldOfView = 22;
            pru.camera.nearClipPlane = 0.01f;
            pru.camera.farClipPlane = 10;
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.camera.backgroundColor = Background;
            pru.lights[0].intensity = 1.2f;
            pru.lights[0].transform.rotation = Quaternion.Euler(20, 180, 0);
            pru.ambientColor = new Color(0.6f, 0.6f, 0.6f);
        }

        public bool HasMesh(string path) => meshes.ContainsKey(path);

        public bool HasShape(string key)
        {
            var (mesh, shape) = FacePreview.Split(key);
            return meshes.TryGetValue(mesh, out var smr) && smr.sharedMesh.GetBlendShapeIndex(shape) >= 0;
        }

        // Sets the weights ("mesh/shape" -> value) on the copy: every key in `targets` gets its weight or 0.
        public void SetWeights(IEnumerable<string> targets, IDictionary<string, float> weights)
        {
            foreach (var key in targets.Concat(weights.Keys).Distinct())
            {
                var (mesh, shape) = FacePreview.Split(key);
                if (!meshes.TryGetValue(mesh, out var smr)) continue;
                var i = smr.sharedMesh.GetBlendShapeIndex(shape);
                if (i >= 0) smr.SetBlendShapeWeight(i, weights.TryGetValue(key, out var w) ? w : 0);
            }
        }

        // The weight now on the copy (for checks).
        public float Weight(string key)
        {
            var (mesh, shape) = FacePreview.Split(key);
            if (!meshes.TryGetValue(mesh, out var smr)) return float.NaN;
            var i = smr.sharedMesh.GetBlendShapeIndex(shape);
            return i < 0 ? float.NaN : smr.GetBlendShapeWeight(i);
        }

        // height: meters above the head bone the camera looks at. yaw: degrees around the head.
        public Texture2D Render(int width, int height, float lift = 0.03f, float yaw = 0)
        {
            var target = head.position + Vector3.up * lift;
            var dir = Quaternion.Euler(0, yaw, 0) * instance.transform.forward;
            pru.camera.transform.position = target + dir * 0.4f;
            pru.camera.transform.LookAt(target);
            pru.BeginStaticPreview(new Rect(0, 0, width, height));
            foreach (var smr in meshes.Values)
            {
                if (!baked.TryGetValue(smr, out var m)) baked[smr] = m = new Mesh();
                smr.BakeMesh(m, true);
                m.RecalculateBounds();
                var matrix = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var mats = smr.sharedMaterials;
                for (int s = 0; s < m.subMeshCount; s++)
                    if (s < mats.Length && mats[s] != null) pru.DrawMesh(m, matrix, mats[s], s);
            }
            pru.Render(true);
            return pru.EndStaticPreview();
        }

        public void Dispose()
        {
            foreach (var m in baked.Values) UnityEngine.Object.DestroyImmediate(m);
            baked.Clear();
            pru.Cleanup();
        }
    }
}
