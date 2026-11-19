using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // States with "stopVisemes": while such a state's fixed amounts show (FT/Active and its FT/Sw/*), the voice's visemes
    // do not move the mouth. The visemes go through Blendshapes Sets (Lip Sync "Viseme Blend Shape Face Tracking"), and
    // the FT/Visemes layer sets those sets' weights to 0 (a set with weight 0 maps nothing) and back.
    //   - Lip Sync Viseme Blend Shape: the mode is switched, and a set of ours (FTG/visemes) gets the avatar's own viseme
    //     blendshapes as they are, with weight 1 also when the Animator does not run.
    //   - Lip Sync Viseme Blend Shape Face Tracking: the avatar's own sets with viseme rows are the ones stopped.
    //   - Other modes, or an own set with viseme rows that cannot be stopped: stopVisemes is ignored, with a notice,
    //     and the state stays as without it (also its fixed amounts in FT/Face).
    // Without stopVisemes states nothing here changes the output.
    public static class VisemeStop
    {
        public const string SetName = NativeSets.Prefix + "visemes";
        public const string LayerName = "FT/Visemes";

        public sealed class Plan
        {
            public List<(StateMachineDef sm, StateDef st)> States = new List<(StateMachineDef, StateDef)>();
            public bool SwitchMode;  // Viseme Blend Shape -> Viseme Blend Shape Face Tracking, with our set
            public List<(string name, float weight)> Sets = new List<(string, float)>();  // the sets FT/Visemes drives and their weight while not stopped
            public float LerpSpeed;
            public bool Any => States.Count > 0 && Sets.Count > 0;
        }

        public static List<(StateMachineDef sm, StateDef st)> StopStates(TableModel m) =>
            m.StateMachines.SelectMany(sm => sm.States.Where(st => st.StopVisemesAny).Select(st => (sm, st))).ToList();

        static bool HasNames(string[] names) => names != null && names.Any(n => !string.IsNullOrEmpty(n));

        // From the original avatar's descriptor (the variant and the build-time copy have the same values).
        public static Plan Build(TableModel m, VRCAvatarDescriptor d, List<string> log)
        {
            var plan = new Plan { States = StopStates(m) };
            if (plan.States.Count == 0) return plan;
            var names = string.Join(", ", plan.States.Select(x => x.sm.Name + "/" + x.st.Name));
            switch (d.lipSync)
            {
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape:
                    if (d.VisemeSkinnedMesh == null || !HasNames(d.VisemeBlendShapes))
                    {
                        log.Add($"notice: stopVisemes ({names}) ignored: Lip Sync has no Face Mesh or no viseme blendshapes");
                        return plan;
                    }
                    plan.SwitchMode = true;
                    plan.Sets.Add((SetName, 1f));
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking:
                    break;
                default:
                    log.Add($"notice: stopVisemes ({names}) ignored: Lip Sync is {d.lipSync} (only Viseme Blend Shape and Viseme Blend Shape Face Tracking can stop visemes)");
                    return plan;
            }

            // In both modes the avatar's own sets with viseme rows move the mouth (with Viseme Blend Shape, once the mode is
            // switched), and the strongest viseme wins: every one of them must be stopped, or none is (the state then
            // stays as without stopVisemes). NativeFtWriter.Apply empties the tracking rows of the avatar's own sets only
            // when it writes ours; a set that keeps tracking rows cannot be stopped, as weight 0 would stop its tracking.
            var oursWritten = NativeSets.Build(m).Count > 0;
            var emptied = NativeFtWriter.WritesSets(oursWritten, d, m.OverwriteFaceTracking);
            var why = oursWritten ? "overwriteFaceTracking is false" : "the table writes no Blendshapes Sets";
            foreach (var s in d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
            {
                if (s == null || string.IsNullOrEmpty(s.setName) || s.setName.StartsWith(NativeSets.Prefix) || !HasNames(s.visemeBlendshapeNames)) continue;
                if (!emptied && HasNames(s.faceTrackingBlendshapeNames))
                {
                    log.Add($"notice: stopVisemes ({names}) ignored: the avatar's Blendshapes Set '{s.setName}' has viseme rows and keeps its tracking rows ({why})");
                    plan.Sets.Clear();
                    plan.SwitchMode = false;
                    return plan;
                }
                plan.Sets.Add((s.setName, s.setWeight));
            }
            if (plan.Sets.Count == 0)
            {
                log.Add($"notice: stopVisemes ({names}) ignored: no Blendshapes Set of the avatar has viseme rows");
                return plan;
            }
            plan.LerpSpeed = NativeSets.LerpSpeed(plan.States.Select(x => x.sm).Distinct());
            log.Add($"visemes: stopped in {names} by {LayerName} (sets: {string.Join(", ", plan.Sets.Select(x => x.name))})");
            return plan;
        }

        // Writes the mode and our set into a descriptor (the Prefab Variant or the build-time copy).
        public static void Apply(VRCAvatarDescriptor d, Plan plan, List<string> log)
        {
            if (plan == null || !plan.Any) return;
            // The plan comes from the table's prefab; the build-time copy may differ (overrides in the scene, other
            // plugins). Write nothing then: FT/Visemes and FT/Face are already built, so the visemes are not stopped.
            // "notice:" so that NdmfPlugin prints it.
            var differs = Differs(d, plan);
            if (differs != null)
            {
                log.Add($"notice: visemes: stopVisemes not written: the avatar differs from the table's prefab ({differs})");
                return;
            }
            if (!plan.SwitchMode) return;
            var count = (int)VRC_AvatarDescriptor.Viseme.Count;
            // VisemeBlendShapes and a set's viseme rows are both in VRC_AvatarDescriptor.Viseme order.
            var names = new string[count];
            for (int i = 0; i < count && i < d.VisemeBlendShapes.Length; i++) names[i] = d.VisemeBlendShapes[i];
            var n = NativeKeys.Count;
            var sets = (d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>()).Where(s => s != null && s.setName != SetName).ToList();
            sets.Add(new VRCAvatarDescriptor.FaceTrackingBlendshapesSet
            {
                setName = SetName,
                setWeight = 1,
                faceTrackingBlendshapeNames = new string[n],
                faceTrackingBlendshapeMultipliers = Enumerable.Repeat(1f, n).ToArray(),
                visemeBlendshapeNames = names,
                visemeBlendshapeMultipliers = Enumerable.Repeat(1f, count).ToArray(),
            });
            d.faceTrackingBlendshapesSetList = sets;
            var meshes = (d.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>()).Where(x => x != null).ToList();
            if (!meshes.Contains(d.VisemeSkinnedMesh)) meshes.Add(d.VisemeSkinnedMesh);
            d.faceTrackingMeshes = meshes;
            d.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
            log.Add($"visemes: Lip Sync Viseme Blend Shape Face Tracking, Blendshapes Set '{SetName}' with the avatar's {names.Count(x => !string.IsNullOrEmpty(x))} viseme blendshapes of '{d.VisemeSkinnedMesh.name}'");

            // Another Face Tracking Mesh with a blendshape of the same name moves too, which Viseme Blend Shape did not do.
            var shapes = new HashSet<string>(names.Where(x => !string.IsNullOrEmpty(x)));
            foreach (var mesh in meshes.Where(x => x != d.VisemeSkinnedMesh && x.sharedMesh != null))
            {
                var same = Enumerable.Range(0, mesh.sharedMesh.blendShapeCount).Select(i => mesh.sharedMesh.GetBlendShapeName(i)).Where(shapes.Contains).ToList();
                if (same.Count > 0) log.Add($"notice: visemes: Face Tracking Mesh '{mesh.name}' also has {same.Count} of the viseme blendshapes; the visemes move it too");
            }
        }

        static string Differs(VRCAvatarDescriptor d, Plan plan)
        {
            if (plan.SwitchMode)
            {
                if (d.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape) return "Lip Sync " + d.lipSync;
                if (d.VisemeSkinnedMesh == null || !HasNames(d.VisemeBlendShapes)) return "no Face Mesh or viseme blendshapes";
            }
            else if (d.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking) return "Lip Sync " + d.lipSync;
            var planned = new HashSet<string>(plan.Sets.Select(x => x.name));
            foreach (var s in d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
            {
                if (s == null || string.IsNullOrEmpty(s.setName) || s.setName.StartsWith(NativeSets.Prefix) || !HasNames(s.visemeBlendshapeNames)) continue;
                if (!planned.Contains(s.setName)) return $"Blendshapes Set '{s.setName}' with viseme rows";
                // Runs after NativeFtWriter has written the sets: tracking rows left now are not emptied, and weight 0
                // would stop them too.
                if (HasNames(s.faceTrackingBlendshapeNames)) return $"Blendshapes Set '{s.setName}' keeps its tracking rows";
            }
            return null;
        }

        // FT/Visemes: On (the sets' weights while not stopped) and Stop (0). Stop while FT/Active and any stopVisemes
        // state's FT/Sw/*; back when FT/Active is off or every one of those FT/Sw/* is off. Voice is not read: with the
        // weight at 0 the face does not change while silent, and entering mid-speech works the same way.
        internal static void BuildLayer(AnimUtil u, Plan plan, List<AnimatorControllerLayer> layers)
        {
            if (plan == null || !plan.Any) return;
            var layer = u.NewLayer(LayerName);
            layers.Add(layer);
            var sm = layer.stateMachine;
            var empty = u.EmptyClip(LayerName + "/Empty");
            var on = AnimUtil.State(sm, "On", empty, new Vector3(300, 0));
            sm.defaultState = on;
            Weights(on, plan.Sets, plan.LerpSpeed);
            // With a state that stops the visemes on one device only, one Stop per device (Stop/vr, Stop/desktop), each
            // entered and left on its device only (VRMode): Animator conditions are all "and", so one Stop could not be
            // left when every state of the current device is off.
            if (!plan.States.Any(x => x.st.StopVisemesByDevice))
            {
                StopState(sm, on, "Stop", new Vector3(600, 0), plan, plan.States, null);
                return;
            }
            var y = 0;
            foreach (var desktop in new[] { false, true })
            {
                var states = plan.States.Where(x => x.st.StopVisemes(desktop)).ToList();
                if (states.Count == 0) continue;
                StopState(sm, on, "Stop/" + Device.Of(desktop), new Vector3(600, y), plan, states, desktop);
                y += 100;
            }
        }

        static void StopState(AnimatorStateMachine sm, AnimatorState on, string name, Vector3 position, Plan plan,
            List<(StateMachineDef sm, StateDef st)> states, bool? desktop)
        {
            var stop = AnimUtil.State(sm, name, on.motion, position);
            Weights(stop, plan.Sets.Select(x => (x.name, 0f)).ToList(), plan.LerpSpeed);
            void OnDevice(AnimatorStateTransition t)
            {
                if (desktop != null) t.AddCondition(desktop.Value ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, 0.5f, NativeSets.SideParameter);
            }
            foreach (var (smd, st) in states)
            {
                var t = AnimUtil.Transition(on, stop);
                t.AddCondition(AnimatorConditionMode.Greater, 0.5f, FtBuilder.Active);
                OnDevice(t);
                t.AddCondition(AnimatorConditionMode.Greater, 0.5f, FtBuilder.SwitchParam(smd.Name, st.Name));
            }
            AnimUtil.Transition(stop, on).AddCondition(AnimatorConditionMode.Less, 0.5f, FtBuilder.Active);
            if (desktop != null)
                AnimUtil.Transition(stop, on).AddCondition(desktop.Value ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, NativeSets.SideParameter);
            var none = AnimUtil.Transition(stop, on);
            foreach (var (smd, st) in states)
                none.AddCondition(AnimatorConditionMode.Less, 0.5f, FtBuilder.SwitchParam(smd.Name, st.Name));
        }

        static void Weights(AnimatorState s, List<(string name, float weight)> sets, float lerp)
        {
            for (int k = 0; k < sets.Count; k += FtBuilder.MaxSetsPerBehavior)
            {
                var b = s.AddStateMachineBehaviour<VRC.SDK3.Avatars.Components.VRCAnimatorFaceTrackingBlendshapesSet>();
                b.blendSets = sets.Skip(k).Take(FtBuilder.MaxSetsPerBehavior).Select(x => new VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet
                {
                    blendshapesSetName = x.name,
                    type = VRC_AnimatorFaceTrackingBlendshapesSet.SourceType.Value,
                    weight = x.weight,
                    lerpSpeed = lerp,
                }).ToList();
            }
        }
    }
}
