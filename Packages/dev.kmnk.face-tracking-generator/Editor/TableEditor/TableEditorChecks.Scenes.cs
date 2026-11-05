using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Saves a scene (-ftOut, under Assets) with two avatars made from the prefab of -ftTable, for checking the
        // avatar the settings editor brings back when one prefab has two avatars in a scene. The second is named
        // "<prefab name> 2".  [FTG] scene: wrote <path> avatars=<name>,<name>
        public static void MakeTwoAvatarScene() => Cli.Run(() =>
        {
            var model = TableLoader.Load(CliArgs.Require("-ftTable"));
            var outPath = CliArgs.Require("-ftOut");
            var prefabPath = !string.IsNullOrEmpty(model.Prefab.Guid) ? AssetDatabase.GUIDToAssetPath(model.Prefab.Guid) : model.Prefab.Path;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            b.name = prefab.name + " 2";
            b.transform.position = new Vector3(1, 0, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            if (!EditorSceneManager.SaveScene(scene, outPath)) throw new IOException("could not save " + outPath);
            Debug.Log($"[FTG] scene: wrote {outPath} avatars={a.name},{b.name}");
            return 0;
        });
    }
}
