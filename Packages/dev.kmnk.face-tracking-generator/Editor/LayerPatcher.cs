using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;

namespace Kmnk.FaceTrackingGenerator
{
    // Changes to the avatar's own layers in a copied controller (design.md "配布元のレイヤーの調整").
    public static class LayerPatcher
    {
        // Parameters that the removed layers used. After the FT layers are added, the ones nothing uses any
        // more are removed from the controller (RemoveOrphanParameters).
        public static HashSet<string> RemovedLayerParameters = new HashSet<string>();

        public static List<string> Apply(AnimatorController c, LayerAdjust adj, string faceTrackParameter)
        {
            var log = new List<string>();
            RemovedLayerParameters = new HashSet<string>();
            if (adj == null) return log;

            // A layer the table names but the controller does not have is left out with a notice, not an error: the
            // template names the usual hand gesture layers, which an avatar without hand gestures does not have.
            // StaticChecker.MissingLayers reports the same names before generating.
            foreach (var name in adj.Remove)
            {
                var i = Array.FindIndex(c.layers, l => l.name == name);
                if (i < 0) { log.Add(Missing(name, "to remove", c)); continue; }
                RemovedLayerParameters.UnionWith(ParameterRefs.Of(c.layers[i].stateMachine));
                c.RemoveLayer(i);
                log.Add($"removed layer {name}");
            }

            if (adj.Weights.Count > 0)
            {
                var layers = c.layers;
                foreach (var kv in adj.Weights)
                {
                    var i = Array.FindIndex(layers, l => l.name == kv.Key);
                    if (i < 0) { log.Add(Missing(kv.Key, "for a weight", c)); continue; }
                    layers[i].defaultWeight = kv.Value;
                    log.Add($"weight {kv.Key} = {kv.Value}");
                }
                c.layers = layers;
            }

            if (adj.StopWhileFaceTrack.Count > 0)
            {
                // Stopped while FT/Gate says FT is on (FT/Active), so that the layers come back both when FaceTrack is
                // off and when there is no face tracking source (UserFaceTracking), and during AFK.
                if (!c.parameters.Any(p => p.name == FtBuilder.Active))
                    c.AddParameter(FtBuilder.Active, UnityEngine.AnimatorControllerParameterType.Float);
                foreach (var name in adj.StopWhileFaceTrack)
                {
                    var layer = c.layers.FirstOrDefault(l => l.name == name);
                    if (layer == null) { log.Add(Missing(name, "to stop while FT is on", c)); continue; }
                    var n = StopWhile(layer.stateMachine, FtBuilder.Active);
                    log.Add($"layer {name}: added '{FtBuilder.Active} < 0.5' to {n} transitions");
                }
            }
            // Muted by FtBuilder (FT/Gate), which leaves out the same names.
            foreach (var name in adj.MuteWhileFaceTrack)
                if (!c.layers.Any(l => l.name == name)) log.Add(Missing(name, "to mute while FT is on", c));
            return log;
        }

        static string Missing(string name, string what, AnimatorController c) =>
            $"notice: layer '{name}' {what} is not in {c.name}; left out";

        // The layer names of a table's layer lists ("remove", "weights", "stopWhileFaceTrack", "muteWhileFaceTrack")
        // that the controller does not have, as (list, name).
        public static List<(string list, string name)> MissingIn(AnimatorController c, LayerAdjust adj)
        {
            var have = new HashSet<string>(c.layers.Select(l => l.name));
            var r = new List<(string, string)>();
            if (adj == null) return r;
            foreach (var (list, names) in new[] { ("remove", (IEnumerable<string>)adj.Remove), ("weights", adj.Weights.Keys),
                         ("stopWhileFaceTrack", adj.StopWhileFaceTrack), ("muteWhileFaceTrack", adj.MuteWhileFaceTrack) })
                foreach (var n in names)
                    if (!have.Contains(n)) r.Add((list, n));
            return r;
        }

        // Adds "FaceTrack is false" to every transition that leaves the default state and to every
        // Any State transition that goes to another state. Going back to the default state stays possible.
        static int StopWhile(AnimatorStateMachine sm, string param)
        {
            var count = 0;
            var def = sm.defaultState;
            foreach (var t in sm.anyStateTransitions)
            {
                if (t.destinationState == def || HasCondition(t, param)) continue;
                t.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
                count++;
            }
            if (def != null)
                foreach (var t in def.transitions)
                {
                    if (t.destinationState == def || HasCondition(t, param)) continue;
                    t.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
                    count++;
                }
            return count;
        }

        public static List<string> RemoveOrphanParameters(AnimatorController c)
        {
            var used = new HashSet<string>();
            foreach (var l in c.layers) used.UnionWith(ParameterRefs.Of(l.stateMachine));
            var removed = new List<string>();
            foreach (var p in c.parameters.Where(p => RemovedLayerParameters.Contains(p.name) && !used.Contains(p.name)).ToList())
            {
                c.RemoveParameter(p);
                removed.Add(p.name);
            }
            return removed;
        }

        static bool HasCondition(AnimatorStateTransition t, string param) =>
            t.conditions.Any(c => c.parameter == param && c.mode == AnimatorConditionMode.Less);
    }

    // Parameter names a state machine reads or writes: conditions, BlendTree parameters, state parameters,
    // Parameter Driver names and sources, and Animator curves (AAP) in its clips.
    static class ParameterRefs
    {
        public static HashSet<string> Of(AnimatorStateMachine root)
        {
            var r = new HashSet<string>();
            var seenMotions = new HashSet<UnityEngine.Motion>();
            void Cond(AnimatorTransitionBase t) { foreach (var c in t.conditions) r.Add(c.parameter); }
            void Visit(UnityEngine.Motion m)
            {
                if (m == null || !seenMotions.Add(m)) return;
                if (m is BlendTree bt)
                {
                    r.Add(bt.blendParameter);
                    r.Add(bt.blendParameterY);
                    foreach (var ch in bt.children) { r.Add(ch.directBlendParameter); Visit(ch.motion); }
                }
                else if (m is UnityEngine.AnimationClip clip)
                {
                    foreach (var b in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                        if (b.type == typeof(UnityEngine.Animator)) r.Add(b.propertyName);
                }
            }
            void Behaviours(UnityEngine.StateMachineBehaviour[] bs)
            {
                foreach (var b in bs)
                    if (b is VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver d)
                        foreach (var p in d.parameters) { r.Add(p.name); r.Add(p.source); }
            }
            void Machine(AnimatorStateMachine sm)
            {
                Behaviours(sm.behaviours);
                foreach (var t in sm.anyStateTransitions) Cond(t);
                foreach (var t in sm.entryTransitions) Cond(t);
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    if (s.speedParameterActive) r.Add(s.speedParameter);
                    if (s.timeParameterActive) r.Add(s.timeParameter);
                    if (s.mirrorParameterActive) r.Add(s.mirrorParameter);
                    if (s.cycleOffsetParameterActive) r.Add(s.cycleOffsetParameter);
                    Behaviours(s.behaviours);
                    foreach (var t in s.transitions) Cond(t);
                    Visit(s.motion);
                }
                foreach (var csm in sm.stateMachines)
                {
                    Machine(csm.stateMachine);
                    foreach (var t in sm.GetStateMachineTransitions(csm.stateMachine)) Cond(t);
                }
            }
            Machine(root);
            r.Remove(null);
            r.Remove("");
            return r;
        }
    }
}
