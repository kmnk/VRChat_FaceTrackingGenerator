using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Writes the Face Tracking section of the generated Avatar Descriptor (design.md "Face Tracking の節への書き込み"):
    // Face Tracking and Use All Tracking Keys on, Forward to Animator for the inputs the Animator reads, and for
    // tracking parts the Blendshapes Sets (NativeSets), Face Tracking Meshes and Eye Look. The avatar's own values
    // (Blendshapes Sets' tracking rows, Parameter Behaviors that move blendshapes, Eye Look on Face Tracking) are
    // replaced only with overwriteFaceTracking; viseme rows and the avatar's Forward to Animator are kept.
    public static class NativeFtWriter
    {
        // Forwarded Parameters up to this count keep the Excellent performance rank.
        public const int ExcellentForwarded = 10;

        // What the generator writes, built from the model in Generator.Generate.
        public sealed class Plan
        {
            public List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> Behaviors = new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
            public List<NativeSets.SetDef> Sets = new List<NativeSets.SetDef>();
            public bool Overwrite;
            public string EyeGazeMode;
            public bool Eyelids;   // a neutral row uses EyeClosedLeft or EyeClosedRight
        }

        public static Plan Build(TableModel m) => new Plan
        {
            Behaviors = Behaviors(m),
            Sets = NativeSets.Build(m),
            Overwrite = m.OverwriteFaceTracking,
            EyeGazeMode = m.EyeGazeMode,
            Eyelids = m.Neutral.Values.SelectMany(r => r).Any(r => r.Key == "EyeClosedLeft" || r.Key == "EyeClosedRight"),
        };

        // The inputs the generated Animator reads: the conditions of the state machines' transitions. Tracking inputs
        // and inputs nothing reads are not forwarded.
        public static HashSet<string> AnimatorInputs(TableModel m)
        {
            var used = new HashSet<string>();
            foreach (var sm in m.StateMachines)
                foreach (var tr in sm.Transitions)
                    foreach (var c in tr.When) used.Add(c.Input);
            return used;
        }

        public static List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> Behaviors(TableModel m)
        {
            var used = AnimatorInputs(m);
            return m.Inputs.Where(i => !i.IsTrack && used.Contains(i.Name)).Select(i => new VRCAvatarDescriptor.FaceTrackingParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator,
                outputParameterName = i.Parameter,
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                customExpression = i.Expression,
                inputTrackingKeys = Array.Empty<int>(),
                inputTrackingKeyNames = Array.Empty<string>(),
                inputInverted = Array.Empty<bool>(),
            }).ToList();
        }

        static bool MovesBlendshapes(VRCAvatarDescriptor.FaceTrackingParameterBehavior b) =>
            b != null && b.targetMode != VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator;

        static bool IsOurs(VRCAvatarDescriptor.FaceTrackingBlendshapesSet s) => s != null && s.setName != null && s.setName.StartsWith(NativeSets.Prefix);

        static bool HasRows(string[] names) => names != null && names.Any(n => !string.IsNullOrEmpty(n));

        // The avatar's own Face Tracking values that writing tracking parts would replace, for the log.
        public static List<string> OwnValues(VRCAvatarDescriptor d)
        {
            var found = new List<string>();
            foreach (var s in d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
                if (!IsOurs(s) && HasRows(s.faceTrackingBlendshapeNames)) found.Add($"Blendshapes Set '{s.setName}'");
            var own = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Count(MovesBlendshapes);
            if (own > 0) found.Add($"{own} Parameter Behaviors that move blendshapes");
            if (d.enableEyeLook && d.customEyeLookSettings.eyeGazeMode != VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode.BoneRotation)
                found.Add("Eye Gaze Mode " + d.customEyeLookSettings.eyeGazeMode);
            if (d.enableEyeLook && d.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.FaceTracking)
                found.Add("Eyelids Type FaceTracking");
            return found;
        }

        // Returns lines for the generation log.
        public static List<string> Apply(VRCAvatarDescriptor d, Plan plan)
        {
            var log = new List<string>();
            d.enableFaceTracking = true;
            d.useAllTrackingKeys = true;

            // Forward to Animator: ours replace the avatar's behaviors with the same output parameter.
            var behaviors = plan.Behaviors;
            var ours = new HashSet<string>(behaviors.Select(b => b.outputParameterName));
            var list = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Where(b => b != null).ToList();
            foreach (var b in list.Where(b => ours.Contains(b.outputParameterName)).ToList())
            {
                list.Remove(b);
                log.Add("face tracking: replaced the avatar's behavior for " + b.outputParameterName);
            }

            if (plan.Sets.Count > 0)
            {
                var own = OwnValues(d);
                if (own.Count > 0 && !plan.Overwrite)
                {
                    log.Add("face tracking: kept the avatar's own Face Tracking settings (overwriteFaceTracking is false): " + string.Join(", ", own) + "; the generator's Blendshapes Sets are not written");
                }
                else
                {
                    if (own.Count > 0) log.Add("face tracking: replaced the avatar's own Face Tracking settings (overwriteFaceTracking): " + string.Join(", ", own));
                    var removed = list.RemoveAll(MovesBlendshapes);
                    if (removed > 0) log.Add($"face tracking: removed {removed} of the avatar's Parameter Behaviors that move blendshapes");
                    WriteSets(d, plan.Sets, log);
                    WriteEyes(d, plan, log);
                }
            }

            var kept = list.Count;
            list.AddRange(behaviors);
            d.faceTrackingParameterBehaviors = list;
            log.Add($"face tracking: on, Use All Tracking Keys on, {behaviors.Count} Forward to Animator behaviors added ({kept} of the avatar's own kept)");

            var forwarded = list.Count(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator);
            if (forwarded > ExcellentForwarded)
                log.Add($"notice: {forwarded} Forward to Animator behaviors: over {ExcellentForwarded}, the avatar is no longer Excellent in Forwarded Parameters");
            foreach (var w in FaceTrackingExpressionValidator.CollectWarnings(d))
                log.Add("notice: SDK: " + w.Replace("\n", " "));
            return log;
        }

        static void WriteSets(VRCAvatarDescriptor d, List<NativeSets.SetDef> sets, List<string> log)
        {
            var n = NativeKeys.Count;
            var all = (d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>()).Where(s => s != null && !IsOurs(s)).ToList();
            // The avatar's own sets keep their viseme rows; their tracking rows are emptied.
            foreach (var s in all)
            {
                if (HasRows(s.faceTrackingBlendshapeNames)) log.Add($"face tracking: emptied the tracking rows of the avatar's Blendshapes Set '{s.setName}' (viseme rows kept)");
                s.faceTrackingBlendshapeNames = new string[n];
                s.faceTrackingBlendshapeMultipliers = Enumerable.Repeat(1f, n).ToArray();
            }
            var avatarRoot = d.transform;
            var meshes = (d.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>()).Where(x => x != null).ToList();
            foreach (var def in sets)
            {
                var names = new string[n];
                var mults = Enumerable.Repeat(1f, n).ToArray();
                foreach (var r in def.Rows)
                {
                    var i = NativeKeys.Index(r.Key);
                    names[i] = r.Shape;
                    mults[i] = r.Multiplier;
                    var tr = avatarRoot.Find(r.Mesh);
                    var smr = tr != null ? tr.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr != null && !meshes.Contains(smr)) meshes.Add(smr);
                }
                all.Add(new VRCAvatarDescriptor.FaceTrackingBlendshapesSet
                {
                    setName = def.Name,
                    setWeight = 0,
                    faceTrackingBlendshapeNames = names,
                    faceTrackingBlendshapeMultipliers = mults,
                    visemeBlendshapeNames = new string[(int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count],
                    visemeBlendshapeMultipliers = Enumerable.Repeat(1f, (int)VRC.SDKBase.VRC_AvatarDescriptor.Viseme.Count).ToArray(),
                });
            }
            d.faceTrackingBlendshapesSetList = all;
            d.faceTrackingMeshes = meshes;
            log.Add($"face tracking: {sets.Count} Blendshapes Sets written ({sets.Sum(s => s.Rows.Count)} rows), {meshes.Count} Face Tracking Meshes");
        }

        static void WriteEyes(VRCAvatarDescriptor d, Plan plan, List<string> log)
        {
            var eyes = d.customEyeLookSettings;
            eyes.eyeGazeMode = (VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode)Enum.Parse(typeof(VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode), plan.EyeGazeMode);
            if (plan.Eyelids) eyes.eyelidType = VRCAvatarDescriptor.EyelidType.FaceTracking;
            d.customEyeLookSettings = eyes;
            d.enableEyeLook = true;
            log.Add($"eye look: Eye Gaze Mode {eyes.eyeGazeMode}" + (plan.Eyelids ? ", Eyelids Type FaceTracking" : ""));
        }

        // The SDK's warnings for the Face Tracking section (empty when it is fine).
        public static List<string> Check(VRCAvatarDescriptor d) => FaceTrackingExpressionValidator.CollectWarnings(d);
    }
}
