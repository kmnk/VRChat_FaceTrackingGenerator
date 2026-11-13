using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Reads the avatar table and the expression table it points to, resolves part names to blendshapes, and drops
    // what the avatar does not have (R23). The input table was removed in 0.12.1: transitions compare tracking keys.
    public static class TableLoader
    {
        public sealed class TableException : Exception
        {
            public TableException(string message) : base(message) { }
        }

        public static TableModel Load(string avatarTablePath) => Load(avatarTablePath, Read);

        // read: table path -> parsed table. The table editor passes tables it holds in memory (not saved yet).
        public static TableModel Load(string avatarTablePath, Func<string, JObject> read)
        {
            var m = new TableModel { AvatarTablePath = avatarTablePath };
            var avatar = read(avatarTablePath);
            var dir = Path.GetDirectoryName(avatarTablePath);
            ParseAvatar(avatar, m, avatarTablePath);

            var tables = Obj(avatar, "tables", avatarTablePath);
            var exprPath = Path.Combine(dir, Str(tables, "expression", avatarTablePath));
            var missingParts = StrList(avatar["missingParts"]);
            // 0.12.1 removed the input table: transitions compare tracking keys. An old "tables.input" is not read.
            if (tables["input"] != null)
                m.Notes.Add("avatar table: 'tables.input' is not read since 0.12.1 (transitions compare tracking keys)");

            var expr = read(exprPath);
            ParseExpressions(expr, m, exprPath);

            Resolve(m, missingParts);
            return m;
        }

        // ---- reading

        // The expression table path of an avatar table, built from its "tables" as Load does.
        public static string ExpressionPath(string avatarTablePath)
        {
            var tables = Obj(Read(avatarTablePath), "tables", avatarTablePath);
            return Path.Combine(Path.GetDirectoryName(avatarTablePath), Str(tables, "expression", avatarTablePath));
        }

        public static JObject Read(string path)
        {
            if (!File.Exists(path)) throw new TableException("table not found: " + path);
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))))
            {
                try
                {
                    var settings = new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
                    var token = JToken.ReadFrom(reader, settings);
                    if (!(token is JObject o)) throw new TableException(path + ": the top level must be an object");
                    return o;
                }
                catch (JsonException e)
                {
                    throw new TableException(path + ": " + e.Message);
                }
            }
        }

        static JObject Obj(JObject o, string key, string file)
        {
            if (!(o[key] is JObject v)) throw new TableException($"{file}: '{Join(o.Path, key)}' must be an object");
            return v;
        }

        static string Str(JObject o, string key, string file)
        {
            var v = o[key];
            if (v == null || v.Type != JTokenType.String) throw new TableException($"{file}: '{Join(o.Path, key)}' must be a string");
            return (string)v;
        }

        static string OptStr(JObject o, string key, string def) => o[key] != null && o[key].Type == JTokenType.String ? (string)o[key] : def;

        static float Num(JToken t, string file)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw new TableException($"{file}: '{t?.Path}' must be a number");
            return t.Value<float>();
        }

        // "scale" of a part: a number from 0 to 1, 1 when it is not written.
        static float PartScale(JObject po, string part, string file)
        {
            var t = po["scale"];
            if (t == null) return 1;
            if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new TableException($"{file}: 'parts.{part}.scale' must be a number from 0 to 1");
            var v = t.Value<float>();
            if (float.IsNaN(v) || v < 0 || v > 1) throw new TableException($"{file}: 'parts.{part}.scale' must be a number from 0 to 1");
            return v;
        }

        static List<string> StrList(JToken t) => t is JArray a ? a.Select(x => (string)x).ToList() : new List<string>();

        static string Join(string path, string key) => string.IsNullOrEmpty(path) ? key : path + "." + key;

        static AssetRef Ref(JToken t, string file)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (!(t is JObject o)) throw new TableException($"{file}: '{t.Path}' must be {{\"guid\": ..., \"path\": ...}}");
            return new AssetRef { Guid = OptStr(o, "guid", null), Path = OptStr(o, "path", null) };
        }

        // ---- left/right pairs

        static IEnumerable<(string name, JToken value)> Expand(JObject container, string file)
        {
            foreach (var p in container.Properties())
            {
                if (!(p.Value is JObject entry) || entry["sides"] == null)
                {
                    yield return (p.Name, p.Value);
                    continue;
                }
                var sides = new List<Dictionary<string, string>>();
                if (entry["sides"] is JArray arr)
                    foreach (var s in arr) sides.Add(new Dictionary<string, string> { ["side"] = (string)s });
                else if (entry["sides"] is JObject so)
                    foreach (var sp in so.Properties())
                    {
                        var vars = new Dictionary<string, string> { ["side"] = sp.Name };
                        if (sp.Value is JObject vo) foreach (var vp in vo.Properties()) vars[vp.Name] = (string)vp.Value;
                        sides.Add(vars);
                    }
                else throw new TableException($"{file}: '{entry.Path}.sides' must be an array or an object");

                foreach (var vars in sides)
                {
                    var name = Sub(p.Name, vars);
                    if (name == p.Name) throw new TableException($"{file}: '{entry.Path}' has sides but its name has no {{side}}");
                    var clone = (JObject)entry.DeepClone();
                    clone.Remove("sides");
                    yield return (name, SubToken(clone, vars));
                }
            }
        }

        static string Sub(string s, Dictionary<string, string> vars)
        {
            foreach (var kv in vars) s = s.Replace("{" + kv.Key + "}", kv.Value);
            return s;
        }

        static JToken SubToken(JToken t, Dictionary<string, string> vars)
        {
            switch (t)
            {
                case JObject o:
                    var n = new JObject();
                    foreach (var p in o.Properties()) n.Add(Sub(p.Name, vars), SubToken(p.Value, vars));
                    return n;
                case JArray a:
                    return new JArray(a.Select(x => SubToken(x, vars)));
                case JValue v when v.Type == JTokenType.String:
                    return new JValue(Sub((string)v, vars));
                default:
                    return t.DeepClone();
            }
        }

        // ---- avatar layer

        static void ParseAvatar(JObject a, TableModel m, string file)
        {
            m.AvatarName = Str(a, "name", file);
            m.Prefab = Ref(a["prefab"], file);
            if (m.Prefab == null) throw new TableException($"{file}: 'prefab' is required");
            m.OutputFolder = OptStr(a, "outputFolder", "Assets/FaceTrackingGeneratorOutput/" + m.AvatarName);
            m.MenuFolder = m.OutputFolder;
            // The Prefab Variant goes into the output folder unless the table says where.
            m.VariantPath = OptStr(a, "variant", $"{m.OutputFolder}/{m.AvatarName}_FT.prefab");
            m.DefaultMesh = OptStr(a, "mesh", null);
            Removed(a, file, "gaze", "the eyes move in the Face Tracking section");
            Removed(a, file, "stopAutoEyes", "the eyelids move in the Face Tracking section");
            m.FaceTrackParameter = OptStr(a, "faceTrackParameter", "FaceTrack");
            m.AfkPlayParameter = OptStr(a, "afkPlayParameter", "AFKPlay");
            m.FaceTrackLabel = OptStr(a, "faceTrackLabel", "FaceTrack");
            m.AfkPlayLabel = OptStr(a, "afkPlayLabel", "AFK");
            m.OverwriteFaceTracking = a["overwriteFaceTracking"]?.Value<bool>() ?? false;
            m.EyeGazeMode = OptStr(a, "eyeGazeMode", "FaceTracking");
            if (m.EyeGazeMode != "FaceTracking" && m.EyeGazeMode != "Both" && m.EyeGazeMode != "BoneRotation")
                throw new TableException($"{file}: 'eyeGazeMode' must be \"FaceTracking\", \"Both\" or \"BoneRotation\"");
            m.Menu = Ref(a["menu"], file);
            m.SubMenu = OptStr(a, "subMenu", null);

            if (a["parts"] is JObject parts)
                foreach (var p in parts.Properties())
                {
                    if (p.Value.Type == JTokenType.String)
                    {
                        if (m.DefaultMesh == null) throw new TableException($"{file}: 'mesh' is required when parts are written as strings");
                        m.Parts[p.Name] = new PartTarget { Mesh = m.DefaultMesh, Shape = (string)p.Value };
                    }
                    else if (p.Value is JObject po)
                        m.Parts[p.Name] = new PartTarget { Mesh = OptStr(po, "mesh", m.DefaultMesh), Shape = Str(po, "shape", file), Scale = PartScale(po, p.Name, file) };
                    else throw new TableException($"{file}: '{p.Value.Path}' must be a string or {{\"mesh\", \"shape\", \"scale\"}}");
                }

            if (a["afkGroups"] is JArray groups)
                foreach (var g in groups.OfType<JObject>())
                    m.AfkGroups.Add(new AfkGroup { Parameters = StrList(g["parameters"]), Wait = g["wait"] != null ? Num(g["wait"], file) : 0 });

            if (a["layers"] is JObject layers)
                foreach (var p in layers.Properties())
                {
                    if (p.Name != "fx" && p.Name != "gesture" && p.Name != "action")
                        throw new TableException($"{file}: 'layers.{p.Name}' must be fx, gesture or action");
                    var lo = (JObject)p.Value;
                    var adj = new LayerAdjust { Remove = StrList(lo["remove"]), StopWhileFaceTrack = StrList(lo["stopWhileFaceTrack"]), MuteWhileFaceTrack = StrList(lo["muteWhileFaceTrack"]) };
                    if (adj.MuteWhileFaceTrack.Count > 0 && p.Name != "fx")
                        throw new TableException($"{file}: 'layers.{p.Name}.muteWhileFaceTrack' is supported only for fx");
                    if (lo["weights"] is JObject w) foreach (var wp in w.Properties()) adj.Weights[wp.Name] = Num(wp.Value, file);
                    m.Layers[p.Name] = adj;
                }

            if (a["afkClips"] is JObject ac)
            {
                m.AfkClips.ActionIntro = Ref(ac["actionIntro"], file);
                m.AfkClips.ActionLoop = Ref(ac["actionLoop"], file);
                m.AfkClips.ActionOutro = Ref(ac["actionOutro"], file);
                m.AfkClips.FxIntro = Ref(ac["fxIntro"], file);
                m.AfkClips.FxLoop = Ref(ac["fxLoop"], file);
                m.AfkClips.FxOutro = Ref(ac["fxOutro"], file);
            }
        }

        // ---- input layer

        // A key that 0.12.0 removed (spec R13): an error that names it and why.
        static void Removed(JObject o, string file, string key, string why)
        {
            if (o[key] != null) throw new TableException($"{file}: '{Join(o.Path, key)}' was removed in 0.12.0 ({why}). Make the tables again from the templates");
        }

        // ---- expression layer

        public const string Off = "off";

        static void ParseExpressions(JObject o, TableModel m, string file)
        {
            foreach (var key in new[] { "trees", "switches", "stops" })
                if (o[key] != null)
                    throw new TableException($"{file}: '{key}' was removed in 0.12.0: the expression layer is written as \"neutral\" and \"stateMachines\" " +
                        "(removed: 1D and 2D nodes, curves, smoothing, stops). Make the tables again from the templates");
            m.StopWhileTalking = StrList(o["stopWhileTalking"]);

            if (o["neutral"] != null)
            {
                if (!(o["neutral"] is JObject neutral)) throw new TableException($"{file}: 'neutral' must be an object of region -> {{key: part}}");
                foreach (var rp in neutral.Properties())
                {
                    if (!(rp.Value is JObject rows)) throw new TableException($"{file}: '{rp.Value.Path}' must be an object of tracking key -> part");
                    m.Neutral[rp.Name] = Rows(rows, rp.Name, false, file);
                }
            }

            if (o["stateMachines"] != null)
            {
                if (!(o["stateMachines"] is JObject sms)) throw new TableException($"{file}: 'stateMachines' must be an object");
                foreach (var sp in sms.Properties())
                    m.StateMachines.Add(StateMachine(sp, m, file));
            }
        }

        // Rows of a region: tracking key -> "part", {"part", "strength"} or (override only) null for "Don't move".
        static List<TrackRow> Rows(JObject rows, string region, bool canSkip, string file)
        {
            var list = new List<TrackRow>();
            foreach (var p in rows.Properties())
            {
                if (NativeKeys.Index(p.Name) < 0) throw new TableException($"{file}: '{p.Path}': '{p.Name}' is not a tracking key of the Face Tracking section");
                var row = new TrackRow { Key = p.Name };
                if (p.Value.Type == JTokenType.Null)
                {
                    if (!canSkip) throw new TableException($"{file}: '{p.Path}': a neutral row needs a part");
                }
                else if (p.Value.Type == JTokenType.String) row.Part = (string)p.Value;
                else if (p.Value is JObject ro)
                {
                    row.Part = Str(ro, "part", file);
                    if (ro["strength"] != null)
                    {
                        row.Strength = Num(ro["strength"], file);
                        if (float.IsNaN(row.Strength) || row.Strength < 0 || row.Strength > 1) throw new TableException($"{file}: '{ro.Path}.strength' must be 0 to 1");
                    }
                }
                else throw new TableException($"{file}: '{p.Path}' must be a part name or {{\"part\", \"strength\"}}" + (canSkip ? " or null" : ""));
                if (row.Part != null)
                {
                    if (!PartNames.IsForm(row.Part)) throw new TableException($"{file}: '{p.Path}': '{row.Part}' is not a part name (region[.state].movement)");
                    if (PartNames.Region(row.Part) != region) throw new TableException($"{file}: '{p.Path}': part {row.Part} is not in region {region}");
                }
                list.Add(row);
            }
            return list;
        }

        static StateMachineDef StateMachine(JProperty sp, TableModel m, string file)
        {
            if (!(sp.Value is JObject so)) throw new TableException($"{file}: '{sp.Path}' must be an object");
            var sm = new StateMachineDef { Name = sp.Name };
            if (!(so["states"] is JObject states) || states.Count == 0) throw new TableException($"{file}: '{so.Path}.states' must be an object with one or more states");
            foreach (var stp in states.Properties())
            {
                var st = new StateDef { Name = stp.Name };
                if (stp.Value.Type != JTokenType.Null)
                {
                    if (!(stp.Value is JObject sto)) throw new TableException($"{file}: '{stp.Path}' must be an object");
                    st.Menu = sto["menu"]?.Value<bool>() ?? false;
                    if (sto["regions"] is JObject regions)
                        foreach (var rp in regions.Properties())
                        {
                            if (rp.Value.Type == JTokenType.String && (string)rp.Value == Off) st.Regions[rp.Name] = new RegionDef { Off = true };
                            else if (rp.Value is JObject ro) st.Regions[rp.Name] = new RegionDef { Rows = Rows(ro, rp.Name, true, file) };
                            else throw new TableException($"{file}: '{rp.Value.Path}' must be \"off\" or an object of tracking key -> part");
                        }
                    else if (sto["regions"] != null) throw new TableException($"{file}: '{sto.Path}.regions' must be an object");
                    if (sto["fixed"] is JObject fixedParts)
                        foreach (var fp in fixedParts.Properties())
                        {
                            if (!PartNames.IsForm(fp.Name)) throw new TableException($"{file}: '{fp.Path}': '{fp.Name}' is not a part name (region[.state].movement)");
                            var v = Num(fp.Value, file);
                            if (float.IsNaN(v) || v < 0 || v > 100) throw new TableException($"{file}: '{fp.Path}' must be 0 to 100");
                            st.Fixed[fp.Name] = v;
                        }
                    else if (sto["fixed"] != null) throw new TableException($"{file}: '{sto.Path}.fixed' must be an object of part -> amount");
                }
                sm.States.Add(st);
            }
            foreach (var t in (so["transitions"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var td = new TransitionDef { From = Str(t, "from", file), To = Str(t, "to", file), Duration = t["duration"] != null ? Num(t["duration"], file) : 0 };
                if (sm.State(td.From) == null || sm.State(td.To) == null)
                    throw new TableException($"{file}: state machine '{sm.Name}': unknown state in {t.Path}");
                foreach (var c in (t["when"] as JArray ?? new JArray()).OfType<JArray>())
                {
                    if (c.Count != 3) throw new TableException($"{file}: '{c.Path}' must be [tracking key, \">\" or \"<\", value]");
                    // The Face Tracking section's key ("v2/" may be written, as in neutral).
                    var cond = new Condition { Input = NativeInputs.NameOf((string)c[0]), Op = (string)c[1], Value = Num(c[2], file) };
                    if (cond.Op != ">" && cond.Op != "<") throw new TableException($"{file}: '{c.Path}': operator must be > or <");
                    if (NativeKeys.Index(cond.Input) < 0)
                        throw new TableException($"{file}: '{c.Path}': '{c[0]}' is not a tracking key of the Face Tracking section " +
                            "(since 0.12.1 a condition compares a tracking key, such as MouthCornerPullLeft; the input table was removed)");
                    td.When.Add(cond);
                }
                if (td.When.Count == 0) throw new TableException($"{file}: '{t.Path}' needs a 'when' condition");
                sm.Transitions.Add(td);
            }
            return sm;
        }

        static void Warn(TableModel m, string msg)
        {
            if (!m.Warnings.Contains(msg)) m.Warnings.Add(msg);
        }

        // ---- dropping what the device or avatar does not have (R23)

        static void Resolve(TableModel m, List<string> missingParts)
        {
            // The tracking keys the transitions compare: forwarded to the Animator as FT/In/<key> (Forward to Animator,
            // a Custom expression of the key alone).
            var compared = m.StateMachines.SelectMany(sm => sm.Transitions).SelectMany(t => t.When).Select(c => c.Input).Distinct().OrderBy(NativeKeys.Index).ToList();
            foreach (var k in compared)
                m.Inputs.Add(new InputDef { Name = k, Key = k, Expression = k, Parameter = NativeInputs.Parameter(k) });

            foreach (var sm in m.StateMachines)
            {
                var reach = new HashSet<string> { sm.States[0].Name };
                foreach (var st in sm.MenuStates) reach.Add(st.Name);
                for (var changed = true; changed;)
                {
                    changed = false;
                    foreach (var t in sm.Transitions)
                        if (reach.Contains(t.From) && reach.Add(t.To)) changed = true;
                }
                foreach (var st in sm.States.Where(s => !reach.Contains(s.Name)))
                    Warn(m, $"state machine {sm.Name}: state {st.Name} cannot be reached (no transition into it and not in the menu)");
                var names = sm.States.SelectMany(st => st.Regions.Keys.Select(r => NativeSets.SetName(sm.Name, st.Name, r)));
                foreach (var n in names.Where(n => n.Length > NativeSets.MaxNameLength))
                    throw new TableException($"Blendshapes Set name '{n}' is longer than {NativeSets.MaxNameLength} characters; use shorter state machine, state or region names");
            }

            // Rows of parts the avatar layer does not set (R23).
            var missingPartSet = new HashSet<string>(missingParts);
            bool Keep(string where, string part)
            {
                if (part == null || m.Parts.ContainsKey(part)) return true;
                var msg = $"{where}: part {part} is not in the avatar layer";
                if (missingPartSet.Contains(part)) { if (!m.Dropped.Contains(msg)) m.Dropped.Add(msg); }
                else Warn(m, msg);
                return false;
            }
            // A neutral row whose part has no blendshape is left out. An override row's is kept as Don't move: the key moves
            // nothing in that state (spec R5), rather than falling back to the neutral row.
            void Filter(List<TrackRow> rows, string where, bool overrides)
            {
                foreach (var r in rows.ToList())
                {
                    if (Keep(where, r.Part)) continue;
                    if (overrides) r.Part = null; else rows.Remove(r);
                }
            }
            foreach (var kv in m.Neutral) Filter(kv.Value, "neutral " + kv.Key, false);
            foreach (var sm in m.StateMachines)
                foreach (var st in sm.States)
                {
                    foreach (var kv in st.Regions.Where(x => !x.Value.Off)) Filter(kv.Value.Rows, $"{sm.Name}/{st.Name} {kv.Key}", true);
                    foreach (var part in st.Fixed.Keys.ToList())
                    {
                        if (!Keep($"{sm.Name}/{st.Name} fixed", part)) { st.Fixed.Remove(part); continue; }
                        st.Fixed[part] *= m.Parts[part].Scale;
                    }
                }

            // The tracking keys of the rows that are left and of the conditions, as inputs "key:<key>" (for the settings
            // editor and FaceEval).
            var keys = m.Neutral.Values.SelectMany(r => r)
                .Concat(m.StateMachines.SelectMany(sm => sm.States).SelectMany(st => st.Regions.Values.SelectMany(rg => rg.Rows)))
                .Select(r => r.Key).Concat(compared).Distinct().OrderBy(k => NativeKeys.Index(k)).ToList();
            foreach (var k in keys)
                m.Inputs.Add(new InputDef { Name = InputDef.TrackPrefix + k, Key = k, IsTrack = true });
        }
    }
}
