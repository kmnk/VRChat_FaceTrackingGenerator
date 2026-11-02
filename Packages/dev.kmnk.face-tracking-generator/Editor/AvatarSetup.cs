using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Helpers for the window's "Avatar in the scene" section.
    public static class AvatarSetup
    {
        // The prefab asset of an avatar: the object itself when it is a prefab asset, else the prefab it comes from.
        public static GameObject PrefabOf(GameObject avatar)
        {
            if (avatar == null) return null;
            if (PrefabUtility.IsPartOfPrefabAsset(avatar)) return avatar;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(avatar);
            return src != null ? src.transform.root.gameObject : null;
        }

        // Writes prefab, mesh and menu of the avatar table from an avatar (scene object or prefab asset).
        public static List<string> FillTable(string avatarTable, GameObject avatar)
        {
            var log = new List<string>();
            var prefab = PrefabOf(avatar) ?? throw new TableLoader.TableException($"{avatar.name} is not a prefab or an instance of one");
            var desc = prefab.GetComponent<VRCAvatarDescriptor>() ?? throw new TableLoader.TableException($"{prefab.name} has no VRCAvatarDescriptor");
            TableWriter.SetTopLevel(avatarTable, "prefab", TableWriter.AssetRefJson(prefab));
            log.Add("prefab: " + AssetDatabase.GetAssetPath(prefab));

            // The face mesh: the one the descriptor uses for visemes, else the one with the most blendshapes.
            var face = desc.VisemeSkinnedMesh != null ? desc.VisemeSkinnedMesh
                : prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
            if (face != null)
            {
                var path = AnimationUtility.CalculateTransformPath(face.transform, prefab.transform);
                TableWriter.SetTopLevel(avatarTable, "mesh", TableWriter.StringJson(path));
                log.Add("mesh: " + path);
            }
            if (desc.expressionsMenu != null)
            {
                TableWriter.SetTopLevel(avatarTable, "menu", TableWriter.AssetRefJson(desc.expressionsMenu));
                log.Add("menu: " + AssetDatabase.GetAssetPath(desc.expressionsMenu));
            }
            return log;
        }

        // Adds FtGeneratorSettings pointing to the avatar table to an avatar in the scene (build-time generation).
        public static FtGeneratorSettings AddSettings(GameObject avatar, string avatarTable)
        {
            if (avatar.GetComponent<VRCAvatarDescriptor>() == null) throw new TableLoader.TableException($"{avatar.name} has no VRCAvatarDescriptor");
            var s = avatar.GetComponent<FtGeneratorSettings>();
            if (s == null) s = Undo.AddComponent<FtGeneratorSettings>(avatar);
            Undo.RecordObject(s, "Set Face Tracking Generator table");
            s.avatarTablePath = avatarTable;
            s.avatarTableGuid = AssetDatabase.AssetPathToGUID(avatarTable);
            EditorUtility.SetDirty(s);
            return s;
        }
    }

    [CustomEditor(typeof(FtGeneratorSettings))]
    public sealed class FtGeneratorSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var s = (FtGeneratorSettings)target;
            var path = !string.IsNullOrEmpty(s.avatarTableGuid) ? AssetDatabase.GUIDToAssetPath(s.avatarTableGuid) : null;
            if (string.IsNullOrEmpty(path)) path = s.avatarTablePath;
            var current = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            EditorGUI.BeginChangeCheck();
            var picked = (DefaultAsset)EditorGUILayout.ObjectField("Avatar table (avatar.jsonc)", current, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Set Face Tracking Generator table");
                s.avatarTablePath = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                s.avatarTableGuid = picked != null ? AssetDatabase.AssetPathToGUID(s.avatarTablePath) : "";
                EditorUtility.SetDirty(s);
            }
            EditorGUILayout.HelpBox("Face tracking is generated when you enter Play Mode and when you upload (NDMF). The avatar itself is not changed.", MessageType.None);
        }
    }
}
