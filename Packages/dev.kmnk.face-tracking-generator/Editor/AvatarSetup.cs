using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
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

        // What "Fill from Prefab" sets in the avatar table, decided from the table as it is now (saved or not).
        public sealed class FillResult
        {
            public GameObject Prefab;
            public string Mesh;               // null: no face mesh found
            public UnityEngine.Object Menu;   // null: the avatar has no Expressions Menu
            public AnimationClip[] AfkClips;  // the six clips after filling the empty ones
            public bool AfkChanged;
            public JObject Parts;             // the "parts" after filling
            public bool PartsChanged;
            public readonly List<string> Log = new List<string>();
        }

        // Decides the prefab, mesh and menu of the avatar table from an avatar (scene object or prefab asset), the
        // empty AFK clips from the avatar's own AFK (AfkFinder), and the parts without a blendshape (FillParts' rule).
        // Writes nothing: FillTable writes the result to the file, Apply to a table held in memory.
        public static FillResult Fill(JObject avatarTable, JObject expressionTable, GameObject avatar)
        {
            var r = new FillResult();
            var prefab = PrefabOf(avatar) ?? throw new TableLoader.TableException($"{avatar.name} is not a prefab or an instance of one");
            var desc = prefab.GetComponent<VRCAvatarDescriptor>() ?? throw new TableLoader.TableException($"{prefab.name} has no VRCAvatarDescriptor");
            r.Prefab = prefab;
            r.Log.Add("prefab: " + AssetDatabase.GetAssetPath(prefab));

            // The face mesh: the one the descriptor uses for visemes, else the one with the most blendshapes.
            var face = desc.VisemeSkinnedMesh != null ? desc.VisemeSkinnedMesh
                : prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.sharedMesh != null).OrderByDescending(x => x.sharedMesh.blendShapeCount).FirstOrDefault();
            if (face != null)
            {
                r.Mesh = AnimationUtility.CalculateTransformPath(face.transform, prefab.transform);
                r.Log.Add("mesh: " + r.Mesh);
            }
            if (desc.expressionsMenu != null)
            {
                r.Menu = desc.expressionsMenu;
                r.Log.Add("menu: " + AssetDatabase.GetAssetPath(desc.expressionsMenu));
            }
            r.AfkClips = AfkFinder.AfkKeys.Select(k => AfkBuilder.Load(TableLoader.AssetRefOf((avatarTable["afkClips"] as JObject)?[k]))).ToArray();
            r.AfkChanged = AfkFinder.Fill(r.AfkClips, desc, r.Log);
            r.Parts = (avatarTable["parts"] as JObject)?.DeepClone() as JObject ?? new JObject();
            if (face != null) r.PartsChanged = FillParts(r.Parts, avatarTable, expressionTable, prefab, face, r.Log);
            return r;
        }

        // Writes prefab, mesh and menu of the avatar table from an avatar, and the empty AFK clips and parts, as text
        // edits that keep the file's comments (Cli.FillFromAvatar).
        public static List<string> FillTable(string avatarTable, GameObject avatar)
        {
            var exprPath = TableLoader.ExpressionPath(avatarTable);
            var r = Fill(TableLoader.Read(avatarTable), File.Exists(exprPath) ? TableLoader.Read(exprPath) : null, avatar);
            TableWriter.SetTopLevel(avatarTable, "prefab", TableWriter.AssetRefJson(r.Prefab));
            if (r.Mesh != null) TableWriter.SetTopLevel(avatarTable, "mesh", TableWriter.StringJson(r.Mesh));
            if (r.Menu != null) TableWriter.SetTopLevel(avatarTable, "menu", TableWriter.AssetRefJson(r.Menu));
            if (r.AfkChanged) TableWriter.SetAfkClips(avatarTable, r.AfkClips);
            if (r.PartsChanged) TableWriter.SetTopLevel(avatarTable, "parts", r.Parts.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\n", "\n  "));
            return r.Log;
        }

        // Puts the result into an avatar table held in memory (the settings editor's "Fill from Prefab", with Undo).
        public static void Apply(JObject avatarTable, FillResult r)
        {
            avatarTable["prefab"] = AssetRefJson(r.Prefab);
            if (r.Mesh != null) avatarTable["mesh"] = r.Mesh;
            if (r.Menu != null) avatarTable["menu"] = AssetRefJson(r.Menu);
            if (r.AfkChanged)
            {
                var o = new JObject();
                for (int i = 0; i < AfkFinder.AfkKeys.Length; i++) o[AfkFinder.AfkKeys[i]] = r.AfkClips[i] == null ? JValue.CreateNull() : (JToken)AssetRefJson(r.AfkClips[i]);
                avatarTable["afkClips"] = o;
            }
            if (r.PartsChanged) avatarTable["parts"] = r.Parts;
        }

        static JObject AssetRefJson(UnityEngine.Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            return new JObject { ["guid"] = AssetDatabase.AssetPathToGUID(path), ["path"] = path };
        }

        // For the parts of the expression table that have no blendshape and are not marked missing: the blendshape whose
        // name equals the part's name after the dot, ignoring capitals (spec R5), on the face mesh, else on another mesh
        // of the avatar (written with its mesh). Others stay Not set. Edits parts; returns whether it set any.
        static bool FillParts(JObject parts, JObject avatar, JObject expr, GameObject prefab, SkinnedMeshRenderer face, List<string> log)
        {
            var meshes = new[] { face }.Concat(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x != face && x.sharedMesh != null))
                .Select(x => (path: AnimationUtility.CalculateTransformPath(x.transform, prefab.transform), shapes: Enumerable.Range(0, x.sharedMesh.blendShapeCount).Select(x.sharedMesh.GetBlendShapeName).ToList()))
                .ToList();
            var missing = new HashSet<string>((avatar["missingParts"] as JArray)?.Select(x => (string)x) ?? new string[0]);
            int found = 0, notFound = 0;
            foreach (var part in PartNames.Used(avatar, expr))
            {
                if (parts[part] != null || missing.Contains(part) || !PartNames.IsForm(part)) continue;
                var movement = PartNames.Movement(part);
                string shape = null, mesh = null;
                foreach (var (path, shapes) in meshes)
                {
                    shape = shapes.FirstOrDefault(x => x == movement) ?? shapes.FirstOrDefault(x => string.Equals(x, movement, StringComparison.OrdinalIgnoreCase));
                    if (shape != null) { mesh = path; break; }
                }
                if (shape == null) { notFound++; continue; }
                parts[part] = mesh == meshes[0].path ? (JToken)shape : new JObject { ["mesh"] = mesh, ["shape"] = shape };
                found++;
            }
            log.Add($"parts: {found} face movements set from blendshapes with the same name, {notFound} not found (Not set)");
            return found > 0;
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
            var picked = (DefaultAsset)EditorGUILayout.ObjectField(EditorText.Label("Settings file", "tip.table"), current, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Set Face Tracking Generator table");
                s.avatarTablePath = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                s.avatarTableGuid = picked != null ? AssetDatabase.AssetPathToGUID(s.avatarTablePath) : "";
                EditorUtility.SetDirty(s);
                current = picked;
            }
            // No settings file, or one that is not found by its GUID or its path.
            if (current == null) EditorGUILayout.HelpBox(EditorText.Get("inspector.noFile"), MessageType.Warning);
            using (new EditorGUI.DisabledScope(current == null))
                if (GUILayout.Button("Open Settings Editor")) TableEditorWindow.OpenTable(AssetDatabase.GetAssetPath(current));
            EditorGUILayout.HelpBox(EditorText.Get("inspector.info"), MessageType.Info);
        }
    }
}
