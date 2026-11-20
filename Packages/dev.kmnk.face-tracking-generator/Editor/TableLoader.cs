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
            // 0.12.1 removed the input table: transitions compare tracking keys. An old "tables.input" is not read.
            if (tables["input"] != null)
                m.Notes.Add("avatar table: 'tables.input' is not read since 0.12.1 (transitions compare tracking keys)");

            var expr = read(exprPath);
            ParseExpressions(expr, m, exprPath);

            m.Measurements = Measurements.Load(dir, avatar["measurements"], m.Warnings);
            // 0.15.0 to 0.17.0 computed the other device's values from the recordings for the main device.
            if (avatar["measurementsMain"] != null)
                Warn(m, "avatar table: 'measurementsMain' is not used since 0.18.0: the values for VR and desktop are written in the expression table (remove it)");
            Resolve(m);
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
            return Parse(File.ReadAllText(path), path);
        }

        // Parses a table's text as Read does (comments allowed, duplicate keys refused). path is for messages.
        public static JObject Parse(string text, string path)
        {
            using (var reader = new JsonTextReader(new StringReader(text)))
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

        // An asset reference as written in a table ({"guid", "path"}), or null when it is not one.
        public static AssetRef AssetRefOf(JToken t) =>
            t is JObject o ? new AssetRef { Guid = (string)o["guid"], Path = (string)o["path"] } : null;

        // ---- avatar layer

        static void ParseAvatar(JObject a, TableModel m, string file)
        {
            m.AvatarName = Str(a, "name", file);
            m.Prefab = Ref(a["prefab"], file);
            if (m.Prefab == null) throw new TableException($"{file}: 'prefab' is required");
            m.OutputFolder = OptStr(a, "outputFolder", FtNames.OutputRoot + m.AvatarName);
            m.MenuFolder = m.OutputFolder;
            m.AssetMenuFolder = m.OutputFolder;
            // The Prefab Variant goes into the output folder unless the table says where.
            m.VariantPath = OptStr(a, "variant", FtNames.Variant(m.OutputFolder, m.AvatarName));
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
                    {
                        // "property" in place of "shape": a material Float or Range property of the renderer. A row with
                        // both is left out with a warning, so the other parts still work.
                        if (po["property"] != null && po["shape"] != null) { Warn(m, $"avatar: part {p.Name} has both 'shape' and 'property'; it is left out"); continue; }
                        if (po["property"] != null)
                            m.Parts[p.Name] = new PartTarget { Mesh = OptStr(po, "mesh", m.DefaultMesh), Property = Str(po, "property", file), Scale = PartScale(po, p.Name, file) };
                        else
                            m.Parts[p.Name] = new PartTarget { Mesh = OptStr(po, "mesh", m.DefaultMesh), Shape = Str(po, "shape", file), Scale = PartScale(po, p.Name, file) };
                    }
                    else throw new TableException($"{file}: '{p.Value.Path}' must be a string or {{\"mesh\", \"shape\" or \"property\", \"scale\"}}");
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
                {
                    // The name goes into Blendshapes Set, parameter, layer and menu asset names (as the State Machine
                    // window allows them).
                    if (!IsMachineName(sp.Name))
                        throw new TableException($"{file}: 'stateMachines.{sp.Name}': a state machine name is letters, digits and _ only");
                    var same = m.StateMachines.FirstOrDefault(x => string.Equals(x.Name, sp.Name, StringComparison.OrdinalIgnoreCase));
                    if (same != null)
                        throw new TableException($"{file}: 'stateMachines.{sp.Name}': the name differs from '{same.Name}' only in capitals (their menu files would be the same)");
                    m.StateMachines.Add(StateMachine(sp, m, file));
                }
            }
        }

        // A state machine name: letters, digits and _.
        public static bool IsMachineName(string name) => !string.IsNullOrEmpty(name) && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$");

        // Rows of a region: tracking key -> "part", {"part", "strength"} or (override only) null for "Don't move". The
        // strength is a number for both devices or {"vr": n, "desktop": n} (a device not written: 1).
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
                    if (ro["strength"] is JObject so)
                    {
                        foreach (var dp in so.Properties())
                            if (!Device.IsName(dp.Name)) throw new TableException($"{file}: '{dp.Path}': a strength per device has \"vr\" and \"desktop\" only");
                        row.StrengthVr = so["vr"] != null ? Strength(so["vr"], file) : 1;
                        row.StrengthDesktop = so["desktop"] != null ? Strength(so["desktop"], file) : 1;
                    }
                    else if (ro["strength"] != null) row.SetStrength(Strength(ro["strength"], file));
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

        static float Strength(JToken t, string file)
        {
            var v = Num(t, file);
            if (float.IsNaN(v) || v < 0 || v > 1) throw new TableException($"{file}: '{t.Path}' must be 0 to 1");
            return v;
        }

        // "stopVisemes": true or false (both devices), or "vr" / "desktop" (only that device).
        static (bool vr, bool desktop) StopVisemes(JToken t, string file)
        {
            if (t == null || t.Type == JTokenType.Null) return (false, false);
            if (t.Type == JTokenType.Boolean) { var b = (bool)t; return (b, b); }
            if (t.Type == JTokenType.String && Device.IsName((string)t)) return ((string)t == Device.Vr, (string)t == Device.Desktop);
            throw new TableException($"{file}: '{t.Path}' must be true, false, \"vr\" or \"desktop\"");
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
                    (st.StopVisemesVr, st.StopVisemesDesktop) = StopVisemes(sto["stopVisemes"], file);
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
                // A "from" or "to" that is not the name of a state (another name, or not a string) is left out with a
                // warning, not an error, so the settings editor still opens the table and its State Machine window can
                // remove the transition.
                var from = t["from"]?.Type == JTokenType.String ? (string)t["from"] : null;
                var to = t["to"]?.Type == JTokenType.String ? (string)t["to"] : null;
                if (from == null || to == null || sm.State(from) == null || sm.State(to) == null)
                {
                    Warn(m, $"{file}: state machine '{sm.Name}': unknown state in {t.Path} ({t["from"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "no from"} -> {t["to"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "no to"}); the transition is left out (remove it in the State Machine window)");
                    continue;
                }
                var td = new TransitionDef { From = from, To = to, Duration = t["duration"] != null ? Num(t["duration"], file) : 0 };
                if (t["device"] != null && t["device"].Type != JTokenType.Null)
                {
                    var dev = t["device"].Type == JTokenType.String ? (string)t["device"] : null;
                    if (!Device.IsName(dev)) throw new TableException($"{file}: '{t.Path}.device' must be \"vr\" or \"desktop\"");
                    td.Device = dev;
                }
                foreach (var c in (t["when"] as JArray ?? new JArray()).OfType<JArray>())
                {
                    if (c.Count != 3 && c.Count != 4) throw new TableException($"{file}: '{c.Path}' must be [tracking key, \">\" or \"<\", value]");
                    // The Face Tracking section's key ("v2/" may be written here; neutral's rows refuse it).
                    var cond = new Condition { Input = NativeInputs.NameOf((string)c[0]), Op = (string)c[1], Value = Num(c[2], file) };
                    // The desktop value of 0.13.x development builds: a transition for one device is written with "device".
                    if (c.Count == 4)
                        Warn(m, $"{file}: '{c[3].Path}' is not used: write a transition for one device with \"device\": \"vr\" or \"desktop\" (remove the fourth element)");
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

        static void Resolve(TableModel m)
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
                if (m.SetsByDevice) names = names.Select(NativeSets.DesktopName);
                foreach (var n in names.Where(n => n.Length > NativeSets.MaxNameLength))
                    throw new TableException($"Blendshapes Set name '{n}' is longer than {NativeSets.MaxNameLength} characters; use shorter state machine, state or region names");
            }

            // With sets per device, a desktop set's name must not be another set's name (a state machine named "desktop"
            // could have one).
            if (m.SetsByDevice)
            {
                var plain = new HashSet<string>(m.Neutral.Keys.Select(NativeSets.NeutralSetName)
                    .Concat(m.StateMachines.SelectMany(sm => sm.States.SelectMany(st => st.Regions.Where(r => !r.Value.Off).Select(r => NativeSets.SetName(sm.Name, st.Name, r.Key))))));
                foreach (var n in plain.Select(NativeSets.DesktopName).Where(plain.Contains).OrderBy(x => x, StringComparer.Ordinal))
                    throw new TableException($"Blendshapes Set name '{n}' is both a set of the tables and the desktop copy of another set; rename the state machine 'desktop' (strengths per device make desktop copies)");
            }

            // Rows of parts the avatar layer does not set (R23).
            bool Keep(string where, string part)
            {
                if (part == null || m.Parts.ContainsKey(part)) return true;
                var msg = $"{where}: part {part} is not in the avatar layer";
                // Not set (no blendshape in the avatar layer yet) is not a mistake: the row is left out, and Check
                // says how many there are in one line (UnsetParts) instead of one finding per row.
                if (!m.Dropped.Contains(msg)) m.Dropped.Add(msg);
                m.UnsetParts.Add(part);
                return false;
            }
            // A neutral row whose part has no blendshape is left out. An override row's is kept as Don't move: the key moves
            // nothing in that state (spec R5), rather than falling back to the neutral row.
            // A material property part moves only in a state's fixed amounts: the rows of neutral and the overrides go to
            // VRChat's Blendshapes Sets, which move blendshapes only. Such a row is left out like a missing part's.
            bool KeepRow(string where, string part)
            {
                if (!Keep(where, part)) return false;
                if (part == null || !m.Parts[part].IsProperty) return true;
                Warn(m, $"{where}: part {part} is a material property, which moves only in a state's fixed amounts; the row is left out");
                m.PropertyPartsInRows.Add(part);
                return false;
            }
            void Filter(List<TrackRow> rows, string where, bool overrides)
            {
                foreach (var r in rows.ToList())
                {
                    if (KeepRow(where, r.Part)) continue;
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
