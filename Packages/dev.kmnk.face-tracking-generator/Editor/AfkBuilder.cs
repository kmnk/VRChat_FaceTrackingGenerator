using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Adds the AFK animation driven by the AFK flag (design.md "AFK の Animator の組み方", spec R30-R32):
    //   Action: AFK/Action  Idle -> Prepare -> Intro -> Loop -> Outro -> BlendOut -> Idle
    //   FX:     AFK/FX      the same states and conditions with the face clips; writes AFK/Playing
    // Eye and mouth Tracking Control are never written here.
    public sealed class AfkBuilder
    {
        public const string Playing = "AFK/Playing";
        const float PrepareBlend = 0.5f;   // the same as the avatars' own AFK (Playable Layer Control)
        const float BlendOutBlend = 0.25f;

        readonly TableModel m;
        public readonly List<AnimatorControllerLayer> ActionLayers = new List<AnimatorControllerLayer>();
        public readonly List<AnimatorControllerLayer> FxLayers = new List<AnimatorControllerLayer>();

        public AfkBuilder(TableModel model) { m = model; }

        public static AnimationClip Load(AssetRef r)
        {
            if (r == null) return null;
            var path = !string.IsNullOrEmpty(r.Guid) ? AssetDatabase.GUIDToAssetPath(r.Guid) : r.Path;
            if (string.IsNullOrEmpty(path)) path = r.Path;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        // The loop clips are required (R31). Without them the AFK part is not built.
        public bool CanBuild(out string reason)
        {
            reason = null;
            if (Load(m.AfkClips.ActionLoop) == null) reason = "no Action loop clip is chosen (afkClips.actionLoop)";
            else if (Load(m.AfkClips.FxLoop) == null) reason = "no FX loop clip is chosen (afkClips.fxLoop)";
            return reason == null;
        }

        // The AFK group the FT gate waits on: the generated AFK writes AFK/Playing until the end of Outro
        // and does not touch eye tracking, so FT can come back without waiting.
        public static AfkGroup Group() => new AfkGroup { Parameters = new List<string> { Playing }, Wait = 0 };

        public void BuildAction(AnimatorController action)
        {
            var u = new AnimUtil(action);
            Params(u);
            var wd = action.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state.writeDefaultValues).DefaultIfEmpty(false).First();
            var layer = u.NewLayer("AFK/Action");
            ActionLayers.Add(layer);
            Build(u, layer.stateMachine, wd, Mode.Action,
                Load(m.AfkClips.ActionIntro), Load(m.AfkClips.ActionLoop), Load(m.AfkClips.ActionOutro));
        }

        // Two FX layers: AFK/FX plays the face clips, AFK/Flag writes AFK/Playing with parameter clips as long
        // as the Action clips, so FT comes back when the Action Outro ends.
        public void BuildFx(AnimatorController fx)
        {
            var u = new AnimUtil(fx);
            Params(u);
            u.EnsureParameter(Playing, AnimatorControllerParameterType.Float);
            var face = u.NewLayer("AFK/FX");
            FxLayers.Add(face);
            Build(u, face.stateMachine, true, Mode.Face,
                Load(m.AfkClips.FxIntro), Load(m.AfkClips.FxLoop), Load(m.AfkClips.FxOutro));
            var flag = u.NewLayer("AFK/Flag");
            FxLayers.Add(flag);
            Build(u, flag.stateMachine, true, Mode.Flag,
                Load(m.AfkClips.ActionIntro), Load(m.AfkClips.ActionLoop), Load(m.AfkClips.ActionOutro));
        }

        enum Mode { Action, Face, Flag }

        void Params(AnimUtil u)
        {
            u.EnsureParameter(m.AfkPlayParameter, AnimatorControllerParameterType.Bool);
            u.EnsureParameter("VRCEmote", AnimatorControllerParameterType.Int);
            u.EnsureParameter(FtBuilder.One, AnimatorControllerParameterType.Float, 1);
            foreach (var p in m.AfkGroups.SelectMany(g => g.Parameters)) u.EnsureParameter(p, AnimatorControllerParameterType.Bool);
        }

        void Build(AnimUtil u, AnimatorStateMachine sm, bool wd, Mode mode, AnimationClip intro, AnimationClip loop, AnimationClip outro)
        {
            var flag = m.AfkPlayParameter;
            var prefix = mode == Mode.Action ? "AFK/Action" : mode == Mode.Face ? "AFK/FX" : "AFK/Flag";

            // Flag mode: a parameter clip as long as the clip it stands for, so exit times match the Action layer.
            Motion M(string name, AnimationClip clip, float playing)
            {
                if (mode != Mode.Flag) return clip;
                var len = clip != null && name != "Loop" ? clip.length : 0;
                return u.ParamClip($"{prefix}/{name}", new[] { (Playing, playing) }, len);
            }

            AnimatorState S(string name, Motion motion, int row)
            {
                var s = AnimUtil.State(sm, name, motion, new Vector3(300 + 220 * row, 0));
                s.writeDefaultValues = wd;
                return s;
            }

            var idle = S("Idle", M("Idle", null, 0), 0);
            var prepare = S("Prepare", M("Prepare", null, 1), 1);
            var introS = intro != null ? S("Intro", M("Intro", intro, 1), 2) : null;
            var loopS = S("Loop", M("Loop", loop, 1), 3);
            var outroS = outro != null ? S("Outro", M("Outro", outro, 1), 4) : null;
            var blendOut = S("BlendOut", M("BlendOut", null, 0), 5);
            sm.defaultState = idle;

            if (mode == Mode.Action)
            {
                var pc = prepare.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                pc.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                pc.goalWeight = 1;
                pc.blendDuration = PrepareBlend;
                Body(prepare, VRC_AnimatorTrackingControl.TrackingType.Animation);
                var bc = blendOut.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                bc.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                bc.goalWeight = 0;
                bc.blendDuration = BlendOutBlend;
                Body(blendOut, VRC_AnimatorTrackingControl.TrackingType.Tracking);
            }

            // Start only while no emote and no avatar AFK is playing, so the Action weight is not fought over.
            var start = AnimUtil.Transition(idle, prepare);
            u.When(start, flag, true);
            start.AddCondition(AnimatorConditionMode.Equals, 0, "VRCEmote");
            foreach (var p in m.AfkGroups.SelectMany(g => g.Parameters)) u.When(start, p, false);

            var first = introS ?? loopS;
            u.When(AnimUtil.Transition(prepare, first), flag, true);
            u.When(AnimUtil.Transition(prepare, outroS ?? blendOut), flag, false);
            if (introS != null)
            {
                var t = AnimUtil.Transition(introS, loopS);
                t.hasExitTime = true;
                t.exitTime = 1;
                u.When(AnimUtil.Transition(introS, outroS ?? blendOut), flag, false);
            }
            u.When(AnimUtil.Transition(loopS, outroS ?? blendOut), flag, false);
            if (outroS != null)
            {
                var t = AnimUtil.Transition(outroS, blendOut);
                t.hasExitTime = true;
                t.exitTime = 1;
            }
            u.When(AnimUtil.Transition(blendOut, idle), flag, false);
            // Turning the flag on again during BlendOut starts over.
            u.When(AnimUtil.Transition(blendOut, prepare), flag, true);
        }

        static void Body(AnimatorState s, VRC_AnimatorTrackingControl.TrackingType t)
        {
            var tc = s.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            tc.trackingHead = tc.trackingLeftHand = tc.trackingRightHand = tc.trackingHip = t;
            tc.trackingLeftFoot = tc.trackingRightFoot = tc.trackingLeftFingers = tc.trackingRightFingers = t;
            tc.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            tc.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.NoChange;
        }
    }
}
