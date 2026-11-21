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
            // Forward to Animator for the inputs the Animator reads
            public List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> Behaviors = new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
            // Apply to Blendshape for the blendshapes with a curve: one per set and blendshape, weighted by the set
            public List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> CurveBehaviors = new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
            public List<NativeSets.SetDef> Sets = new List<NativeSets.SetDef>();
            public bool Overwrite;
            public string EyeGazeMode;
            public bool Eyelids;   // a neutral row uses EyeClosedLeft or EyeClosedRight
            public VisemeStop.Plan Visemes;  // set by Generator.Generate, which reads the avatar's Lip Sync
        }

        public static Plan Build(TableModel m)
        {
            var sets = NativeSets.Build(m);
            return new Plan
            {
                Behaviors = Behaviors(m),
                CurveBehaviors = CurveBehaviors(sets),
                Sets = sets,
                Overwrite = m.OverwriteFaceTracking,
                EyeGazeMode = m.EyeGazeMode,
                Eyelids = m.Neutral.Values.SelectMany(r => r).Any(r => r.Key == "EyeClosedLeft" || r.Key == "EyeClosedRight"),
            };
        }

        public static List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> CurveBehaviors(List<NativeSets.SetDef> sets) =>
            sets.SelectMany(set => set.Curves.Select(ct => new VRCAvatarDescriptor.FaceTrackingParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape,
                targetBlendshapeName = ct.Shape,
                weightBlendSetName = set.Name,
                outputParameterName = "",
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                customExpression = ct.Expression,
                inputTrackingKeys = Array.Empty<int>(),
                inputTrackingKeyNames = Array.Empty<string>(),
                inputInverted = Array.Empty<bool>(),
            })).ToList();

        // A Parameter Behavior the generator wrote for a curve: it follows one of the generator's sets.
        static bool IsOurBehavior(VRCAvatarDescriptor.FaceTrackingParameterBehavior b) =>
            MovesBlendshapes(b) && b.weightBlendSetName != null && b.weightBlendSetName.StartsWith(NativeSets.Prefix);

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
            var own = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Count(b => MovesBlendshapes(b) && !IsOurBehavior(b));
            if (own > 0) found.Add($"{own} Parameter Behaviors that move blendshapes");
            if (d.enableEyeLook && d.customEyeLookSettings.eyeGazeMode != VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode.BoneRotation)
                found.Add("Eye Gaze Mode " + d.customEyeLookSettings.eyeGazeMode);
            if (d.enableEyeLook && d.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.FaceTracking)
                found.Add("Eyelids Type FaceTracking");
            return found;
        }

        // Whether Apply writes the generator's Blendshapes Sets (and empties the tracking rows of the avatar's own sets):
        // the table has sets, and the avatar has no own Face Tracking values or overwriteFaceTracking is on.
        public static bool WritesSets(bool hasSets, VRCAvatarDescriptor d, bool overwrite) => hasSets && (OwnValues(d).Count == 0 || overwrite);

        // Returns lines for the generation log.
        public static List<string> Apply(VRCAvatarDescriptor d, Plan plan)
        {
            var log = new List<string>();
            d.enableFaceTracking = true;
            d.useAllTrackingKeys = true;

            // Forward to Animator: ours replace the avatar's behaviors with the same output parameter.
            var behaviors = plan.Behaviors;
            var ours = new HashSet<string>(behaviors.Select(b => b.outputParameterName));
            // The curves' behaviors of an earlier generation go first (they are written again below).
            var list = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Where(b => b != null && !IsOurBehavior(b)).ToList();
            foreach (var b in list.Where(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator && ours.Contains(b.outputParameterName)).ToList())
            {
                list.Remove(b);
                log.Add("face tracking: replaced the avatar's behavior for " + b.outputParameterName);
            }

            var curvesAdded = 0;
            if (plan.Sets.Count > 0)
            {
                var own = OwnValues(d);
                if (!WritesSets(true, d, plan.Overwrite))
                {
                    log.Add("face tracking: kept the avatar's own Face Tracking settings (overwriteFaceTracking is false): " + string.Join(", ", own) + "; the generator's Blendshapes Sets are not written");
                    if (plan.CurveBehaviors.Count > 0) log.Add($"face tracking: the {plan.CurveBehaviors.Count} Parameter Behaviors of the curves are not written either (they follow the generator's Blendshapes Sets)");
                }
                else
                {
                    if (own.Count > 0) log.Add("face tracking: replaced the avatar's own Face Tracking settings (overwriteFaceTracking): " + string.Join(", ", own));
                    var removed = list.RemoveAll(MovesBlendshapes);
                    if (removed > 0) log.Add($"face tracking: removed {removed} of the avatar's Parameter Behaviors that move blendshapes");
                    WriteSets(d, plan.Sets, log);
                    WriteEyes(d, plan, log);
                    list.AddRange(plan.CurveBehaviors);
                    curvesAdded = plan.CurveBehaviors.Count;
                    if (plan.CurveBehaviors.Count > 0) log.Add($"face tracking: {plan.CurveBehaviors.Count} Parameter Behaviors for curves (Apply to Blendshape, Custom) added");
                }
            }

            VisemeStop.Apply(d, plan.Visemes, log);

            var kept = list.Count - curvesAdded;
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
                void AddMesh(string path)
                {
                    var tr = avatarRoot.Find(path);
                    var smr = tr != null ? tr.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr != null && !meshes.Contains(smr)) meshes.Add(smr);
                }
                foreach (var r in def.Rows)
                {
                    var i = NativeKeys.Index(r.Key);
                    names[i] = r.Shape;
                    mults[i] = r.Multiplier;
                    AddMesh(r.Mesh);
                }
                // Apply to Blendshape moves the blendshape on the Face Tracking Meshes only.
                foreach (var ct in def.Curves) foreach (var mesh in ct.Meshes) AddMesh(mesh);
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
