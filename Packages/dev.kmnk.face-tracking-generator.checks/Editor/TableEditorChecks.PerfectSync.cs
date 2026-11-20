using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // The 52 ARKit blend shape names (perfect sync), in Apple's order.
        public static readonly string[] ArkitNames =
        {
            "eyeBlinkLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpLeft", "eyeSquintLeft", "eyeWideLeft",
            "eyeBlinkRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpRight", "eyeSquintRight", "eyeWideRight",
            "jawForward", "jawLeft", "jawRight", "jawOpen", "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight",
            "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthStretchLeft", "mouthStretchRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthUpperUpLeft", "mouthUpperUpRight",
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
            "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "noseSneerLeft", "noseSneerRight", "tongueOut",
        };

        // Makes avatars for checking tracking parts (plan step 1): Prefab Variants of -ftPrefab with a child
        // "PerfectSyncFace", a small generated mesh with the 52 ARKit blend shapes (every second name with a capital first
        // letter, e.g. EyeLookDownLeft). Writes under -ftOut:
        //   PerfectSyncAvatar.prefab        no Face Tracking settings of its own
        //   PerfectSyncAvatarOwnFT.prefab   its own Blendshapes Set "own" (JawOpen -> jawOpen, and a viseme row)
        //   PerfectSyncParameters.asset, PerfectSyncMenu.asset   copies of the prefab's Expression Parameters and menu, which the two use
        // [FTG] check: perfect sync avatar: <path> blendshapes=52
        // [FTG] check: perfect sync avatar (own face tracking): <path> sets=1
        public static void MakePerfectSyncAvatar() => Cli.Run(() =>
        {
            var basePath = CliArgs.Require("-ftPrefab");
            var outDir = CliArgs.Require("-ftOut");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath) ?? throw new TableLoader.TableException("prefab not found: " + basePath);
            Directory.CreateDirectory(outDir);

            var names = ArkitNames.Select((n, i) => i % 2 == 1 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : n).ToArray();
            var mesh = new Mesh { name = "PerfectSyncFace" };
            var v = new[] { new Vector3(0, 1.5f, 0.1f), new Vector3(0.1f, 1.5f, 0.1f), new Vector3(0, 1.6f, 0.1f), new Vector3(0.1f, 1.6f, 0.1f) };
            mesh.vertices = v;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            for (int i = 0; i < names.Length; i++)
            {
                var d = new Vector3[v.Length];
                d[i % v.Length] = new Vector3(0, 0, 0.001f * (i + 1));
                mesh.AddBlendShapeFrame(names[i], 100, d, null, null);
            }
            // Copies of the avatar's Expression Parameters and menu, so that adding the menu (MenuInstaller, which changes
            // the assets the descriptor points to) does not touch the originals.
            var src = prefab.GetComponent<VRCAvatarDescriptor>();
            string Copy(Object asset, string name)
            {
                if (asset == null) return null;
                var dest = outDir + "/" + name;
                AssetDatabase.DeleteAsset(dest);
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(asset), dest);
                return dest;
            }
            var exprCopy = Copy(src.expressionParameters, "PerfectSyncParameters.asset");
            var menuCopy = Copy(src.expressionsMenu, "PerfectSyncMenu.asset");

            var meshPath = outDir + "/PerfectSyncFace.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            string Make(string file, bool ownFt)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var face = new GameObject("PerfectSyncFace");
                    face.transform.SetParent(inst.transform, false);
                    var smr = face.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    var d = inst.GetComponent<VRCAvatarDescriptor>();
                    if (exprCopy != null) d.expressionParameters = AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>(exprCopy);
                    if (menuCopy != null) d.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>(menuCopy);
                    d.enableFaceTracking = false;
                    d.faceTrackingBlendshapesSetList = new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>();
                    d.faceTrackingParameterBehaviors = new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
                    d.faceTrackingMeshes = new List<SkinnedMeshRenderer>();
                    var eyes = d.customEyeLookSettings;
                    eyes.eyeGazeMode = VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode.BoneRotation;
                    if (eyes.eyelidType == VRCAvatarDescriptor.EyelidType.FaceTracking) eyes.eyelidType = VRCAvatarDescriptor.EyelidType.None;
                    d.customEyeLookSettings = eyes;
                    if (ownFt)
                    {
                        d.enableFaceTracking = true;
                        var n = NativeKeys.Count;
                        var keys = new string[n];
                        keys[NativeKeys.Index("JawOpen")] = names[System.Array.IndexOf(ArkitNames, "jawOpen")];
                        var visemes = new string[(int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count];
                        visemes[(int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.aa] = names[System.Array.IndexOf(ArkitNames, "mouthFunnel")];
                        d.faceTrackingBlendshapesSetList.Add(new VRCAvatarDescriptor.FaceTrackingBlendshapesSet
                        {
                            setName = "own", setWeight = 1,
                            faceTrackingBlendshapeNames = keys, faceTrackingBlendshapeMultipliers = Enumerable.Repeat(1f, n).ToArray(),
                            visemeBlendshapeNames = visemes, visemeBlendshapeMultipliers = Enumerable.Repeat(1f, visemes.Length).ToArray(),
                        });
                        d.faceTrackingMeshes.Add(smr);
                    }
                    var path = outDir + "/" + file;
                    PrefabUtility.SaveAsPrefabAsset(inst, path);
                    return path;
                }
                finally
                {
                    Object.DestroyImmediate(inst);
                }
            }

            var plain = Make("PerfectSyncAvatar.prefab", false);
            Debug.Log($"[FTG] check: perfect sync avatar: {plain} blendshapes={mesh.blendShapeCount}");
            var own = Make("PerfectSyncAvatarOwnFT.prefab", true);
            var sets = AssetDatabase.LoadAssetAtPath<GameObject>(own).GetComponent<VRCAvatarDescriptor>().faceTrackingBlendshapesSetList.Count;
            Debug.Log($"[FTG] check: perfect sync avatar (own face tracking): {own} sets={sets}");
            Debug.Log($"[FTG] check: perfect sync avatar parameters={exprCopy} menu={menuCopy}");
            return 0;
        });
    }
}
