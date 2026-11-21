using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The resolved model as JSON, for Cli.Load and the checks that compare it.
    public static class ModelJson
    {
        // A strength per device as {"vr", "desktop"}, else a number, so that the output of a table without them stays as before.
        public static JToken Strength(TrackRow r) =>
            r.StrengthVr != r.StrengthDesktop ? new JObject { ["vr"] = r.StrengthVr, ["desktop"] = r.StrengthDesktop } : (JToken)r.StrengthVr;

        // A row's curves as the table writes them, or null for a row without a curve (straight on both devices).
        public static JToken Curve(TrackRow r) =>
            r.CurveVr == null && r.CurveDesktop == null ? null
            : CurveKeys.Same(r.CurveVr, r.CurveDesktop) ? (JToken)r.CurveVr.ToJson()
            : new JObject { ["vr"] = r.CurveVr?.ToJson(), ["desktop"] = r.CurveDesktop?.ToJson() };

        static JObject WithCurve(JObject o, TrackRow r)
        {
            var c = Curve(r);
            if (c != null) o["curve"] = c;
            return o;
        }

        static JObject Row(TrackRow r, TableModel m) => WithCurve(new JObject
        {
            ["key"] = r.Key, ["part"] = r.Part, ["strength"] = Strength(r),
            ["shape"] = r.Part != null && m.Parts.TryGetValue(r.Part, out var t) ? t.Shape : null,
        }, r);

        public static JObject ToJson(TableModel m)
        {
            return new JObject
            {
                ["avatar"] = m.AvatarName,
                ["prefab"] = m.Prefab?.Path,
                ["inputs"] = new JArray(m.Inputs.Select(i => new JObject { ["name"] = i.Name, ["key"] = i.Key, ["parameter"] = i.Parameter, ["expression"] = i.Expression, ["expressionFrom"] = i.IsTrack ? "tracking key" : i.ExpressionFromTable ? "table" : "built-in" })),
                ["neutral"] = new JObject(m.Neutral.Select(kv => new JProperty(kv.Key, new JArray(kv.Value.Select(r => Row(r, m)))))),
                ["stateMachines"] = new JArray(m.StateMachines.Select(sm => new JObject
                {
                    ["name"] = sm.Name,
                    ["states"] = new JArray(sm.States.Select(st =>
                    {
                        var o = new JObject
                        {
                            ["name"] = st.Name,
                            ["menu"] = st.Menu ? sm.MenuValue(st.Name) : 0,
                            ["regions"] = new JObject(st.Regions.Select(kv => new JProperty(kv.Key, kv.Value.Off ? (JToken)TableLoader.Off : new JArray(kv.Value.Rows.Select(r => Row(r, m)))))),
                            ["fixed"] = new JObject(st.Fixed.Select(kv => new JProperty(kv.Key, kv.Value))),
                        };
                        // Only when set, so that the output of a table without it stays as before.
                        if (st.StopVisemesAny) o["stopVisemes"] = st.StopVisemesByDevice ? (JToken)Device.Of(st.StopVisemesDesktop) : true;
                        return o;
                    })),
                    ["transitions"] = new JArray(sm.Transitions.Select(t =>
                    {
                        var o = new JObject
                        {
                            ["from"] = t.From, ["to"] = t.To, ["duration"] = t.Duration,
                            ["when"] = new JArray(t.When.Select(c => new JArray(c.Input, c.Op, c.Value))),
                        };
                        if (t.Device != null) o["device"] = t.Device;
                        return o;
                    })),
                })),
                ["stopWhileTalking"] = new JArray(m.StopWhileTalking),
                ["dropped"] = new JArray(m.Dropped),
                ["warnings"] = new JArray(m.Warnings),
                ["notes"] = new JArray(m.Notes),
            };
        }

        // The avatar layer as the loader resolved it (Cli.Load -ftFull 1): parts with mesh, scale and property, the
        // menu, layers, AFK, the Face Tracking options, the output paths and the recordings.
        public static JObject Avatar(TableModel m)
        {
            JToken Ref(AssetRef r) => r == null ? null : new JObject { ["guid"] = r.Guid, ["path"] = r.Path };
            var afk = m.AfkClips;
            return new JObject
            {
                ["avatarTablePath"] = m.AvatarTablePath,
                ["prefab"] = Ref(m.Prefab),
                ["variant"] = m.VariantPath,
                ["outputFolder"] = m.OutputFolder,
                ["menuFolder"] = m.MenuFolder,
                ["mesh"] = m.DefaultMesh,
                ["parts"] = new JObject(m.Parts.Select(kv => new JProperty(kv.Key, new JObject
                {
                    ["mesh"] = kv.Value.Mesh, ["shape"] = kv.Value.Shape, ["property"] = kv.Value.Property,
                    ["propertyMin"] = kv.Value.PropertyMin, ["propertyMax"] = kv.Value.PropertyMax, ["scale"] = kv.Value.Scale,
                }))),
                ["faceTrackParameter"] = m.FaceTrackParameter,
                ["afkPlayParameter"] = m.AfkPlayParameter,
                ["faceTrackLabel"] = m.FaceTrackLabel,
                ["afkPlayLabel"] = m.AfkPlayLabel,
                ["menu"] = Ref(m.Menu),
                ["subMenu"] = m.SubMenu,
                ["afkGroups"] = new JArray(m.AfkGroups.Select(g => new JObject { ["parameters"] = new JArray(g.Parameters), ["wait"] = g.Wait })),
                ["afkClips"] = new JObject
                {
                    ["actionIntro"] = Ref(afk.ActionIntro), ["actionLoop"] = Ref(afk.ActionLoop), ["actionOutro"] = Ref(afk.ActionOutro),
                    ["fxIntro"] = Ref(afk.FxIntro), ["fxLoop"] = Ref(afk.FxLoop), ["fxOutro"] = Ref(afk.FxOutro),
                },
                ["overwriteFaceTracking"] = m.OverwriteFaceTracking,
                ["eyeGazeMode"] = m.EyeGazeMode,
                ["layers"] = new JObject(m.Layers.Select(kv => new JProperty(kv.Key, new JObject
                {
                    ["remove"] = new JArray(kv.Value.Remove),
                    ["weights"] = new JObject(kv.Value.Weights.Select(w => new JProperty(w.Key, w.Value))),
                    ["stopWhileFaceTrack"] = new JArray(kv.Value.StopWhileFaceTrack),
                    ["muteWhileFaceTrack"] = new JArray(kv.Value.MuteWhileFaceTrack),
                }))),
                ["measurements"] = new JObject(Measurements.Sides.Select(side =>
                {
                    var s = side == Measurements.Vr ? m.Measurements.VrSide : m.Measurements.DesktopSide;
                    return new JProperty(side, s == null ? null : new JObject
                    {
                        ["files"] = new JArray(s.Files),
                        ["keys"] = new JObject(s.Keys.Select(k => new JProperty(k.Key, new JObject { ["sent"] = k.Value.Sent, ["neutral"] = k.Value.Neutral, ["max"] = k.Value.Max, ["readMax"] = k.Value.ReadMax }))),
                    });
                })),
                ["propertyPartsInRows"] = new JArray(m.PropertyPartsInRows),
            };
        }
    }
}
