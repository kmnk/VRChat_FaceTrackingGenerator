using System;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Writes the inputs into the Face Tracking section of an Avatar Descriptor (R2, R6, R7): turns on Face Tracking
    // and Use All Tracking Keys, and adds one Forward to Animator behavior with a Custom expression per input. The
    // avatar's own Face Tracking settings are kept; its behaviors with the same output parameter are replaced.
    public static class NativeFtWriter
    {
        // Forwarded Parameters up to this count keep the Excellent performance rank.
        public const int ExcellentForwarded = 10;

        public static List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> Behaviors(TableModel m) =>
            m.Inputs.Where(i => !i.IsDerived).Select(i => new VRCAvatarDescriptor.FaceTrackingParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator,
                outputParameterName = i.Parameter,
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                customExpression = i.Expression,
                inputTrackingKeys = Array.Empty<int>(),
                inputTrackingKeyNames = Array.Empty<string>(),
                inputInverted = Array.Empty<bool>(),
            }).ToList();

        // Returns lines for the generation log.
        public static List<string> Apply(VRCAvatarDescriptor d, List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> behaviors)
        {
            var log = new List<string>();
            d.enableFaceTracking = true;
            d.useAllTrackingKeys = true;
            var ours = new HashSet<string>(behaviors.Select(b => b.outputParameterName));
            var list = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Where(b => b != null).ToList();
            foreach (var b in list.Where(b => ours.Contains(b.outputParameterName)).ToList())
            {
                list.Remove(b);
                log.Add("face tracking: replaced the avatar's behavior for " + b.outputParameterName);
            }
            var own = list.Count;
            list.AddRange(behaviors);
            d.faceTrackingParameterBehaviors = list;
            log.Add($"face tracking: on, Use All Tracking Keys on, {behaviors.Count} Forward to Animator behaviors added ({own} of the avatar's own kept)");

            var forwarded = list.Count(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator);
            if (forwarded > ExcellentForwarded)
                log.Add($"notice: {forwarded} Forward to Animator behaviors: over {ExcellentForwarded}, the avatar is no longer Excellent in Forwarded Parameters");
            foreach (var w in FaceTrackingExpressionValidator.CollectWarnings(d))
                log.Add("notice: SDK: " + w.Replace("\n", " "));
            return log;
        }

        // The SDK's warnings for the Face Tracking section (empty when it is fine).
        public static List<string> Check(VRCAvatarDescriptor d) => FaceTrackingExpressionValidator.CollectWarnings(d);
    }
}
