using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // What the Face Tracking section costs against VRChat's limits (design.md "上限で止める"): Parameter Behaviors (128),
    // Custom expression instructions over the avatar (2048, counted before VRChat simplifies constants, so a little
    // high) and Face Tracking Operations with the performance rank (the SDK's own count, FaceTrackingPerformanceScanner).
    public static class FtCost
    {
        public const int MaxBehaviors = 128, MaxInstructions = 2048, ExcellentOperations = 2000;
        static readonly (int max, string rank)[] Ranks = { (2000, "Excellent"), (4000, "Good"), (8000, "Medium"), (16000, "Poor") };

        public sealed class Result
        {
            public int Behaviors, Instructions, Operations;
            public string Rank => Ranks.Where(r => Operations <= r.max).Select(r => r.rank).FirstOrDefault() ?? "Very Poor";
            public bool OverBehaviors => Behaviors > MaxBehaviors;
            public bool OverInstructions => Instructions > MaxInstructions;
            public bool OverExcellent => Operations > ExcellentOperations;
        }

        public static Result Count(VRCAvatarDescriptor d)
        {
            var list = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>()).Where(b => b != null).ToList();
            return new Result
            {
                Behaviors = list.Count,
                Instructions = list.Where(b => b.combineMode == VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom).Sum(b => Instructions(b.customExpression)),
                Operations = FaceTrackingPerformanceScanner.CountOperations(d),
            };
        }

        static int Instructions(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return 0;
            try { return FtExpression.Count(expression).Instructions; }
            catch (FtExpression.ParseException) { return 0; }
        }

        // What writing the plan into the prefab's Avatar Descriptor would cost: written into a copy that is destroyed.
        public static Result Predict(GameObject prefab, NativeFtWriter.Plan plan)
        {
            var copy = UnityEngine.Object.Instantiate(prefab);
            copy.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var d = copy.GetComponent<VRCAvatarDescriptor>();
                NativeFtWriter.Apply(d, plan);
                return Count(d);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        // The generation log's lines: the counts, and the rows a curve's expression cannot follow within CurveFit.Tolerance.
        public static List<string> Lines(Result r, List<NativeSets.SetDef> sets)
        {
            var log = new List<string>
            {
                $"face tracking cost: Parameter Behaviors {r.Behaviors} / {MaxBehaviors}, expression instructions {r.Instructions} / {MaxInstructions}, Face Tracking Operations {r.Operations} / {ExcellentOperations} ({r.Rank})",
            };
            log.AddRange(Rough(sets).Select(x => $"curve: {x.set} {x.key} ({x.part}): the expression differs from the curve by up to {x.err.ToString("0.00", CultureInfo.InvariantCulture)} (more than {CurveFit.Tolerance.ToString(CultureInfo.InvariantCulture)}; one expression of a blendshape is full)"));
            if (r.OverExcellent) log.Add($"notice: {r.Operations} Face Tracking Operations: over {ExcellentOperations}, the avatar is {r.Rank} (no longer Excellent) in Face Tracking Operations");
            return log;
        }

        // The rows whose polyline is farther than the tolerance from the curve: (set name, key, part, difference).
        public static IEnumerable<(string set, string key, string part, float err)> Rough(IEnumerable<NativeSets.SetDef> sets) =>
            sets.SelectMany(s => s.Curves.SelectMany(ct => ct.Lines.Where(l => l.MaxError > CurveFit.Tolerance).Select(l => (s.Name, l.Key, l.Part, l.MaxError))));

        // Null when within the limits; else why generating stops, with the sets and blendshapes that take the most.
        public static string Over(Result r, NativeFtWriter.Plan plan)
        {
            if (!r.OverBehaviors && !r.OverInstructions) return null;
            var what = new List<string>();
            if (r.OverBehaviors) what.Add($"{r.Behaviors} Parameter Behaviors (VRChat allows {MaxBehaviors})");
            if (r.OverInstructions) what.Add($"{r.Instructions} expression instructions (VRChat allows {MaxInstructions})");
            var bySet = plan.Sets.Where(s => s.Curves.Count > 0)
                .Select(s => (s.Name, n: s.Curves.Count, i: s.Curves.Sum(c => Instructions(c.Expression))))
                .OrderByDescending(x => x.i).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
            var byShape = plan.Sets.SelectMany(s => s.Curves.Select(c => (set: s.Name, c, i: Instructions(c.Expression))))
                .OrderByDescending(x => x.i).ThenBy(x => x.set, StringComparer.Ordinal).Take(10)
                .Select(x => $"{x.set} {x.c.Shape} ({string.Join(", ", x.c.Lines.Select(l => l.Key))}): {x.i}");
            return $"the Face Tracking section would have {string.Join(" and ", what)}; the curves take " +
                   $"{plan.CurveBehaviors.Count} behaviors. Fewer curves, fewer states overriding regions with curves or the same curve on both devices take fewer. " +
                   $"Per set (behaviors, instructions): {string.Join("; ", bySet.Select(x => $"{x.Name} {x.n}, {x.i}"))}. " +
                   $"Most instructions: {string.Join("; ", byShape)}";
        }
    }
}
