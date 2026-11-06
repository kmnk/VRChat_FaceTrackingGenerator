using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Plays the avatar's Playable Layers (Base, Additive, Gesture, Action, FX) in the same order as VRChat
    // in Play Mode, and writes blendshape values, layer states and weights per snapshot as JSON Lines.
    // Not reproduced: viseme / auto eye look / auto blink blendshapes, sync (remote value steps and interval),
    // Base and Additive locomotion, IK. VRChat state behaviours (Animator Layer Control, Playable Layer Control,
    // Animator Tracking Control, Avatar Parameter Driver Set and Add) are run by this tool when a layer enters
    // a new state (at the start of the transition), also on layers with weight 0.
    //
    // Run (do not pass -quit; the tool exits Unity, exit code 1 on failure):
    //   scripts/unity-batch.sh --no-quit <project> Kmnk.FaceTrackingGenerator.ScenarioRunner.Run \
    //     -ftAvatar <prefab path> -ftMesh <mesh name> -ftScenario <scenario file> -ftOut <out .jsonl> [-ftFps <n>]
    //
    // Scenario file (one command per line, text after # is a comment):
    //   fps <n>                 frame rate (default 60). Write before scenario. -ftFps overrides it
    //   scenario <name>         re-create the avatar and reset parameters to defaults
    //   set <param> <value>     set a value (Bool 0/1, Int and Float numbers). Kept until changed
    //   wait <seconds>          advance that many seconds
    //   frames <n>              advance n frames
    //   watch <param>           include the parameter's value in snaps
    //   snap <label>            write the current state as one line
    public static class ScenarioRunner
    {
        static readonly string[] PlayableOrder = { "Base", "Additive", "Gesture", "Action", "FX" };

        const string SessionKey = "Kmnk.FaceTrackingGenerator.ScenarioRunner.Pending";
        const string ArgsKey = "Kmnk.FaceTrackingGenerator.ScenarioRunner.Args";

        // Command line: arguments come from -ftScenario, -ftOut, -ftAvatar, -ftMesh, -ftFps; Unity exits at the end.
        public static void Run()
        {
            var args = new[] { CliArgs.Require("-ftScenario"), CliArgs.Require("-ftOut"), CliArgs.Require("-ftAvatar"), CliArgs.Require("-ftMesh"), CliArgs.Get("-ftFps") ?? "", CliArgs.Get("-ftNdmf") ?? "" };
            Start(args);
        }

        // Editor window: the same run; the editor leaves Play Mode at the end instead of exiting.
        public static void RunInEditor(string scenario, string outPath, string avatar, string mesh) => Start(new[] { scenario, outPath, avatar, mesh, "" });

        static void Start(string[] args)
        {
            SessionState.SetString(ArgsKey, string.Join("\n", args));
            if (!Application.isPlaying)
            {
                SessionState.SetBool(SessionKey, true);
                EditorApplication.EnterPlaymode();
                return;
            }
            Execute(args);
        }

        [InitializeOnLoadMethod]
        static void RegisterPlayModeRun()
        {
            EditorApplication.playModeStateChanged += st =>
            {
                if (st != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
                SessionState.EraseBool(SessionKey);
                var code = 0;
                try { Execute(SessionState.GetString(ArgsKey, "").Split('\n')); }
                catch (Exception e) { Debug.LogException(e); code = 1; }
                if (Application.isBatchMode) EditorApplication.Exit(code);
                else EditorApplication.ExitPlaymode();
            };
        }

        static void Execute(string[] args)
        {
            var scenarioPath = args[0];
            var outPath = args[1];
            var avatarPath = args[2];
            var meshName = args[3];
            var fpsOverride = args.Length > 4 && args[4] != "" ? args[4] : null;
            var ndmf = args.Length > 5 && args[5] == "true";

            var sb = new StringBuilder();
            float fps = 60;
            Sim sim = null;
            var count = 0;
            try
            {
                foreach (var raw in File.ReadAllLines(scenarioPath))
                {
                    var line = raw.Split('#')[0].Trim();
                    if (line.Length == 0) continue;
                    var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    switch (tok[0])
                    {
                        case "fps": fps = F(tok[1]); break;
                        case "scenario":
                            if (fpsOverride != null) fps = F(fpsOverride);
                            sim?.Dispose();
                            sim = new Sim(avatarPath, meshName, 1f / fps, line.Substring(tok[0].Length).Trim(), ndmf);
                            break;
                        case "set": sim.Set(tok[1], F(tok[2])); break;
                        case "wait": sim.Advance(Mathf.RoundToInt(F(tok[1]) * fps)); break;
                        case "frames": sim.Advance(int.Parse(tok[1])); break;
                        case "watch": sim.Watch(tok[1]); break;
                        case "snap": sb.AppendLine(sim.Snap(line.Substring(tok[0].Length).Trim())); count++; break;
                        default: throw new FormatException("unknown command: " + raw);
                    }
                }
            }
            finally
            {
                sim?.Dispose();
            }
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[FTG] scenario: wrote {count} records to {outPath} (fps={fps.ToString(CultureInfo.InvariantCulture)})");
        }

        // Compares two records. Records are matched by (scenario, label). For each blendshape on mesh -ftMesh's
        // record ("shapes"), a value missing on one side counts as 0. Exits 1 when any difference is over the tolerance.
        //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.ScenarioRunner.Compare \
        //     -ftA <a.jsonl> -ftB <b.jsonl> [-ftTolerance 1.0] [-ftOut <diff.txt>]
        // With -ftExpect <file>, records listed as "intended" are not compared with A; instead the "expect"
        // lines are checked against B (see Settings/Scenarios/*.expect.txt for the format).
        public static void Compare()
        {
            var code = 0;
            try { code = CompareFiles(CliArgs.Require("-ftA"), CliArgs.Require("-ftB"), CliArgs.GetFloat("-ftTolerance", 1.0f), CliArgs.Get("-ftOut"), CliArgs.Get("-ftExpect")); }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            EditorApplication.Exit(code);
        }

        sealed class Expectations
        {
            public readonly HashSet<string> IntendedScenarios = new HashSet<string>();
            public readonly HashSet<(string, string)> IntendedRecords = new HashSet<(string, string)>();
            public readonly List<(string scenario, string label, string shape, float value, float tolerance)> Values = new List<(string, string, string, float, float)>();

            public bool IsIntended((string, string) key) => IntendedScenarios.Contains(key.Item1) || IntendedRecords.Contains(key);

            public static Expectations Read(string path)
            {
                var e = new Expectations();
                if (path == null) return e;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Split('#')[0].Trim();
                    if (line.Length == 0) continue;
                    var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (t[0] == "intended" && t.Length == 2) e.IntendedScenarios.Add(t[1]);
                    else if (t[0] == "intended" && t.Length == 3) e.IntendedRecords.Add((t[1], t[2]));
                    else if (t[0] == "expect" && (t.Length == 5 || t.Length == 6))
                        e.Values.Add((t[1], t[2], t[3], F(t[4]), t.Length == 6 ? F(t[5]) : 1.0f));
                    else throw new FormatException("unknown expectation: " + raw);
                }
                return e;
            }
        }

        public static int CompareFiles(string pathA, string pathB, float tolerance, string outPath, string expectPath = null)
        {
            var a = Load(pathA);
            var b = Load(pathB);
            var ex = Expectations.Read(expectPath);
            var lines = new List<string>();
            var intended = 0;
            foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal))
            {
                if (!a.ContainsKey(key) || !b.ContainsKey(key))
                {
                    lines.Add($"missing\t{key.Item1}\t{key.Item2}\t{(a.ContainsKey(key) ? "only A" : "only B")}");
                    continue;
                }
                if (ex.IsIntended(key)) { intended++; continue; }
                var sa = (JObject)a[key]["shapes"];
                var sbs = (JObject)b[key]["shapes"];
                var names = sa.Properties().Select(p => p.Name).Union(sbs.Properties().Select(p => p.Name)).OrderBy(n => n, StringComparer.Ordinal);
                foreach (var n in names)
                {
                    var va = sa[n]?.Value<float>() ?? 0f;
                    var vb = sbs[n]?.Value<float>() ?? 0f;
                    if (Mathf.Abs(va - vb) > tolerance + 1e-4f)
                        lines.Add($"diff\t{key.Item1}\t{key.Item2}\t{n}\t{N(va)}\t{N(vb)}");
                }
            }
            var expectFailures = 0;
            foreach (var (sc, label, shape, value, tol) in ex.Values)
            {
                if (!b.TryGetValue((sc, label), out var rec)) { lines.Add($"expect-missing\t{sc}\t{label}"); expectFailures++; continue; }
                var v = rec["shapes"][shape]?.Value<float>() ?? 0f;
                if (Mathf.Abs(v - value) > tol + 1e-4f) { lines.Add($"expect\t{sc}\t{label}\t{shape}\twant {N(value)}\tgot {N(v)}"); expectFailures++; }
            }
            var summary = $"[FTG] compare: records A={a.Count} B={b.Count} tolerance={N(tolerance)} intended={intended} expectations={ex.Values.Count} expectFailures={expectFailures} differences={lines.Count}";
            if (outPath != null) File.WriteAllText(outPath, string.Join("\n", lines) + (lines.Count > 0 ? "\n" : ""));
            foreach (var l in lines.Take(50)) Debug.Log("[FTG] " + l);
            Debug.Log(summary);
            return lines.Count == 0 ? 0 : 1;
        }

        static Dictionary<(string, string), JObject> Load(string path)
        {
            var d = new Dictionary<(string, string), JObject>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var o = JObject.Parse(line);
                var key = ((string)o["scenario"], (string)o["label"]);
                if (d.ContainsKey(key)) throw new FormatException($"duplicate record {key.Item1} / {key.Item2} in {path}");
                d[key] = o;
            }
            return d;
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        sealed class Blend
        {
            public int Playable;   // index in PlayableOrder
            public int Layer;      // -1: weight of the whole Playable Layer
            public float From, To, Duration, T;
        }

        sealed class Sim : IDisposable
        {
            readonly string name;
            readonly float dt;
            readonly GameObject avatar;
            readonly SkinnedMeshRenderer mesh;
            readonly PlayableGraph graph;
            readonly AnimationLayerMixerPlayable mixer;
            // index in PlayableOrder -> controller and playable (null when the avatar has none)
            readonly AnimatorController[] controllers = new AnimatorController[PlayableOrder.Length];
            readonly AnimatorControllerPlayable?[] playables = new AnimatorControllerPlayable?[PlayableOrder.Length];
            readonly int[] mixerInput = Enumerable.Repeat(-1, PlayableOrder.Length).ToArray();
            readonly Dictionary<int, string>[] stateNames = new Dictionary<int, string>[PlayableOrder.Length];
            readonly Dictionary<(int, int), StateMachineBehaviour[]>[] stateBehaviours =
                new Dictionary<(int, int), StateMachineBehaviour[]>[PlayableOrder.Length];
            readonly int[][] entered = new int[PlayableOrder.Length][];
            readonly List<Blend> blends = new List<Blend>();
            readonly List<string> events = new List<string>();
            readonly Dictionary<string, float> set = new Dictionary<string, float>();
            readonly List<string> watched = new List<string>();
            string eyes = "Tracking", mouth = "Tracking";
            int frame;

            public Sim(string avatarPath, string meshName, float dt, string name, bool ndmf = false)
            {
                this.name = name;
                this.dt = dt;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
                if (prefab == null) throw new FileNotFoundException(avatarPath);
                // Keep SDK behaviours from running by themselves; this tool runs them.
                VRC_AnimatorLayerControl.Initialize = null;
                VRC_PlayableLayerControl.Initialize = null;
                VRC_AnimatorTrackingControl.Initialize = null;
                VRC_AvatarParameterDriver.OnApplySettings = null;
                avatar = UnityEngine.Object.Instantiate(prefab);
                // Build-time output: run NDMF on the instance, as entering Play Mode does for avatars in the scene.
                if (ndmf) nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(avatar);
                if (ndmf)
                {
                    var fd = avatar.GetComponent<VRCAvatarDescriptor>();
                    var bs = fd.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
                    Debug.Log($"[FTG] scenario: face tracking enabled={fd.enableFaceTracking} allKeys={fd.useAllTrackingKeys} behaviors={bs.Count}");
                    foreach (var b in bs) Debug.Log($"[FTG] scenario: face tracking {b.targetMode} {b.outputParameterName} = {b.customExpression}");
                }

                var desc = avatar.GetComponent<VRCAvatarDescriptor>();
                var animator = avatar.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                // Without a camera in batchmode, culling would skip writing values.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                mesh = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == meshName);
                if (mesh == null) throw new ArgumentException($"mesh '{meshName}' not found in {avatarPath}");

                graph = PlayableGraph.Create("FaceTrackingGenerator.ScenarioRunner");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "out", animator);
                var used = new List<int>();
                foreach (var l in desc.baseAnimationLayers)
                {
                    var idx = Array.IndexOf(PlayableOrder, l.type.ToString());
                    if (idx < 0 || l.isDefault || l.animatorController == null) continue;
                    controllers[idx] = (AnimatorController)l.animatorController;
                    used.Add(idx);
                }
                used.Sort();
                mixer = AnimationLayerMixerPlayable.Create(graph, used.Count);
                output.SetSourcePlayable(mixer);
                for (int k = 0; k < used.Count; k++)
                {
                    var idx = used[k];
                    var c = controllers[idx];
                    var p = AnimatorControllerPlayable.Create(graph, c);
                    playables[idx] = p;
                    mixerInput[idx] = k;
                    graph.Connect(p, 0, mixer, k);
                    // In VRChat, Action stays at weight 0 until Playable Layer Control raises it.
                    mixer.SetInputWeight(k, PlayableOrder[idx] == "Action" ? 0f : 1f);
                    if (PlayableOrder[idx] == "Additive") mixer.SetLayerAdditive((uint)k, true);
                    // Gesture uses the descriptor's mask, or the first layer's mask when none.
                    if (PlayableOrder[idx] == "Gesture")
                    {
                        var dl = desc.baseAnimationLayers.First(x => x.type == VRCAvatarDescriptor.AnimLayerType.Gesture);
                        var m = dl.mask != null ? dl.mask : c.layers[0].avatarMask;
                        if (m != null) mixer.SetLayerMaskFromAvatarMask((uint)k, m);
                        events.Add($"gesture-mask {(m != null ? AssetDatabase.GetAssetPath(m) : "none")}");
                    }
                    stateNames[idx] = StateNames(c);
                    stateBehaviours[idx] = Behaviours(c);
                }

                // Defaults: controller defaults, then Expression Parameters defaults; IsLocal true.
                if (desc.expressionParameters != null)
                    foreach (var ep in desc.expressionParameters.parameters)
                        if (!string.IsNullOrEmpty(ep.name)) SetRaw(ep.name, ep.defaultValue);
                SetRaw("IsLocal", 1);
                graph.Play();
                events.Add("mode play");
            }

            static Dictionary<int, string> StateNames(AnimatorController c)
            {
                var d = new Dictionary<int, string>();
                foreach (var l in c.layers)
                {
                    void Walk(AnimatorStateMachine sm, string path)
                    {
                        foreach (var s in sm.states)
                            d[Animator.StringToHash(path + "." + s.state.name)] = s.state.name;
                        foreach (var sub in sm.stateMachines)
                            Walk(sub.stateMachine, path + "." + sub.stateMachine.name);
                    }
                    Walk(l.stateMachine, l.name);
                }
                return d;
            }

            static Dictionary<(int, int), StateMachineBehaviour[]> Behaviours(AnimatorController c)
            {
                var d = new Dictionary<(int, int), StateMachineBehaviour[]>();
                for (int li = 0; li < c.layers.Length; li++)
                {
                    void Walk(AnimatorStateMachine sm, string path)
                    {
                        foreach (var st in sm.states)
                            if (st.state.behaviours.Length > 0)
                                d[(li, Animator.StringToHash(path + "." + st.state.name))] = st.state.behaviours;
                        foreach (var sub in sm.stateMachines)
                            Walk(sub.stateMachine, path + "." + sub.stateMachine.name);
                    }
                    Walk(c.layers[li].stateMachine, c.layers[li].name);
                }
                return d;
            }

            // Runs behaviours of states that a layer newly entered this frame (at the start of the transition).
            // Behaviours on state machines themselves are not handled.
            void CheckEntries()
            {
                for (int i = 0; i < PlayableOrder.Length; i++)
                {
                    if (playables[i] == null) continue;
                    var p = playables[i].Value;
                    var n = controllers[i].layers.Length;
                    if (entered[i] == null) entered[i] = new int[n];
                    for (int l = 0; l < n; l++)
                    {
                        var h = p.IsInTransition(l) ? p.GetNextAnimatorStateInfo(l).fullPathHash : p.GetCurrentAnimatorStateInfo(l).fullPathHash;
                        if (h == entered[i][l]) continue;
                        entered[i][l] = h;
                        if (!stateBehaviours[i].TryGetValue((l, h), out var bs)) continue;
                        foreach (var b in bs) Apply(b, PlayableOrder[i] + "/" + controllers[i].layers[l].name);
                    }
                }
            }

            void Apply(StateMachineBehaviour b, string where)
            {
                switch (b)
                {
                    case VRC_AnimatorLayerControl s:
                    {
                        var idx = Array.IndexOf(PlayableOrder, s.playable.ToString());
                        events.Add($"{T()} {where}: layer-control {s.playable}[{s.layer}] -> {s.goalWeight} in {s.blendDuration}s");
                        if (idx >= 0 && playables[idx] != null)
                            StartBlend(idx, s.layer, playables[idx].Value.GetLayerWeight(s.layer), s.goalWeight, s.blendDuration);
                        break;
                    }
                    case VRC_PlayableLayerControl s:
                    {
                        var idx = Array.IndexOf(PlayableOrder, s.layer.ToString());
                        events.Add($"{T()} {where}: playable-control {s.layer} -> {s.goalWeight} in {s.blendDuration}s");
                        if (idx >= 0 && mixerInput[idx] >= 0)
                            StartBlend(idx, -1, mixer.GetInputWeight(mixerInput[idx]), s.goalWeight, s.blendDuration);
                        break;
                    }
                    case VRC_AnimatorTrackingControl s:
                    {
                        var e0 = eyes; var m0 = mouth;
                        if (s.trackingEyes != VRC_AnimatorTrackingControl.TrackingType.NoChange) eyes = s.trackingEyes.ToString();
                        if (s.trackingMouth != VRC_AnimatorTrackingControl.TrackingType.NoChange) mouth = s.trackingMouth.ToString();
                        if (e0 != eyes || m0 != mouth) events.Add($"{T()} {where}: tracking eyes={eyes} mouth={mouth}");
                        break;
                    }
                    case VRC_AvatarParameterDriver s:
                        foreach (var p in s.parameters)
                        {
                            events.Add($"{T()} {where}: driver {p.type} {p.name} {p.value.ToString(CultureInfo.InvariantCulture)}");
                            if (p.type == VRC_AvatarParameterDriver.ChangeType.Set) SetRaw(p.name, p.value);
                            else if (p.type == VRC_AvatarParameterDriver.ChangeType.Add) SetRaw(p.name, Get(p.name) + p.value);
                        }
                        break;
                }
            }

            void StartBlend(int idx, int layer, float from, float to, float dur)
            {
                blends.RemoveAll(x => x.Playable == idx && x.Layer == layer);
                blends.Add(new Blend { Playable = idx, Layer = layer, From = from, To = to, Duration = dur });
            }

            string T() => (frame * dt).ToString("0.000", CultureInfo.InvariantCulture);

            public void Watch(string p) => watched.Add(p);

            public void Set(string p, float v)
            {
                set[p] = v;
                SetRaw(p, v);
            }

            void SetRaw(string p, float v)
            {
                foreach (var pl in playables)
                {
                    if (pl == null) continue;
                    var x = pl.Value;
                    for (int i = 0; i < x.GetParameterCount(); i++)
                    {
                        var ap = x.GetParameter(i);
                        if (ap.name != p) continue;
                        switch (ap.type)
                        {
                            case AnimatorControllerParameterType.Bool: x.SetBool(ap.nameHash, v != 0); break;
                            case AnimatorControllerParameterType.Int: x.SetInteger(ap.nameHash, Mathf.RoundToInt(v)); break;
                            case AnimatorControllerParameterType.Float: x.SetFloat(ap.nameHash, v); break;
                            case AnimatorControllerParameterType.Trigger: if (v != 0) x.SetTrigger(ap.nameHash); break;
                        }
                    }
                }
            }

            float Get(string p)
            {
                foreach (var pl in playables)
                {
                    if (pl == null) continue;
                    var x = pl.Value;
                    for (int i = 0; i < x.GetParameterCount(); i++)
                    {
                        var ap = x.GetParameter(i);
                        if (ap.name != p) continue;
                        switch (ap.type)
                        {
                            case AnimatorControllerParameterType.Bool: return x.GetBool(ap.nameHash) ? 1 : 0;
                            case AnimatorControllerParameterType.Int: return x.GetInteger(ap.nameHash);
                            case AnimatorControllerParameterType.Float: return x.GetFloat(ap.nameHash);
                        }
                    }
                }
                return 0;
            }

            public void Advance(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    foreach (var b in blends.ToArray())
                    {
                        b.T += dt;
                        var w = b.Duration <= 0 ? b.To : Mathf.Lerp(b.From, b.To, Mathf.Clamp01(b.T / b.Duration));
                        if (b.Layer < 0) mixer.SetInputWeight(mixerInput[b.Playable], w);
                        else playables[b.Playable].Value.SetLayerWeight(b.Layer, w);
                        if (b.T >= b.Duration) blends.Remove(b);
                    }
                    graph.Evaluate(dt);
                    frame++;
                    CheckEntries();
                }
            }

            public string Snap(string label)
            {
                var sb = new StringBuilder();
                sb.Append("{\"scenario\":").Append(Q(name)).Append(",\"label\":").Append(Q(label));
                sb.Append(",\"t\":").Append(T());
                sb.Append(",\"set\":{").Append(string.Join(",", set.OrderBy(k => k.Key, StringComparer.Ordinal)
                    .Select(k => Q(k.Key) + ":" + N1(k.Value)))).Append("}");
                sb.Append(",\"params\":{").Append(string.Join(",", watched.Select(w => Q(w) + ":" + N3(Get(w))))).Append("}");
                sb.Append(",\"tracking\":{\"eyes\":").Append(Q(eyes)).Append(",\"mouth\":").Append(Q(mouth)).Append("}");
                sb.Append(",\"playableWeights\":{");
                sb.Append(string.Join(",", Enumerable.Range(0, PlayableOrder.Length).Where(i => mixerInput[i] >= 0)
                    .Select(i => Q(PlayableOrder[i]) + ":" + N1(mixer.GetInputWeight(mixerInput[i])))));
                sb.Append("},\"layers\":{");
                var ls = new List<string>();
                for (int i = 0; i < PlayableOrder.Length; i++)
                {
                    if (playables[i] == null) continue;
                    var p = playables[i].Value;
                    var c = controllers[i];
                    for (int l = 0; l < c.layers.Length; l++)
                    {
                        var h = p.GetCurrentAnimatorStateInfo(l).fullPathHash;
                        var st = stateNames[i].TryGetValue(h, out var s) ? s : "?" + h;
                        if (p.IsInTransition(l))
                        {
                            var nh = p.GetNextAnimatorStateInfo(l).fullPathHash;
                            st += "->" + (stateNames[i].TryGetValue(nh, out var ns) ? ns : "?" + nh);
                        }
                        ls.Add(Q(PlayableOrder[i] + "/" + c.layers[l].name) + ":{\"w\":" + N1(p.GetLayerWeight(l)) + ",\"state\":" + Q(st) + "}");
                    }
                }
                sb.Append(string.Join(",", ls)).Append("}");
                sb.Append(",\"shapes\":{");
                var shp = new List<string>();
                var m = mesh.sharedMesh;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    var w = mesh.GetBlendShapeWeight(i);
                    if (Mathf.Abs(w) >= 0.05f) shp.Add(Q(m.GetBlendShapeName(i)) + ":" + N1(w));
                }
                sb.Append(string.Join(",", shp)).Append("}");
                sb.Append(",\"events\":[").Append(string.Join(",", events.Select(Q))).Append("]}");
                events.Clear();
                return sb.ToString();
            }

            static string N3(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

            static string N1(float v) => (Mathf.Round(v * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);

            static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            public void Dispose()
            {
                if (graph.IsValid()) graph.Destroy();
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
    }
}
