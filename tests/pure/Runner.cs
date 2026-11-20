using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator.PureTests
{
    // Loads each case's tables with TableLoader outside Unity and compares the model (ModelJson, avatar layer
    // included) and the face values (FaceEval) with tests/pure/expected/<case>.json.
    //   mono pure.exe <cases dir> <expected dir> [--write]
    // --write writes the expected files instead of comparing. Prints "pure: cases=<n> failed=<n>"; exit 1 on a failure.
    static class Runner
    {
        static int Main(string[] args)
        {
            var casesDir = args[0];
            var expectedDir = args[1];
            var write = args.Contains("--write");
            int n = 0, failed = 0;
            foreach (var dir in Directory.GetDirectories(casesDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(dir);
                var actual = Record(Path.Combine(dir, "avatar.jsonc")).ToString(Formatting.Indented) + "\n";
                var expectedPath = Path.Combine(expectedDir, name + ".json");
                n++;
                if (write) { File.WriteAllText(expectedPath, actual); continue; }
                var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath) : null;
                if (expected == actual) continue;
                failed++;
                Console.WriteLine($"pure: {name} differs from {expectedPath}");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "pure-" + name + ".json"), actual);
            }
            Console.WriteLine($"pure: cases={n} failed={failed}");
            return failed == 0 ? 0 : 1;
        }

        static JObject Record(string avatarTable)
        {
            TableModel m;
            try { m = TableLoader.Load(avatarTable.Replace('\\', '/')); }
            catch (TableLoader.TableException e) { return new JObject { ["error"] = e.Message.Replace('\\', '/') }; }
            var o = ModelJson.ToJson(m);
            o["avatarLayer"] = ModelJson.Avatar(m);
            var all = Enumerable.Range(0, NativeKeys.Count).ToDictionary(i => InputDef.TrackPrefix + ((VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingKey)i).ToString(), i => 1f);
            var faces = new JObject();
            void Face(string label, System.Collections.Generic.Dictionary<string, float> set, System.Collections.Generic.Dictionary<string, string> st)
            {
                foreach (var desktop in new[] { false, true })
                {
                    var shapes = FaceEval.Evaluate(m, set, st, desktop, withProperties: true);
                    faces[label + (desktop ? " desktop" : " vr")] = new JObject(shapes.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1))));
                }
            }
            Face("rest", new System.Collections.Generic.Dictionary<string, float>(), new System.Collections.Generic.Dictionary<string, string>());
            Face("all keys 1", all, new System.Collections.Generic.Dictionary<string, string>());
            foreach (var sm in m.StateMachines)
                foreach (var st in sm.States)
                    Face($"state {sm.Name}/{st.Name}", all, new System.Collections.Generic.Dictionary<string, string> { [sm.Name] = st.Name });
            o["faces"] = faces;
            return o;
        }
    }
}
