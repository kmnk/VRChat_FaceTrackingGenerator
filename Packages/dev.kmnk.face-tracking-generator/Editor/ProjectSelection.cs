using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // What the generator window and the settings editor chose in this project: the settings file, and the avatar in
    // the scene per settings file and scene. Kept in the project's UserSettings folder, so other projects on the same
    // computer do not change it and git does not track it.
    [FilePath("UserSettings/Kmnk.FaceTrackingGenerator.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ProjectSelection : ScriptableSingleton<ProjectSelection>
    {
        // Versions up to 0.5.1 kept one choice for the whole computer under this EditorPrefs key.
        // It is read once, in a project that has not chosen a settings file yet.
        const string LegacyTableKey = "Kmnk.FaceTrackingGenerator.AvatarTable";

        [Serializable]
        sealed class AvatarChoice
        {
            public string table;   // GUID of avatar.jsonc
            public string scene;   // GUID of the scene asset
            public string avatar;  // GlobalObjectId of the avatar's GameObject
        }

        [SerializeField] bool tableChosen;
        [SerializeField] string tableGuid = "";
        [SerializeField] List<AvatarChoice> avatars = new List<AvatarChoice>();
        [SerializeField] string avatarName = "";

        // The name last typed in the generator window's "Avatar name" (for Create Settings), or "" when none.
        public static string AvatarName
        {
            get => instance.avatarName ?? "";
            set
            {
                var s = instance;
                if (s.avatarName == value) return;
                s.avatarName = value ?? "";
                s.Save(true);
            }
        }

        // The chosen settings file, or null when none is chosen or the file is gone.
        public static string TablePath
        {
            get
            {
                var s = instance;
                if (!s.tableChosen)
                {
                    var legacy = EditorPrefs.GetString(LegacyTableKey, "");
                    if (legacy == "" || AssetDatabase.LoadAssetAtPath<DefaultAsset>(legacy) == null) return null;
                    SetTable(legacy);
                }
                return PathOf(s.tableGuid);
            }
        }

        // Records a choice of the settings file (null or "" clears it).
        public static void SetTable(string path)
        {
            var s = instance;
            s.tableChosen = true;
            s.tableGuid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            s.Save(true);
        }

        // The avatar to show for a settings file in the open scenes: the one chosen before in that scene ("saved"),
        // else the only avatar made from the settings file's prefab ("prefab"), else null ("none").
        public static GameObject FindAvatar(string tablePath, GameObject prefab, out string source)
        {
            source = "none";
            var table = GuidOf(tablePath);
            if (table == "") return null;
            var s = instance;
            foreach (var scene in LoadedScenes())
            {
                var sceneGuid = GuidOf(scene.path);
                var choice = s.avatars.FirstOrDefault(a => a.table == table && a.scene == sceneGuid);
                if (choice == null || !GlobalObjectId.TryParse(choice.avatar, out var id)) continue;
                var go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (IsSceneAvatar(go)) { source = "saved"; return go; }
            }
            if (prefab == null) return null;
            var made = LoadedScenes()
                .SelectMany(sc => sc.GetRootGameObjects())
                .SelectMany(r => r.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                .Select(d => d.gameObject)
                .Where(go => AvatarSetup.PrefabOf(go) == prefab)
                .Distinct()
                .ToList();
            if (made.Count != 1) return null;
            source = "prefab";
            return made[0];
        }

        // Records the avatar chosen for a settings file in the avatar's scene. Null forgets the choices of the
        // settings file in the open scenes. A scene never saved has no GUID, so nothing is kept for it.
        public static void SetAvatar(string tablePath, GameObject avatar)
        {
            var table = GuidOf(tablePath);
            if (table == "") return;
            var s = instance;
            if (avatar == null)
            {
                var open = new HashSet<string>(LoadedScenes().Select(sc => GuidOf(sc.path)));
                if (s.avatars.RemoveAll(a => a.table == table && open.Contains(a.scene)) > 0) s.Save(true);
                return;
            }
            if (!IsSceneAvatar(avatar)) return;
            var sceneGuid = GuidOf(avatar.scene.path);
            if (sceneGuid == "") return;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString();
            var choice = s.avatars.FirstOrDefault(a => a.table == table && a.scene == sceneGuid);
            if (choice != null && choice.avatar == id) return;
            if (choice == null) s.avatars.Add(choice = new AvatarChoice { table = table, scene = sceneGuid });
            choice.avatar = id;
            s.Save(true);
        }

        static bool IsSceneAvatar(GameObject go) =>
            go != null && go.scene.IsValid() && go.GetComponent<VRCAvatarDescriptor>() != null;

        static IEnumerable<Scene> LoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded) yield return sc;
            }
        }

        static string GuidOf(string path) => string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path) ?? "";

        static string PathOf(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<DefaultAsset>(path) != null ? path : null;
        }
    }
}
