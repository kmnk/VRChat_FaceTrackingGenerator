using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Puts the avatar prefab of -ftTable in a new scene and, for each query of -ftQueries (the Cli.Curve form:
        // {"label", "set": {input: value}, "state": {state machine: state}}), shows the weights with FacePreview and checks
        //   - the blendshape values on the avatar equal the FaceEval weights
        //   - an image of the face equals an image of a second copy of the avatar with the weights set directly
        // then stops the preview and checks the values are back and the scene has no new change.
        // Writes <label>.png and weights.jsonl (the Weights list, in the Cli.Curve form) to -ftOut.
        // Run with UNITY_BATCH_GRAPHICS=1 (it renders).
        public static void Preview() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var outDir = CliArgs.Require("-ftOut");
            Directory.CreateDirectory(outDir);
            var queries = File.ReadAllLines(CliArgs.Require("-ftQueries")).Where(l => !string.IsNullOrWhiteSpace(l)).Select(JObject.Parse).ToList();

            var prefabPath = !string.IsNullOrEmpty(model.Prefab.Guid) ? AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab.Path;
            if (string.IsNullOrEmpty(prefabPath)) prefabPath = model.Prefab.Path;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            b.name = prefab.name + " (direct)";
            var faceA = a.transform.Find(model.DefaultMesh).GetComponent<SkinnedMeshRenderer>();
            var faceB = b.transform.Find(model.DefaultMesh).GetComponent<SkinnedMeshRenderer>();
            // Draw baked copies only: the renderers of the two avatars are turned off.
            foreach (var r in a.GetComponentsInChildren<Renderer>(true).Concat(b.GetComponentsInChildren<Renderer>(true))) r.enabled = false;
            ShaderUtil.allowAsyncCompilation = false;  // otherwise the first images are drawn before the shaders are ready
            var (cam, baked) = Stage(a, faceA);
            var rt = new RenderTexture(512, 512, 24);
            cam.targetTexture = rt;

            var before = Weights(faceA);
            var headStart = HeadOf(a, faceA).position;
            var dirtyBefore = scene.isDirty;
            var preview = new FacePreview();
            if (!preview.Start(a)) throw new InvalidOperationException("the preview did not start (AnimationMode is busy or Play Mode)");

            var failed = 0;
            var lines = new List<string>();
            Render(cam, rt);  // the first image after setup can be broken; draw one and drop it
            foreach (var q in queries)
            {
                var label = (string)q["label"];
                var set = ((JObject)q["set"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => p.Value.Value<float>());
                var st = ((JObject)q["state"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
                var weights = FaceEval.Evaluate(model, set, st);
                var missing = preview.Apply(model, weights);

                // values on the avatar
                var worst = 0f;
                foreach (var key in FacePreview.Targets(model, weights).Where(k => !missing.Contains(k)))
                {
                    var (mesh, shape) = FacePreview.Split(key);
                    var smr = a.transform.Find(mesh).GetComponent<SkinnedMeshRenderer>();
                    var got = smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(shape));
                    worst = Math.Max(worst, Math.Abs(got - (weights.TryGetValue(key, out var w) ? w : 0)));
                }

                // the preview must not move the skeleton
                var headMoved = Vector3.Distance(HeadOf(a, faceA).position, headStart) > 1e-4f;

                // images: preview vs set directly
                Bake(faceA, baked);
                var imgA = Render(cam, rt);
                for (int i = 0; i < faceB.sharedMesh.blendShapeCount; i++) faceB.SetBlendShapeWeight(i, faceA.GetBlendShapeWeight(i));
                foreach (var key in FacePreview.Targets(model, weights).Where(k => !missing.Contains(k) && FacePreview.Split(k).mesh == model.DefaultMesh))
                    faceB.SetBlendShapeWeight(faceB.sharedMesh.GetBlendShapeIndex(FacePreview.Split(key).shape), weights.TryGetValue(key, out var w) ? w : 0);
                Bake(faceB, baked);
                var imgB = Render(cam, rt);
                var samePixels = imgA.GetPixels32().SequenceEqual(imgB.GetPixels32());
                File.WriteAllBytes(Path.Combine(outDir, Safe(label) + ".png"), imgA.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(imgA);
                UnityEngine.Object.DestroyImmediate(imgB);

                var ok = worst < 0.01f && samePixels && !headMoved;
                if (!ok) failed++;
                Debug.Log($"[FTG] table-editor preview {label}: {(ok ? "pass" : "FAIL")} max-diff={worst:0.###} same-pixels={samePixels} head-moved={headMoved} missing={missing.Count}");

                var rows = PreviewWeights.Rows(model, set, st);
                lines.Add(new JObject { ["label"] = label, ["shapes"] = new JObject(rows.Select(r => new JProperty(r.Key, Math.Round(r.Amount, 1)))), ["from"] = new JObject(rows.Select(r => new JProperty(r.Key, string.Join(", ", r.From)))) }.ToString(Formatting.None));
            }
            preview.Stop();

            var after = Weights(faceA);
            var restored = before.SequenceEqual(after);
            var dirtyAfter = scene.isDirty;
            Debug.Log($"[FTG] table-editor preview stop: restored={restored} dirty-before={dirtyBefore} dirty-after={dirtyAfter}");
            if (!restored || dirtyAfter != dirtyBefore) failed++;
            File.WriteAllText(Path.Combine(outDir, "weights.jsonl"), string.Join("\n", lines) + "\n");
            Debug.Log($"[FTG] table-editor preview: queries={queries.Count} failed={failed}");
            return failed == 0 ? 0 : 1;
        });

        // Prints the states for a sequence of input values (-ftSteps: JSON Lines of {"label", "set": {...}, "menu":
        // {state machine: state or null}}; values stay until changed, like moving sliders). Writes "label sm=state ..." lines.
        public static void Switches() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var tracker = new SwitchTracker();
            var inputs = new Dictionary<string, float>();
            foreach (var line in File.ReadAllLines(CliArgs.Require("-ftSteps")).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var o = JObject.Parse(line);
                foreach (var p in ((JObject)o["set"] ?? new JObject()).Properties()) inputs[p.Name] = p.Value.Value<float>();
                foreach (var p in ((JObject)o["menu"] ?? new JObject()).Properties()) tracker.Choose(p.Name, p.Value.Type == JTokenType.Null ? null : (string)p.Value);
                var states = tracker.Update(model, inputs);
                Debug.Log($"[FTG] table-editor switches {(string)o["label"]}: " + string.Join(" ", states.Select(kv => kv.Key + "=" + kv.Value)));
            }
            return 0;
        });

        static float[] Weights(SkinnedMeshRenderer smr) => Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(smr.GetBlendShapeWeight).ToArray();

        static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '=' ? c : '_'));

        // A camera in front of the head and a renderer for baked copies of the face.
        static (Camera cam, MeshFilter baked) Stage(GameObject avatar, SkinnedMeshRenderer face)
        {
            var head = HeadOf(avatar, face);
            var light = new GameObject("FTG light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(20, 180, 0);
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var cam = new GameObject("FTG camera").AddComponent<Camera>();
            var dy = CliArgs.GetFloat("-ftCameraY", 0.03f);
            var target = head.position + Vector3.up * dy;
            cam.transform.position = target + avatar.transform.forward * 0.4f;
            cam.transform.LookAt(target);
            cam.fieldOfView = 22; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.9f);
            var copy = new GameObject("FTG baked face");
            copy.transform.SetPositionAndRotation(face.transform.position, face.transform.rotation);
            var mf = copy.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh();
            copy.AddComponent<MeshRenderer>().sharedMaterials = face.sharedMaterials;
            return (cam, mf);
        }

        static Transform HeadOf(GameObject avatar, SkinnedMeshRenderer face)
        {
            var anim = avatar.GetComponent<Animator>();
            return anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : face.transform;
        }

        // BakeMesh leaves the bounds of the target mesh at zero, and the camera then culls it.
        static void Bake(SkinnedMeshRenderer smr, MeshFilter target)
        {
            smr.BakeMesh(target.sharedMesh, true);
            target.sharedMesh.RecalculateBounds();
        }

        static Texture2D Render(Camera cam, RenderTexture rt)
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }
    }
}
