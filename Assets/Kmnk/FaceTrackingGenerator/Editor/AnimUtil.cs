using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Small helpers to build states, transitions, BlendTrees and clips inside one controller asset.
    // Every created object is stored in the controller asset; StableIds gives them stable file IDs later.
    sealed class AnimUtil
    {
        public readonly AnimatorController Controller;
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

        public AnimUtil(AnimatorController controller)
        {
            Controller = controller;
        }

        public void EnsureParameter(string name, AnimatorControllerParameterType type, float def = 0)
        {
            var existing = Controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing != null) return;
            var p = new AnimatorControllerParameter { name = name, type = type };
            switch (type)
            {
                case AnimatorControllerParameterType.Float: p.defaultFloat = def; break;
                case AnimatorControllerParameterType.Int: p.defaultInt = Mathf.RoundToInt(def); break;
                case AnimatorControllerParameterType.Bool: p.defaultBool = def != 0; break;
            }
            Controller.AddParameter(p);
        }

        public AnimatorControllerParameterType TypeOf(string name)
        {
            var p = Controller.parameters.FirstOrDefault(x => x.name == name);
            if (p == null) throw new System.InvalidOperationException("parameter not found: " + name);
            return p.type;
        }

        public AnimatorControllerLayer NewLayer(string name)
        {
            var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, Controller);
            var layer = new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = sm, blendingMode = AnimatorLayerBlendingMode.Override };
            Controller.AddLayer(layer);
            return layer;
        }

        public static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.writeDefaultValues = true;
            s.motion = motion;
            return s;
        }

        public static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration = 0)
        {
            var t = from.AddTransition(to);
            Configure(t, duration);
            return t;
        }

        public static AnimatorStateTransition AnyTransition(AnimatorStateMachine sm, AnimatorState to, float duration = 0)
        {
            var t = sm.AddAnyStateTransition(to);
            Configure(t, duration);
            t.canTransitionToSelf = false;
            return t;
        }

        static void Configure(AnimatorStateTransition t, float duration)
        {
            t.hasExitTime = false;
            t.exitTime = 0;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.offset = 0;
            t.interruptionSource = TransitionInterruptionSource.None;
        }

        // "param is true/false" for a parameter of any type: Bool If/IfNot, Float > 0.5 / < 0.5, Int != 0 / == 0.
        public void When(AnimatorStateTransition t, string param, bool value)
        {
            switch (TypeOf(param))
            {
                case AnimatorControllerParameterType.Float:
                    t.AddCondition(value ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, param);
                    break;
                case AnimatorControllerParameterType.Int:
                    t.AddCondition(value ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.Equals, 0, param);
                    break;
                default:
                    t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
                    break;
            }
        }

        // A clip that sets Animator parameters (AAP) to constant values.
        public AnimationClip ParamClip(string name, IEnumerable<(string param, float value)> values, float length = 0)
        {
            var clip = NewClip(name);
            foreach (var (param, value) in values)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), param), Constant(value, length));
            return clip;
        }

        // A clip that sets blendshapes to constant values.
        public AnimationClip ShapeClip(string name, IEnumerable<(string mesh, string shape, float value)> values)
        {
            var clip = NewClip(name);
            foreach (var (mesh, shape, value) in values)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(mesh, typeof(SkinnedMeshRenderer), "blendShape." + shape), Constant(value, 0));
            return clip;
        }

        public AnimationClip EmptyClip(string name) => NewClip(name);

        // A clip that sets a parameter from 0 at time 0 to `length` at time `length` (linear).
        public AnimationClip RampClip(string name, string param, float length, bool loop = false, params (string param, float value)[] constants)
        {
            var clip = NewClip(name);
            if (loop)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            var curve = AnimationCurve.Linear(0, 0, length, length);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), param), curve);
            foreach (var (p, v) in constants)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), p), Constant(v, length));
            return clip;
        }

        AnimationClip NewClip(string name)
        {
            if (clips.ContainsKey(name)) throw new System.InvalidOperationException("duplicate clip name: " + name);
            var clip = new AnimationClip { name = name };
            AssetDatabase.AddObjectToAsset(clip, Controller);
            clips[name] = clip;
            return clip;
        }

        static AnimationCurve Constant(float value, float length)
        {
            var keys = length > 0 ? new[] { new Keyframe(0, value), new Keyframe(length, value) } : new[] { new Keyframe(0, value) };
            return new AnimationCurve(keys);
        }

        public BlendTree NewTree(string name, BlendTreeType type, string param = null, string paramY = null)
        {
            var bt = new BlendTree { name = name, blendType = type, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            if (param != null) bt.blendParameter = param;
            if (paramY != null) bt.blendParameterY = paramY;
            AssetDatabase.AddObjectToAsset(bt, Controller);
            return bt;
        }

        public static void AddDirectChild(BlendTree direct, Motion motion, string weightParam)
        {
            direct.AddChild(motion);
            var children = direct.children;
            children[children.Length - 1].directBlendParameter = weightParam;
            direct.children = children;
        }
    }
}
