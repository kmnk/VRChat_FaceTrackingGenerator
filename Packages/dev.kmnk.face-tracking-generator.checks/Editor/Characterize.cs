using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Records the behaviour of the command line entry points for each characterization case in one Unity run, so that
    // the records before and after a refactoring can be compared with diff -r. Called by scripts/characterize.sh.
    //   -ftCases <file>: one case per line, "<id> <avatar table>"; # starts a comment
    //   -ftOut <dir>: writes <dir>/<id>/load.json, load.log, load-full.json (Cli.Load -ftFull 1), generate.log, output/ (the output folder after generating),
    //                 check.log, curve-vr.jsonl, curve-desktop.jsonl, queries.jsonl
    // Each command runs as from the command line (Cli.Load, Generate, Check, Curve) with its exit code in the log.
    public static class Characterize
    {
        public static void Run() => Cli.Run(() =>
        {
            var cases = File.ReadAllLines(CliArgs.Require("-ftCases"))
                .Select(l => l.Split('#')[0].Trim()).Where(l => l != "")
                .Select(l => l.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries)).ToList();
            var outRoot = CliArgs.Require("-ftOut");
            foreach (var c in cases)
            {
                var id = c[0];
                var table = c[1];
                var dir = Path.Combine(outRoot, id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "load.log"), Capture(() => Cli.Load(), "-ftTable", table, "-ftOut", Path.Combine(dir, "load.json")));
                // The full model (avatar layer included); without -ftFull the output above is the default one.
                if (CliArgs.Get("-ftFull") != "0") Capture(() => Cli.Load(), "-ftTable", table, "-ftFull", "1", "-ftOut", Path.Combine(dir, "load-full.json"));
                File.WriteAllText(Path.Combine(dir, "generate.log"), Capture(() => Cli.Generate(), "-ftTable", table));
                TableModel model;
                try { model = TableLoader.Load(table); }
                catch (TableLoader.TableException e)
                {
                    Debug.Log($"[FTG] characterize: {id} stopped: {e.Message}");
                    continue;
                }
                CopyDir(model.OutputFolder, Path.Combine(dir, "output"));
                File.WriteAllText(Path.Combine(dir, "check.log"), Capture(() => Cli.Check(), "-ftTable", table));
                var queries = Path.Combine(dir, "queries.jsonl");
                File.WriteAllText(queries, string.Join("\n", Queries(model)) + "\n");
                Capture(() => Cli.Curve(), "-ftTable", table, "-ftQueries", queries, "-ftOut", Path.Combine(dir, "curve-vr.jsonl"));
                Capture(() => Cli.Curve(), "-ftTable", table, "-ftQueries", queries, "-ftDesktop", "1", "-ftOut", Path.Combine(dir, "curve-desktop.jsonl"));
                Debug.Log($"[FTG] characterize: {id} done");
            }
            return 0;
        });

        // Runs a command with the given arguments and returns its [FTG] lines and exit code. Absolute paths of the
        // project are written relative to it.
        static string Capture(System.Action cli, params string[] args)
        {
            var lines = new List<string>();
            void OnLog(string msg, string stack, LogType type)
            {
                foreach (var l in msg.Split('\n'))
                    if (l.StartsWith("[FTG]")) lines.Add(l);
            }
            Application.logMessageReceived += OnLog;
            var keep = Cli.KeepRunning;
            CliArgs.Override = new[] { "Unity" }.Concat(args).ToArray();
            Cli.KeepRunning = true;
            try { cli(); }
            finally
            {
                CliArgs.Override = null;
                Cli.KeepRunning = keep;
                Application.logMessageReceived -= OnLog;
            }
            lines.Add("exit " + Cli.LastExitCode);
            var root = Path.GetFullPath(".").Replace('\\', '/') + "/";
            return string.Join("\n", lines.Select(l => l.Replace(root, ""))) + "\n";
        }

        // The face with nothing moving, with every tracking key at 1, and in each state of each state machine.
        static IEnumerable<string> Queries(TableModel m)
        {
            yield return new JObject { ["label"] = "rest" }.ToString(Formatting.None);
            var all = new JObject();
            for (int i = 0; i < NativeKeys.Count; i++) all[((VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingKey)i).ToString()] = 1;
            yield return new JObject { ["label"] = "all keys 1", ["set"] = all }.ToString(Formatting.None);
            foreach (var sm in m.StateMachines)
                foreach (var st in sm.States)
                    yield return new JObject { ["label"] = $"state {sm.Name}/{st.Name}", ["set"] = all, ["state"] = new JObject { [sm.Name] = st.Name } }.ToString(Formatting.None);
        }

        static void CopyDir(string src, string dest)
        {
            if (!Directory.Exists(src)) return;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(src.Length + 1).Replace('\\', '/');
                // The build-time output is recorded by the scenario with NDMF (characterize.sh), not here.
                if (rel == "Build.meta" || rel.StartsWith("Build/")) continue;
                var to = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to, true);
            }
        }
    }
}
