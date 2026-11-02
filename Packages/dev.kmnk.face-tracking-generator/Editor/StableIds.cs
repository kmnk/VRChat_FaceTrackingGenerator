using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kmnk.FaceTrackingGenerator
{
    // Unity gives random local file IDs to objects added to an asset, so regenerating the same controller
    // would change every ID and show a git diff (spec R9). After the asset is saved, this replaces the IDs of
    // generated objects with IDs derived from a stable key (layer, state, transition order, ...), and sorts the
    // YAML documents by ID the way Unity writes them.
    public static class StableIds
    {
        // Collects generated objects with stable keys by walking the layers and clips the generator added.
        public sealed class Registry
        {
            readonly List<(Object obj, string key)> items = new List<(Object, string)>();
            readonly HashSet<string> keys = new HashSet<string>();
            readonly HashSet<Object> seen = new HashSet<Object>();
            readonly string assetPath;

            public Registry(string assetPath) { this.assetPath = assetPath; }

            public IReadOnlyList<(Object obj, string key)> Items => items;

            public void Add(Object obj, string key)
            {
                if (obj == null || !seen.Add(obj)) return;
                if (!keys.Add(key)) throw new InvalidOperationException("duplicate stable key: " + key);
                items.Add((obj, key));
            }

            public void AddLayer(AnimatorControllerLayer layer)
            {
                AddStateMachine(layer.stateMachine, "L:" + layer.name);
            }

            public void AddStateMachine(AnimatorStateMachine sm, string key)
            {
                Add(sm, key);
                for (int i = 0; i < sm.behaviours.Length; i++) Add(sm.behaviours[i], key + "/B" + i);
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    var sk = key + "/S:" + s.name;
                    Add(s, sk);
                    for (int i = 0; i < s.behaviours.Length; i++) Add(s.behaviours[i], sk + "/B" + i);
                    for (int i = 0; i < s.transitions.Length; i++) Add(s.transitions[i], sk + "/T" + i);
                    AddMotion(s.motion, sk + "/M");
                }
                for (int i = 0; i < sm.anyStateTransitions.Length; i++) Add(sm.anyStateTransitions[i], key + "/AnyT" + i);
                for (int i = 0; i < sm.entryTransitions.Length; i++) Add(sm.entryTransitions[i], key + "/EntryT" + i);
                foreach (var csm in sm.stateMachines) AddStateMachine(csm.stateMachine, key + "/SM:" + csm.stateMachine.name);
            }

            void AddMotion(Motion m, string key)
            {
                if (m is BlendTree bt)
                {
                    Add(bt, key);
                    var children = bt.children;
                    for (int i = 0; i < children.Length; i++) AddMotion(children[i].motion, key + "/C" + i);
                }
                else if (m is AnimationClip clip && AssetDatabase.GetAssetPath(clip) == assetPath)
                {
                    Add(clip, "Clip:" + clip.name);
                }
            }
        }

        // Removes sub-assets that the controller no longer reaches (left behind by RemoveLayer, for example).
        // Returns the number of removed objects.
        public static int RemoveUnreachable(AnimatorController c)
        {
            var path = AssetDatabase.GetAssetPath(c);
            var reach = new HashSet<Object> { c };
            void Motion(Motion m)
            {
                if (m == null || !reach.Add(m)) return;
                if (m is BlendTree bt) foreach (var ch in bt.children) Motion(ch.motion);
            }
            void Machine(AnimatorStateMachine sm)
            {
                if (sm == null || !reach.Add(sm)) return;
                foreach (var b in sm.behaviours) reach.Add(b);
                foreach (var t in sm.anyStateTransitions) reach.Add(t);
                foreach (var t in sm.entryTransitions) reach.Add(t);
                foreach (var cs in sm.states)
                {
                    reach.Add(cs.state);
                    foreach (var b in cs.state.behaviours) reach.Add(b);
                    foreach (var t in cs.state.transitions) reach.Add(t);
                    Motion(cs.state.motion);
                }
                foreach (var csm in sm.stateMachines)
                {
                    Machine(csm.stateMachine);
                    foreach (var t in sm.GetStateMachineTransitions(csm.stateMachine)) reach.Add(t);
                }
            }
            foreach (var l in c.layers) Machine(l.stateMachine);
            var removed = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null || reach.Contains(o)) continue;
                Object.DestroyImmediate(o, true);
                removed++;
            }
            return removed;
        }

        static readonly Regex DocHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)(.*)$", RegexOptions.Multiline);

        // Call after AssetDatabase.SaveAssets(). Rewrites the asset file and re-imports it.
        public static void Apply(string assetPath, Registry registry)
        {
            var map = new Dictionary<long, long>();
            foreach (var (obj, key) in registry.Items)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long id))
                    throw new InvalidOperationException("object is not saved in an asset: " + key);
                if (AssetDatabase.GetAssetPath(obj) != assetPath)
                    throw new InvalidOperationException($"object {key} is in {AssetDatabase.GetAssetPath(obj)}, not {assetPath}");
                map[id] = Hash(key);
            }

            var text = File.ReadAllText(assetPath);
            var headerEnd = text.IndexOf("\n--- ", StringComparison.Ordinal);
            if (headerEnd < 0) throw new InvalidOperationException("unexpected asset format: " + assetPath);
            var header = text.Substring(0, headerEnd + 1);
            var body = text.Substring(headerEnd + 1);

            var existing = DocHeader.Matches(body).Cast<Match>().Select(m => long.Parse(m.Groups[2].Value)).ToList();
            var unknown = existing.Where(id => !map.ContainsKey(id)).ToHashSet();
            foreach (var v in map.Values)
                if (unknown.Contains(v)) throw new InvalidOperationException("stable id collides with an existing id: " + v);
            if (map.Values.Distinct().Count() != map.Count) throw new InvalidOperationException("stable id hash collision");

            // Replace "&<id>" in headers and "fileID: <id>" in references.
            body = Regex.Replace(body, @"(?<=^--- !u!\d+ &)-?\d+", m => Remap(m.Value, map), RegexOptions.Multiline);
            body = Regex.Replace(body, @"(?<=fileID: )-?\d+", m => Remap(m.Value, map));

            // Split into documents and sort by id like Unity does.
            var docs = new List<(long id, string text)>();
            var starts = DocHeader.Matches(body).Cast<Match>().ToList();
            for (int i = 0; i < starts.Count; i++)
            {
                var s = starts[i].Index;
                var e = i + 1 < starts.Count ? starts[i + 1].Index : body.Length;
                docs.Add((long.Parse(starts[i].Groups[2].Value), body.Substring(s, e - s)));
            }
            var sb = new StringBuilder(header);
            foreach (var d in docs.OrderBy(d => d.id)) sb.Append(d.text);
            var result = sb.ToString();
            if (result != text)
            {
                File.WriteAllText(assetPath, result);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        static string Remap(string s, Dictionary<long, long> map)
        {
            var id = long.Parse(s);
            return map.TryGetValue(id, out var n) ? n.ToString() : s;
        }

        // FNV-1a 64-bit of the key, kept positive and away from small IDs Unity uses for main objects.
        static long Hash(string key)
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(key))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            var v = (long)(h & 0x3FFFFFFFFFFFFFFFUL);
            return v < 100000000 ? v + 100000000 : v;
        }
    }
}
