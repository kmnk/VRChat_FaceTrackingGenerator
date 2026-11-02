using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Draws the face of -ftTable's prefab with FaceRenderer (what Face View shows): all parts at 0 ("neutral"),
        // and each blendshape of -ftShapes ("mesh/shape,..."; default: the first three parts) at 100. Checks that
        //   - the weights on the preview copy are what was set
        //   - each face differs from the neutral one, and drawing the same weights again gives the same pixels
        //     within a tolerance (under 0.1% of the pixels, by at most 8/255)
        //   - the open scene gets no change and the prefab file does not change
        // Writes <name>.png to -ftOut. Run with UNITY_BATCH_GRAPHICS=1.
        public static void FaceView() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var outDir = CliArgs.Require("-ftOut");
            Directory.CreateDirectory(outDir);
            var prefabPath = !string.IsNullOrEmpty(model.Prefab.Guid) ? AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab.Path;
            if (string.IsNullOrEmpty(prefabPath)) prefabPath = model.Prefab.Path;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var hashBefore = Sha1(prefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var dirtyBefore = scene.isDirty;

            var targets = model.Parts.Values.Select(p => p.Mesh + "/" + p.Shape).Distinct().ToList();
            var shapes = (CliArgs.Get("-ftShapes") ?? string.Join(",", targets.Take(3))).Split(',').Where(s => s != "").ToList();
            var failed = 0;
            ShaderUtil.allowAsyncCompilation = false;
            using (var r = new FaceRenderer(prefab, model.Parts.Values.Select(p => p.Mesh).Append(model.DefaultMesh)))
            {
                r.SetWeights(targets, new Dictionary<string, float>());
                r.Render(256, 256);  // the first image can be drawn before the shaders are ready
                var neutral = r.Render(512, 512);
                File.WriteAllBytes(Path.Combine(outDir, "neutral.png"), neutral.EncodeToPNG());
                foreach (var key in shapes)
                {
                    var weights = new Dictionary<string, float> { [key] = 100 };
                    r.SetWeights(targets, weights);
                    var setOk = Math.Abs(r.Weight(key) - 100) < 0.01f && targets.Where(t => t != key && r.HasShape(t)).All(t => Math.Abs(r.Weight(t)) < 0.01f);
                    var a = r.Render(512, 512);
                    var b = r.Render(512, 512);
                    var differs = !a.GetPixels32().SequenceEqual(neutral.GetPixels32());
                    var (diffCount, maxDiff) = Diff(a, b);
                    // Drawing the same weights twice differed in up to 15 pixels by up to 4/255 (2026-11-02, Mac).
                    var same = diffCount < a.width * a.height / 1000 && maxDiff <= 8;
                    File.WriteAllBytes(Path.Combine(outDir, Safe(key) + ".png"), a.EncodeToPNG());
                    var ok = r.HasShape(key) && setOk && differs && same;
                    if (!ok) failed++;
                    Debug.Log($"[FTG] table-editor face-view {key}: {(ok ? "pass" : "FAIL")} on-avatar={r.HasShape(key)} weights-set={setOk} differs-from-neutral={differs} same-again={same} diff-pixels={diffCount} max-diff={maxDiff}");
                    UnityEngine.Object.DestroyImmediate(a);
                    UnityEngine.Object.DestroyImmediate(b);
                }
                UnityEngine.Object.DestroyImmediate(neutral);

                // Moving through the list: blendshapes that are not in the tables must not stay on the face.
                var others = (CliArgs.Get("-ftHover") ?? "").Split(',').Where(x => x != "").ToList();
                if (others.Count >= 2)
                {
                    foreach (var key in others) r.SetWeights(targets, new Dictionary<string, float> { [key] = 100 });
                    var last = others[others.Count - 1];
                    var leftovers = others.Take(others.Count - 1).Where(k => r.HasShape(k) && Math.Abs(r.Weight(k)) > 0.01f).ToList();
                    var ok = Math.Abs(r.Weight(last) - 100) < 0.01f && leftovers.Count == 0;
                    if (!ok) failed++;
                    Debug.Log($"[FTG] table-editor face-view hover {string.Join(" > ", others)}: {(ok ? "pass" : "FAIL")} left-on={string.Join(",", leftovers)}");
                }
            }
            var hashAfter = Sha1(prefabPath);
            var untouched = hashBefore == hashAfter && scene.isDirty == dirtyBefore;
            if (!untouched) failed++;
            Debug.Log($"[FTG] table-editor face-view: shapes={shapes.Count} failed={failed} prefab-unchanged={hashBefore == hashAfter} scene-dirty={scene.isDirty}");
            return failed == 0 ? 0 : 1;
        });

        static (int count, int max) Diff(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            int count = 0, max = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                var d = Math.Max(Math.Abs(pa[i].r - pb[i].r), Math.Max(Math.Abs(pa[i].g - pb[i].g), Math.Abs(pa[i].b - pb[i].b)));
                if (d > 0) { count++; max = Math.Max(max, d); }
            }
            return (count, max);
        }

        static string Sha1(string assetPath)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assetPath)));
        }
    }
}
