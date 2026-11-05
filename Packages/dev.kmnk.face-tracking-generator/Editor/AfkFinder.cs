using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Finds the AFK clips an avatar already has, by the shape of its own AFK layer in the Action and FX controllers of
    // the Avatar Descriptor (not by names). From a transition on the built-in AFK parameter (If), skip waiting states
    // (no clip, or the clip of the state machine's default state) through AFK-on or condition-free transitions; the
    // first state with a clip is the Intro if a condition-free transition leads on to another clip state (the Loop),
    // else it is the Loop. The Loop must leave on AFK off (IfNot); where that leads, a clip that is not a waiting one
    // is the Outro.
    public static class AfkFinder
    {
        public sealed class Found
        {
            public string Layer;
            public AnimationClip Intro, Loop, Outro;
        }

        const string Afk = "AFK";

        public static AnimatorController Controller(VRCAvatarDescriptor desc, VRCAvatarDescriptor.AnimLayerType type)
        {
            if (desc == null || desc.baseAnimationLayers == null) return null;
            var l = desc.baseAnimationLayers.FirstOrDefault(x => x.type == type);
            return l.isDefault ? null : l.animatorController as AnimatorController;
        }

        // Every layer of the controller that has the shape (usually one). A shape with an Intro is preferred, in a
        // layer and across layers: layers that only hold a state while AFK is on (a blink or tail kept still) have no
        // Intro.
        public static List<Found> Find(AnimatorController c)
        {
            var result = new List<Found>();
            if (c == null) return result;
            foreach (var layer in c.layers)
            {
                var root = layer.stateMachine;
                var waitClip = root.defaultState != null ? root.defaultState.motion as AnimationClip : null;
                var all = AfkStarts(root).Select(s => Follow(s, waitClip)).Where(f => f != null).ToList();
                var pick = all.FirstOrDefault(f => f.Intro != null) ?? all.FirstOrDefault();
                if (pick == null) continue;
                pick.Layer = layer.name;
                result.Add(pick);
            }
            if (result.Any(f => f.Intro != null)) result.RemoveAll(f => f.Intro == null);
            return result;
        }

        // The states AFK-on transitions lead to, anywhere in the layer (sub-state machines included).
        static IEnumerable<AnimatorState> AfkStarts(AnimatorStateMachine root)
        {
            foreach (var (sm, s) in States(root))
                foreach (var t in s.transitions)
                    if (t.conditions.Any(x => x.parameter == Afk && x.mode == AnimatorConditionMode.If) && Target(t) is AnimatorState to)
                        yield return to;
            foreach (var sm in Machines(root))
                foreach (var t in sm.anyStateTransitions)
                    if (t.conditions.Any(x => x.parameter == Afk && x.mode == AnimatorConditionMode.If) && Target(t) is AnimatorState to)
                        yield return to;
        }

        static Found Follow(AnimatorState s, AnimationClip waitClip)
        {
            bool Waiting(AnimatorState x) => !(x.motion is AnimationClip clip) || clip == waitClip;
            bool On(AnimatorStateTransition t) => t.conditions.All(x => x.parameter == Afk && x.mode == AnimatorConditionMode.If);
            var seen = new HashSet<AnimatorState>();
            while (s != null && Waiting(s) && seen.Add(s))
                s = s.transitions.Where(On).Select(Target).OfType<AnimatorState>().FirstOrDefault();
            if (s == null || Waiting(s)) return null;
            var next = s.transitions.Where(t => t.conditions.Length == 0).Select(Target).OfType<AnimatorState>().FirstOrDefault(x => x != s && !Waiting(x));
            var intro = next != null ? s : null;
            var loop = next ?? s;
            var off = loop.transitions.FirstOrDefault(t => t.conditions.Any(x => x.parameter == Afk && x.mode == AnimatorConditionMode.IfNot));
            if (off == null) return null;
            var outroState = Target(off) as AnimatorState;
            var outro = outroState != null && !Waiting(outroState) && outroState != loop ? (AnimationClip)outroState.motion : null;
            return new Found { Intro = intro != null ? (AnimationClip)intro.motion : null, Loop = (AnimationClip)loop.motion, Outro = outro };
        }

        // A transition into a sub-state machine enters its default state.
        static object Target(AnimatorStateTransition t)
        {
            if (t.destinationState != null) return t.destinationState;
            var sm = t.destinationStateMachine;
            while (sm != null && sm.defaultState == null && sm.stateMachines.Length > 0) sm = sm.stateMachines[0].stateMachine;
            return sm?.defaultState;
        }

        static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine sm)
        {
            yield return sm;
            foreach (var c in sm.stateMachines) foreach (var x in Machines(c.stateMachine)) yield return x;
        }

        static IEnumerable<(AnimatorStateMachine, AnimatorState)> States(AnimatorStateMachine root) =>
            Machines(root).SelectMany(sm => sm.states.Select(s => (sm, s.state)));

        // Fills the empty ones of the six AFK clips of the avatar table; returns the result lines (spec-afk R4).
        public static List<string> FillTable(string avatarTable, VRCAvatarDescriptor desc)
        {
            var log = new List<string>();
            var c = TableLoader.Load(avatarTable).AfkClips;
            var clips = new[] { c.ActionIntro, c.ActionLoop, c.ActionOutro, c.FxIntro, c.FxLoop, c.FxOutro }.Select(AfkBuilder.Load).ToArray();
            var keys = new[] { "actionIntro", "actionLoop", "actionOutro", "fxIntro", "fxLoop", "fxOutro" };
            var changed = false;
            foreach (var (type, label, at) in new[] { (VRCAvatarDescriptor.AnimLayerType.Action, "Action", 0), (VRCAvatarDescriptor.AnimLayerType.FX, "FX", 3) })
            {
                var ctrl = Controller(desc, type);
                if (ctrl == null) { log.Add($"afk {label}: no controller"); continue; }
                var found = Find(ctrl);
                if (found.Count == 0) { log.Add($"afk {label}: no AFK layer found ({ctrl.name})"); continue; }
                if (found.Count > 1) { log.Add($"afk {label}: more than one AFK layer, not filled ({ctrl.name}: {string.Join(", ", found.Select(f => f.Layer))})"); continue; }
                var f0 = found[0];
                var got = new[] { f0.Intro, f0.Loop, f0.Outro };
                for (int i = 0; i < 3; i++)
                {
                    var k = keys[at + i];
                    if (clips[at + i] != null) { log.Add($"afk {k}: kept"); continue; }
                    if (got[i] == null) { log.Add($"afk {k}: none ({label}: {ctrl.name}/{f0.Layer})"); continue; }
                    clips[at + i] = got[i];
                    changed = true;
                    log.Add($"afk {k}: {AssetDatabase.GetAssetPath(got[i])} ({label}: {ctrl.name}/{f0.Layer})");
                }
            }
            if (changed) TableWriter.SetAfkClips(avatarTable, clips);
            return log;
        }
    }
}
