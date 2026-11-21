using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // Checks of the response curves (the task's plan.md). Each entry prints "[FTG] curve-check <item> ok|NG <value>" and
    // exits 0 when every item is ok, 1 otherwise.
    //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.CurveChecks.<Load|Fit|Generated|Eval|Ops|Edit> [-ftTable <avatar.jsonc>]
    // -ftTable defaults to the CharCurve table. Generated and Eval read the Prefab Variant of a Cli.Generate run.
    public static class CurveChecks
    {
        const string DefaultTable = "Assets/Kmnk/FaceTrackingGenerator/Settings/CharCurve/avatar.jsonc";
        static int fails;

        static void Item(string name, bool ok, string value = "")
        {
            Debug.Log($"[FTG] curve-check {name} {(ok ? "ok" : "NG")} {value}".TrimEnd());
            if (!ok) fails++;
        }

        static int Done() { var r = fails == 0 ? 0 : 1; fails = 0; return r; }

        static string Table => CliArgs.Get("-ftTable") ?? DefaultTable;
        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        // ---- curves used by several checks

        static AnimationCurve SCurve() => new CurveKeys { Keys = { new CurveKeys.Key { Time = 0, Value = 0 }, new CurveKeys.Key { Time = 0.5f, Value = 0.4f, InTangent = 1.6f, OutTangent = 1.6f }, new CurveKeys.Key { Time = 1, Value = 0.8f } } }.ToAnimationCurve();

        static AnimationCurve Square()
        {
            var c = new AnimationCurve();
            for (int i = 0; i <= 10; i++) c.AddKey(i / 10f, (i / 10f) * (i / 10f));
            for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            return c;
        }

        static AnimationCurve Step()
        {
            var c = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 1));
            for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Constant); }
            return c;
        }

        static AnimationCurve Weighted()
        {
            var c = new AnimationCurve(new Keyframe(0, 0, 0, 2.5f, 0, 0.6f) { weightedMode = WeightedMode.Out }, new Keyframe(1, 0.9f, 0.1f, 0, 0.2f, 0) { weightedMode = WeightedMode.In });
            return c;
        }

        static float MaxDiff(AnimationCurve a, AnimationCurve b)
        {
            float d = 0;
            for (int i = 0; i <= CurveKeys.Samples; i++) { var x = i / (float)CurveKeys.Samples; d = Math.Max(d, Math.Abs(CurveKeys.Value(a, x) - CurveKeys.Value(b, x))); }
            return d;
        }

        // ---- step 2: the table and the model

        public static void Load() => Cli.Run(() =>
        {
            var exprPath = TableLoader.ExpressionPath(Table);
            var baseExpr = TableLoader.Read(exprPath);
            string Error(Action<JObject> change)
            {
                var e = (JObject)baseExpr.DeepClone();
                change(e);
                try { TableLoader.Load(Table, p => System.IO.Path.GetFullPath(p) == System.IO.Path.GetFullPath(exprPath) ? e : TableLoader.Read(p)); return null; }
                catch (TableLoader.TableException ex) { return ex.Message; }
            }
            JObject Row(JObject e) => (JObject)e["neutral"]["eye"]["EyeClosedLeft"];
            JArray Keys(JObject e) => (JArray)Row(e)["curve"];
            void Expect(string item, Action<JObject> change, string part)
            {
                var m = Error(change);
                Item("load-error-" + item, m != null && m.Contains(part), m ?? "no error");
            }
            Expect("both", e => Row(e)["strength"] = 0.5, "not both");
            Expect("time-range", e => Keys(e)[1]["time"] = 1.5, "must be 0 to 1");
            Expect("value-range", e => Keys(e)[1]["value"] = -0.1, "must be 0 to 1");
            Expect("order", e => { Keys(e)[1]["time"] = 0; }, "must increase");
            Expect("first", e => Keys(e)[0]["time"] = 0.1, "first key's time must be 0");
            Expect("last", e => Keys(e)[2]["time"] = 0.9, "last key's time must be 1");
            Expect("field", e => Keys(e)[1]["foo"] = 1, "a curve key has");
            Expect("mode", e => Keys(e)[1]["leftTangent"] = "Smooth", "must be one of");
            Expect("device", e => Row(e)["curve"] = new JObject { ["pc"] = Keys(e).DeepClone() }, "\"vr\" and \"desktop\" only");

            var m0 = TableLoader.Load(Table);
            TrackRow N(string region, string key) => m0.Neutral[region].First(r => r.Key == key);
            var left = N("eye", "EyeClosedLeft");
            Item("load-curve", left.CurveVr != null && left.CurveDesktop != null && Math.Abs(left.StrengthVr - 0.8f) < 0.001f, $"strength={F(left.StrengthVr)}");
            var right = N("eye", "EyeClosedRight");
            Item("load-straight-as-strength", right.CurveVr == null && Math.Abs(right.StrengthVr - 0.7f) < 0.0005f, $"strength={F(right.StrengthVr)}");
            var jaw = N("jaw", "JawOpen");
            Item("load-per-device", jaw.CurveVr != null && jaw.CurveDesktop != null && !CurveKeys.Same(jaw.CurveVr, jaw.CurveDesktop) && m0.SetsByDevice);
            Item("load-curved-shapes", m0.CurvedShapes().Count > 0, string.Join(",", m0.CurvedShapes().OrderBy(x => x)));

            // Unity curve -> table keys -> Unity curve
            // A steep segment between keys at times that are not round (as dragging in the curve editor leaves them)
            var steep = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.48736f, 0.02f), new Keyframe(0.51264f, 0.98f), new Keyframe(1, 1));
            for (int i = 0; i < steep.length; i++) { AnimationUtility.SetKeyLeftTangentMode(steep, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(steep, i, AnimationUtility.TangentMode.Linear); }
            foreach (var (name, c) in new[] { ("s", SCurve()), ("clampedauto", Square()), ("constant", Step()), ("weighted", Weighted()), ("steep", steep) })
            {
                var json = CurveKeys.From(c).ToJson();
                var back = CurveKeys.Parse(JToken.Parse(json.ToString()), msg => new Exception(msg)).ToAnimationCurve();
                var d = MaxDiff(c, back);
                Item("roundtrip-" + name, d <= 0.001f, $"diff={F(d)} json={json.ToString(Newtonsoft.Json.Formatting.None)}");
            }

            var line = CurveKeys.Line(0.7f);
            Item("straight-line", Math.Abs(CurveKeys.Straight(line) - 0.7f) < 1e-5f);
            var noisy = CurveKeys.Line(0.7f);
            var k0 = noisy.keys[0]; k0.outTangent += 1e-5f; noisy.MoveKey(0, k0);
            Item("straight-noisy", !float.IsNaN(CurveKeys.Straight(noisy)));
            Item("straight-s", float.IsNaN(CurveKeys.Straight(SCurve())));
            return Done();
        });

        // ---- step 3: polylines and expressions

        static readonly Type Validator = typeof(VRCAvatarDescriptor).Assembly.GetType("FaceTrackingExpressionValidator")
            ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("FaceTrackingExpressionValidator")).FirstOrDefault(t => t != null);

        // The SDK's check of one expression: (problems, instruction estimate).
        static (List<string> problems, int instructions) SdkValidate(string expr)
        {
            var lookupType = Validator.GetNestedType("KeyNameLookup", BindingFlags.NonPublic);
            var lookup = Activator.CreateInstance(lookupType, new object[] { true });
            var m = Validator.GetMethod("ValidateExpression", BindingFlags.NonPublic | BindingFlags.Static);
            var args = new object[] { expr, lookup, 0 };
            var problems = (List<string>)m.Invoke(null, args);
            return (problems, (int)args[2]);
        }

        // The SDK's performance estimate of one expression: (instructions, operations).
        static (int instructions, int operations) SdkEstimate(string expr)
        {
            var r = typeof(FaceTrackingPerformanceScanner).GetMethod("EstimateExpressionCost", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { expr, true });
            int Get(string n) => (int)(r.GetType().GetField(n)?.GetValue(r) ?? r.GetType().GetProperty(n)?.GetValue(r) ?? -1);
            return (Get("Instructions"), Get("Operations"));
        }

        static float ExprDiff(string expr, string key, Func<float, float> curve)
        {
            var e = FtExpression.Parse(expr);
            float d = 0;
            for (int i = 0; i <= CurveKeys.Samples; i++)
            {
                var x = i / (float)CurveKeys.Samples;
                var v = Mathf.Clamp01(e.Evaluate(n => n == key ? x : 0));
                d = Math.Max(d, Math.Abs(v - curve(x)));
            }
            return d;
        }

        static void CheckSdk(string item, string expr)
        {
            var (problems, est) = SdkValidate(expr);
            var ours = FtExpression.Count(expr);
            Item(item + "-sdk-valid", problems.Count == 0, string.Join(" | ", problems));
            Item(item + "-sdk-instructions", est == ours.Instructions, $"sdk={est} ours={ours.Instructions}");
            var (pi, po) = SdkEstimate(expr);
            Item(item + "-sdk-estimate", po == ours.Operations, $"sdk instructions={pi} operations={po}; ours operations={ours.Operations}");
        }

        public static void Fit() => Cli.Run(() =>
        {
            foreach (var (name, c) in new[] { ("s", SCurve()), ("square", Square()), ("step", Step()), ("line", CurveKeys.Line(0.6f)), ("weighted", Weighted()) })
            {
                var keys = CurveKeys.From(c);
                var line = CurveFit.Fit("JawOpen", "jaw.jawOpen", keys, 1, 1);
                var expr = CurveFit.FitOne(new List<CurveFit.Line> { line });
                var d = ExprDiff(expr, "JawOpen", x => CurveKeys.Value(c, x));
                Item("fit-" + name, d <= CurveFit.Tolerance + 1e-4f && line.MaxError <= CurveFit.Tolerance, $"diff={F(d)} points={line.Points.Count} expr={expr}");
                CheckSdk("fit-" + name, expr);
            }
            // Scale goes into the polyline
            var scaled = CurveFit.Fit("JawOpen", "jaw.jawOpen", CurveKeys.From(SCurve()), 1, 0.5f);
            var se = CurveFit.FitOne(new List<CurveFit.Line> { scaled });
            Item("fit-scale", ExprDiff(se, "JawOpen", x => CurveKeys.Value(SCurve(), x) * 0.5f) <= CurveFit.Tolerance + 1e-4f, se);

            // Four zigzags on one blendshape: over one expression, fitted within the limits with MaxError reported
            var zig = new AnimationCurve();
            for (int i = 0; i <= 20; i++) zig.AddKey(i / 20f, i == 0 ? 0 : i % 2 == 1 ? 0.2f : 0.8f);
            for (int i = 0; i < zig.length; i++) { AnimationUtility.SetKeyLeftTangentMode(zig, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(zig, i, AnimationUtility.TangentMode.Linear); }
            var keysZ = CurveKeys.From(zig);
            var lines = new[] { "LipFunnelUpperLeft", "LipFunnelUpperRight", "LipFunnelLowerLeft", "LipFunnelLowerRight" }
                .Select(k => CurveFit.Fit(k, "lip.mouthFunnel", keysZ, 1, 1)).ToList();
            var ze = CurveFit.FitOne(lines);
            var zc = FtExpression.Count(ze);
            Item("fit-limit", zc.Characters <= CurveFit.MaxCharacters && zc.Tokens <= CurveFit.MaxTokens && zc.Instructions <= CurveFit.MaxInstructions,
                $"characters={zc.Characters} tokens={zc.Tokens} instructions={zc.Instructions}");
            Item("fit-limit-reported", lines.Any(l => l.MaxError > CurveFit.Tolerance), string.Join(",", lines.Select(l => F(l.MaxError))));
            CheckSdk("fit-limit", ze);
            // Two keys: the larger
            var two = new List<CurveFit.Line> { CurveFit.Fit("BrowInnerUpLeft", "brow.browInnerUp", CurveKeys.From(SCurve()), 1, 1), CurveFit.Fit("BrowInnerUpRight", "brow.browInnerUp", null, 0.9f, 1) };
            var te = FtExpression.Parse(CurveFit.FitOne(two));
            float worst = 0;
            for (int i = 0; i <= 20; i++)
                for (int j = 0; j <= 20; j++)
                {
                    float a = i / 20f, b = j / 20f;
                    var want = Math.Max(CurveKeys.Value(SCurve(), a), 0.9f * b);
                    worst = Math.Max(worst, Math.Abs(Mathf.Clamp01(te.Evaluate(n => n == "BrowInnerUpLeft" ? a : n == "BrowInnerUpRight" ? b : 0)) - want));
                }
            Item("fit-two-keys-max", worst <= CurveFit.Tolerance + 1e-4f, $"diff={F(worst)} expr={te.Text}");
            return Done();
        });

        // ---- step 4: the generated avatar

        static VRCAvatarDescriptor Variant(TableModel m)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(m.VariantPath);
            return go != null ? go.GetComponent<VRCAvatarDescriptor>() : null;
        }

        static List<VRCAvatarDescriptor.FaceTrackingParameterBehavior> CurveBehaviors(VRCAvatarDescriptor d) =>
            (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>())
                .Where(b => b != null && b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape && (b.weightBlendSetName ?? "").StartsWith(NativeSets.Prefix)).ToList();

        // The value a row of the table gives at x on a device: its curve (or the straight line of its strength) x scale.
        static float RowValue(TableModel m, TrackRow r, bool desktop, float x)
        {
            var scale = m.Parts[r.Part].Scale;
            var c = r.Curve(desktop);
            return c != null ? CurveKeys.Value(c.ToAnimationCurve(), x) * scale : Mathf.Clamp01(r.Strength(desktop) * x * scale);
        }

        public static void Generated() => Cli.Run(() =>
        {
            var m = TableLoader.Load(Table);
            var d = Variant(m);
            if (d == null) { Item("variant", false, "no Prefab Variant at " + m.VariantPath + " (run Cli.Generate first)"); return Done(); }
            var curved = m.CurvedShapes();
            var ours = (d.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>()).Where(s => s.setName.StartsWith(NativeSets.Prefix)).ToList();
            var inSets = ours.SelectMany(s => s.faceTrackingBlendshapeNames.Where(n => !string.IsNullOrEmpty(n) && curved.Contains(n)).Select(n => s.setName + ":" + n)).ToList();
            Item("no-set-rows-for-curves", inSets.Count == 0, string.Join(",", inSets));
            var bs = CurveBehaviors(d);
            var dup = bs.GroupBy(b => b.weightBlendSetName + ":" + b.targetBlendshapeName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Item("one-behavior-per-set-and-shape", bs.Count > 0 && dup.Count == 0, $"behaviors={bs.Count} {string.Join(",", dup)}");
            var setNames = new HashSet<string>(ours.Select(s => s.setName));
            Item("behavior-sets-exist", bs.All(b => setNames.Contains(b.weightBlendSetName)), string.Join(",", bs.Select(b => b.weightBlendSetName).Where(n => !setNames.Contains(n))));
            var jawVr = bs.FirstOrDefault(b => b.weightBlendSetName == NativeSets.NeutralSetName("jaw"));
            var jawDesktop = bs.FirstOrDefault(b => b.weightBlendSetName == NativeSets.DesktopName(NativeSets.NeutralSetName("jaw")));
            Item("per-device-behaviors", jawVr != null && jawDesktop != null && jawVr.customExpression != jawDesktop.customExpression);
            var meshes = (d.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>()).Where(x => x != null && x.sharedMesh != null).ToList();
            Item("behavior-meshes", bs.All(b => meshes.Any(x => x.sharedMesh.GetBlendShapeIndex(b.targetBlendshapeName) >= 0)));
            var warnings = FaceTrackingExpressionValidator.CollectWarnings(d);
            Item("sdk-warnings", warnings.Count == 0, string.Join(" | ", warnings));

            // Each expression against the table's curves: one key at x, the others at 0 (their value at 0)
            var plan = NativeFtWriter.Build(m);
            var worst = 0f; var where = "";
            foreach (var set in plan.Sets)
                foreach (var ct in set.Curves.Where(c => c.Lines.All(l => l.MaxError <= CurveFit.Tolerance)))
                {
                    var b = bs.FirstOrDefault(x => x.weightBlendSetName == set.Name && x.targetBlendshapeName == ct.Shape);
                    if (b == null) { Item("behavior-of-" + set.Name + "-" + ct.Shape, false); continue; }
                    var e = FtExpression.Parse(b.customExpression);
                    var desktop = set.Side == Device.Desktop;
                    var rows = Rows(m, set).Where(r => m.Parts[r.Part].Shape == ct.Shape).ToList();
                    foreach (var r in rows)
                        for (int i = 0; i <= CurveKeys.Samples; i++)
                        {
                            var x = i / (float)CurveKeys.Samples;
                            var want = rows.Max(o => o == r ? RowValue(m, o, desktop, x) : RowValue(m, o, desktop, 0));
                            var got = Mathf.Clamp01(e.Evaluate(n => n == r.Key ? x : 0));
                            var diff = Math.Abs(got - want);
                            if (diff > worst) { worst = diff; where = $"{set.Name} {ct.Shape} {r.Key} x={F(x)}"; }
                        }
                }
            Item("expressions-follow-curves", worst <= CurveFit.Tolerance + 1e-4f, $"diff={F(worst)} at {where}");

            // Two keys on one blendshape: one expression, the larger of the rows
            var brow = bs.Where(b => b.weightBlendSetName == NativeSets.NeutralSetName("brow") && b.targetBlendshapeName == m.Parts["brow.browInnerUp"].Shape).ToList();
            Item("two-keys-one-expression", brow.Count == 1 && brow[0].customExpression.StartsWith("max("), brow.Count == 1 ? brow[0].customExpression : brow.Count.ToString());

            // The predicted cost is the generated avatar's, and the SDK counts the same
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRefs.Path(m.Prefab, "prefab"));
            plan.Visemes = VisemeStop.Build(m, prefab.GetComponent<VRCAvatarDescriptor>(), new List<string>());
            var predicted = FtCost.Predict(prefab, plan);
            var actual = FtCost.Count(d);
            Item("cost-predicted", predicted.Behaviors == actual.Behaviors && predicted.Instructions == actual.Instructions && predicted.Operations == actual.Operations,
                $"predicted={predicted.Behaviors}/{predicted.Instructions}/{predicted.Operations} actual={actual.Behaviors}/{actual.Instructions}/{actual.Operations}");
            var sdkInstructions = (d.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>())
                .Where(b => b != null && b.combineMode == VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom).Sum(b => SdkEstimate(b.customExpression).instructions);
            Item("cost-instructions-sdk", sdkInstructions == actual.Instructions, $"sdk={sdkInstructions} ours={actual.Instructions}");

            // Generating again does not add behaviors
            var before = d.faceTrackingParameterBehaviors.Count;
            Generator.Generate(TableLoader.Load(Table));
            var after = Variant(m).faceTrackingParameterBehaviors.Count;
            Item("generate-again", before == after, $"before={before} after={after}");
            return Done();
        });

        // The table's rows a set has (its region's rows of neutral or of the state's override).
        static IEnumerable<TrackRow> Rows(TableModel m, NativeSets.SetDef set)
        {
            if (set.StateMachine == null) return m.Neutral[set.Region].Where(r => r.Part != null);
            var st = m.StateMachine(set.StateMachine).State(set.State);
            return NativeSets.OverrideRows(m, set.Region, st.Regions[set.Region]).Where(r => r.Part != null);
        }

        // ---- step 5: FaceEval against the generated expressions

        public static void Eval() => Cli.Run(() =>
        {
            var m = TableLoader.Load(Table);
            var d = Variant(m);
            if (d == null) { Item("variant", false, "run Cli.Generate first"); return Done(); }
            var bs = CurveBehaviors(d);
            var worst = 0f; var where = ""; var n = 0;
            foreach (var (state, setOf) in new[] { ("neutral", (Func<string, string>)(NativeSets.NeutralSetName)), ("joy", r => NativeSets.SetName("Emotion", "joy", r)) })
                foreach (var desktop in new[] { false, true })
                    foreach (var b in bs)
                    {
                        var region = m.Neutral.Keys.FirstOrDefault(r => { var nm = setOf(r); return (desktop ? NativeSets.DesktopName(nm) : nm) == b.weightBlendSetName; });
                        if (region == null) continue;
                        var e = FtExpression.Parse(b.customExpression);
                        foreach (var key in e.Names)
                            for (int i = 0; i <= 100; i++)
                            {
                                var x = i / 100f;
                                var inputs = new Dictionary<string, float> { [InputDef.TrackPrefix + key] = x };
                                var states = new Dictionary<string, string> { ["Emotion"] = state };
                                var faces = FaceEval.Evaluate(m, inputs, states, desktop);
                                var mesh = m.Parts.Values.First(p => p.Shape == b.targetBlendshapeName).Mesh;
                                var got = faces.TryGetValue(mesh + "/" + b.targetBlendshapeName, out var v) ? v : 0;
                                var want = Mathf.Clamp01(e.Evaluate(k => k == key ? x : 0)) * 100;
                                n++;
                                var diff = Math.Abs(got - want);
                                if (diff > worst) { worst = diff; where = $"{b.weightBlendSetName} {b.targetBlendshapeName} {key} x={F(x)} faceeval={F(got)} expr={F(want)}"; }
                            }
                    }
            Item("faceeval-matches-expressions", n > 0 && worst <= 1.0f, $"compared={n} diff={F(worst)} {where}");
            return Done();
        });

        // ---- step 5: the ScenarioRunner records (-ftRecords <jsonl> of Characterization~/scenarios/curve.txt) against
        // FaceEval: a snap labelled "joy" is in Emotion/joy, the others in neutral; VRMode 0 is desktop.
        public static void Scenario() => Cli.Run(() =>
        {
            var m = TableLoader.Load(Table);
            var curved = m.CurvedShapes();
            var worst = 0f; var where = ""; var n = 0;
            foreach (var line in System.IO.File.ReadAllLines(CliArgs.Require("-ftRecords")))
            {
                var r = JObject.Parse(line);
                var set = (JObject)r["set"];
                var inputs = set.Properties().Where(p => NativeKeys.Index(p.Name) >= 0).ToDictionary(p => InputDef.TrackPrefix + p.Name, p => p.Value.Value<float>());
                var desktop = set["VRMode"] != null && set["VRMode"].Value<float>() < 0.5f;
                var states = new Dictionary<string, string> { ["Emotion"] = (string)r["label"] == "joy" ? "joy" : "neutral" };
                var faces = FaceEval.Evaluate(m, inputs, states, desktop);
                foreach (var shape in curved)
                {
                    var mesh = m.Parts.Values.First(p => p.Shape == shape).Mesh;
                    var want = faces.TryGetValue(mesh + "/" + shape, out var v) ? v : 0;
                    var got = r["shapes"]?[shape]?.Value<float>() ?? 0;
                    n++;
                    if (Math.Abs(got - want) > worst) { worst = Math.Abs(got - want); where = $"{r["scenario"]}/{r["label"]} {shape} record={F(got)} faceeval={F(want)}"; }
                }
            }
            Item("scenario-matches-faceeval", n > 0 && worst <= 1.0f, $"compared={n} diff={F(worst)} {where}");
            return Done();
        });

        // ---- step 4: over Excellent but within the limits

        public static void Ops() => Cli.Run(() =>
        {
            var m = TableLoader.Load(Table);
            // Without the zigzags (they take many instructions), and with states overriding lip: each state copies the
            // lip rows into its sets (two per state: the sets are per device), adding Face Tracking Operations only.
            foreach (var r in m.AllRows().Where(r => r.Part == "lip.mouthFunnel")) { r.CurveVr = r.CurveDesktop = null; }
            var sm = m.StateMachine("Emotion");
            var lipRow = m.Neutral["lip"].First();
            for (int i = 0; i < 25; i++)
            {
                var st = new StateDef { Name = "ops" + i };
                st.Regions["lip"] = new RegionDef { Rows = { new TrackRow { Key = lipRow.Key, Part = lipRow.Part, StrengthVr = 0.9f, StrengthDesktop = 0.9f } } };
                sm.States.Add(st);
            }
            var plan = NativeFtWriter.Build(m);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRefs.Path(m.Prefab, "prefab"));
            plan.Visemes = VisemeStop.Build(m, prefab.GetComponent<VRCAvatarDescriptor>(), new List<string>());
            var cost = FtCost.Predict(prefab, plan);
            Item("ops-over-excellent", cost.OverExcellent && !cost.OverBehaviors && !cost.OverInstructions && plan.Sets.Count <= 128,
                $"behaviors={cost.Behaviors} instructions={cost.Instructions} operations={cost.Operations} rank={cost.Rank} sets={plan.Sets.Count}");
            Item("ops-not-stopped", FtCost.Over(cost, plan) == null);
            var lines = FtCost.Lines(cost, plan.Sets);
            Item("ops-log", lines.Any(l => l.StartsWith("notice:") && l.Contains(cost.Rank)) && lines.Any(l => l.StartsWith("face tracking cost:") && l.Contains($"({cost.Rank})")), string.Join(" | ", lines.Where(l => !l.StartsWith("curve:"))));
            return Done();
        });

        // ---- step 6: edits of the table

        public static void Edit() => Cli.Run(() =>
        {
            var exprPath = TableLoader.ExpressionPath(Table);
            var e = TableLoader.Read(exprPath);
            JToken Neutral(string region, string key) => e["neutral"][region][key];
            StateMachineEdit.SetNeutralCurve(e, "eye", "EyeSquintLeft", SCurve());
            var row = Neutral("eye", "EyeSquintLeft");
            Item("edit-curve", row is JObject o && o["curve"] is JArray && o["strength"] == null, row.ToString(Newtonsoft.Json.Formatting.None));
            StateMachineEdit.SetNeutralCurve(e, "eye", "EyeSquintLeft", CurveKeys.Line(0.6f));
            row = Neutral("eye", "EyeSquintLeft");
            Item("edit-straight-strength", row is JObject o2 && o2["curve"] == null && Math.Abs(o2["strength"].Value<float>() - 0.6f) < 1e-4f, row.ToString(Newtonsoft.Json.Formatting.None));
            StateMachineEdit.SetNeutralCurve(e, "eye", "EyeSquintLeft", CurveKeys.Line(1));
            row = Neutral("eye", "EyeSquintLeft");
            Item("edit-straight-one", row.Type == JTokenType.String, row.ToString(Newtonsoft.Json.Formatting.None));
            StateMachineEdit.SetNeutralCurve(e, "eye", "EyeSquintLeft", SCurve(), Device.Desktop);
            row = Neutral("eye", "EyeSquintLeft");
            Item("edit-desktop-only", row["curve"] is JObject co && co["vr"] is JArray && co["desktop"] is JArray, row.ToString(Newtonsoft.Json.Formatting.None));
            var m = TableLoader.Load(Table, p => System.IO.Path.GetFullPath(p) == System.IO.Path.GetFullPath(exprPath) ? e : TableLoader.Read(p));
            var r = m.Neutral["eye"].First(x => x.Key == "EyeSquintLeft");
            Item("edit-desktop-only-model", r.CurveVr == null && Math.Abs(r.StrengthVr - 1) < 1e-4f && r.CurveDesktop != null);
            // The movement changed: the curve stays
            var (part, s) = StateMachineEdit.ReadRow(Neutral("eye", "EyeSquintLeft"));
            StateMachineEdit.SetNeutralRow(e, "eye", "EyeSquintLeft", "eye.eyeWideLeft", s);
            row = Neutral("eye", "EyeSquintLeft");
            Item("edit-movement-keeps-curve", (string)row["part"] == "eye.eyeWideLeft" && row["curve"] is JObject);
            // Copy from: a state's curve goes to the other state
            StateMachineEdit.CopyRegion(e, "Emotion", "angry", "eye", "joy");
            var copied = e["stateMachines"]["Emotion"]["states"]["angry"]["regions"]["eye"]["EyeClosedLeft"];
            var orig = e["stateMachines"]["Emotion"]["states"]["joy"]["regions"]["eye"]["EyeClosedLeft"];
            Item("edit-copy-keeps-curve", copied?["curve"] != null && JToken.DeepEquals(copied, orig), copied?.ToString(Newtonsoft.Json.Formatting.None));
            // A curve the loader would refuse stays as written when another value of the row is edited
            var bad = new JArray { new JObject { ["time"] = 0, ["value"] = 0 }, new JObject { ["time"] = 1.5, ["value"] = 1 } };
            e["neutral"]["eye"]["EyeWideRight"] = new JObject { ["part"] = "eye.eyeWideRight", ["curve"] = bad };
            var (bp, bs) = StateMachineEdit.ReadRow(Neutral("eye", "EyeWideRight"));
            StateMachineEdit.SetNeutralRow(e, "eye", "EyeWideRight", "eye.eyeSquintRight", bs);
            row = Neutral("eye", "EyeWideRight");
            Item("edit-keeps-unread-curve", row is JObject ro && (string)ro["part"] == "eye.eyeSquintRight" && JToken.DeepEquals(ro["curve"], bad), row.ToString(Newtonsoft.Json.Formatting.None));
            e["neutral"]["eye"]["EyeWideRight"] = "eye.eyeWideRight";
            // The settings editor's save writes it back the same
            var text = JsonTableWriter.Write(e);
            Item("edit-save-roundtrip", JToken.DeepEquals(JObject.Parse(text), e));
            // The edits of TableEditorChecks.Edit ("setNeutralCurve", "setRowCurve")
            var ops = new JObject { ["op"] = "setRowCurve", ["sm"] = "Emotion", ["state"] = "joy", ["region"] = "eye", ["key"] = "EyeClosedLeft", ["device"] = "desktop", ["curve"] = CurveKeys.From(Square()).ToJson() };
            StateMachineEdit.Apply(e, ops);
            var joyRow = e["stateMachines"]["Emotion"]["states"]["joy"]["regions"]["eye"]["EyeClosedLeft"];
            Item("edit-op-setRowCurve", joyRow["curve"] is JObject jo && jo["desktop"] is JArray, joyRow.ToString(Newtonsoft.Json.Formatting.None));
            return Done();
        });
    }
}
