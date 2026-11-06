using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Reads the avatar table and the input and expression tables it points to, expands left/right pairs,
    // resolves part names to blendshapes, and drops what the device or the avatar does not have (R23).
    //
    // Left/right pairs: an entry of "inputs", "switches" or "trees" may have
    //   "sides": ["Left", "Right"]                              -> "{side}" is replaced
    //   "sides": {"Left": {"s": "L"}, "Right": {"s": "R"}}      -> "{side}" and "{s}" are replaced
    // in the entry name and in every string and property name inside the entry.
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
            var inputPath = Path.Combine(dir, Str(tables, "input", avatarTablePath));
            var exprPath = Path.Combine(dir, Str(tables, "expression", avatarTablePath));
            var missingParts = StrList(avatar["missingParts"]);

            var input = read(inputPath);
            var missingKeys = StrList(input["missing"]);
            ParseInputs(input, m, inputPath);

            var expr = read(exprPath);
            ParseExpressions(expr, m, exprPath);

            Resolve(m, missingKeys, missingParts);
            return m;
        }

        // ---- reading

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
            // The Prefab Variant goes into the output folder unless the table says where.
            m.VariantPath = OptStr(a, "variant", $"{m.OutputFolder}/{m.AvatarName}_FT.prefab");
            m.DefaultMesh = OptStr(a, "mesh", null);
            m.Gaze = OptStr(a, "gaze", "shared");
            if (m.Gaze != "shared") throw new TableException($"{file}: 'gaze' supports only \"shared\" for now");
            m.FaceTrackParameter = OptStr(a, "faceTrackParameter", "FaceTrack");
            m.AfkPlayParameter = OptStr(a, "afkPlayParameter", "AFKPlay");
            m.FaceTrackLabel = OptStr(a, "faceTrackLabel", "FaceTrack");
            m.AfkPlayLabel = OptStr(a, "afkPlayLabel", "AFK");
            m.StopAutoEyes = a["stopAutoEyes"]?.Value<bool>() ?? false;
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
                        m.Parts[p.Name] = new PartTarget { Mesh = OptStr(po, "mesh", m.DefaultMesh), Shape = Str(po, "shape", file) };
                    else throw new TableException($"{file}: '{p.Value.Path}' must be a string or {{\"mesh\", \"shape\"}}");
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

        static void ParseInputs(JObject o, TableModel m, string file)
        {
            if (o["smoothing"] is JObject sm)
                foreach (var (name, v) in Expand(sm, file))
                {
                    var so = (JObject)v;
                    m.Smoothings.Add(new SmoothingDef { Name = name, LocalTau = Num(so["localTau"], file), RemoteTau = Num(so["remoteTau"], file) });
                }

            foreach (var (name, v) in Expand(Obj(o, "inputs", file), file))
            {
                if (!(v is JObject io)) throw new TableException($"{file}: '{v.Path}' must be an object");
                var def = new InputDef { Name = name };
                if (io["average"] != null)
                {
                    def.Average = StrList(io["average"]);
                    if (def.Average.Count < 2) throw new TableException($"{file}: input '{name}': 'average' needs two or more inputs");
                }
                else
                {
                    def.Key = Str(io, "key", file);
                    var expr = OptStr(io, "expression", null);
                    if (expr != null)
                    {
                        try { FtExpression.Parse(expr); }
                        catch (FtExpression.ParseException e) { throw new TableException($"{file}: input '{name}': {e.Message}"); }
                        def.Expression = expr;
                        def.ExpressionFromTable = true;
                    }
                    def.Smoothing = OptStr(io, "smoothing", null);
                    def.Sync = OptStr(io, "sync", "none");
                    if (def.Sync != "none" && def.Sync != "float8")
                        throw new TableException($"{file}: input '{name}': sync '{def.Sync}' is not supported yet (use none or float8)");
                    if (def.Smoothing != null && m.Smoothings.All(s => s.Name != def.Smoothing))
                        throw new TableException($"{file}: input '{name}': unknown smoothing '{def.Smoothing}'");
                }
                if (m.Inputs.Any(i => i.Name == name)) throw new TableException($"{file}: duplicate input '{name}'");
                def.Parameter = NativeInputs.Parameter(name);
                if (def.Parameter.Length > NativeInputs.MaxParameterLength)
                    throw new TableException($"{file}: input '{name}': the name is too long (the parameter {def.Parameter} is over {NativeInputs.MaxParameterLength} characters)");
                m.Inputs.Add(def);
            }
            var synced = m.Inputs.Where(i => !i.IsDerived && i.Sync == "float8").Select(i => i.Name).ToList();
            if (synced.Count > 0)
                m.Notes.Add($"input table: sync is ignored: inputs come from the Face Tracking section and are not synced ({string.Join(", ", synced)})");
            foreach (var d in m.Inputs.Where(i => i.IsDerived))
                foreach (var src in d.Average)
                    if (m.Input(src) == null || m.Input(src).IsDerived)
                        throw new TableException($"{file}: input '{d.Name}': '{src}' must be a non-derived input");
        }

        // ---- expression layer

        static void ParseExpressions(JObject o, TableModel m, string file)
        {
            m.StopWhileTalking = StrList(o["stopWhileTalking"]);

            if (o["switches"] is JObject sws)
                foreach (var (name, v) in Expand(sws, file))
                {
                    var so = (JObject)v;
                    var sw = new SwitchDef { Name = name, States = StrList(so["states"]) };
                    if (sw.States.Count < 2) throw new TableException($"{file}: switch '{name}' needs two or more states");
                    var defDur = so["duration"] != null ? Num(so["duration"], file) : 0f;
                    foreach (var t in (so["transitions"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var td = new TransitionDef { From = Str(t, "from", file), To = Str(t, "to", file), Duration = t["duration"] != null ? Num(t["duration"], file) : defDur };
                        if (!sw.States.Contains(td.From) || !sw.States.Contains(td.To))
                            throw new TableException($"{file}: switch '{name}': unknown state in {t.Path}");
                        foreach (var c in (t["when"] as JArray ?? new JArray()).OfType<JArray>())
                        {
                            if (c.Count != 3) throw new TableException($"{file}: '{c.Path}' must be [input, \">\" or \"<\", value]");
                            var cond = new Condition { Input = (string)c[0], Op = (string)c[1], Value = Num(c[2], file) };
                            if (cond.Op != ">" && cond.Op != "<") throw new TableException($"{file}: '{c.Path}': operator must be > or <");
                            if (m.Input(cond.Input) == null) throw new TableException($"{file}: '{c.Path}': unknown input '{cond.Input}'");
                            td.When.Add(cond);
                        }
                        if (td.When.Count == 0) throw new TableException($"{file}: '{t.Path}' needs a 'when' condition");
                        sw.Transitions.Add(td);
                    }
                    m.Switches.Add(sw);
                }

            foreach (var (name, v) in Expand(Obj(o, "trees", file), file))
            {
                var to = (JObject)v;
                if (to["tree"] == null) throw new TableException($"{file}: tree '{name}' needs 'tree'");
                m.Trees.Add(new TreeDef { Name = name, Root = ParseNode(to["tree"], m, file) });
            }
        }

        static Node ParseNode(JToken t, TableModel m, string file)
        {
            if (t == null || t.Type == JTokenType.Null) return new PoseNode();
            if (!(t is JObject o)) throw new TableException($"{file}: '{t.Path}' must be an object");
            if (o["1d"] != null)
            {
                var n = new Blend1DNode { Input = (string)o["1d"] };
                if (m.Input(n.Input) == null) throw new TableException($"{file}: '{o.Path}': unknown input '{n.Input}'");
                foreach (var c in Children(o, file))
                {
                    if (c.Count != 2) throw new TableException($"{file}: '{c.Path}' must be [threshold, node]");
                    n.Children.Add((Num(c[0], file), ParseNode(c[1], m, file)));
                }
                return n;
            }
            if (o["2d"] != null)
            {
                var axes = StrList(o["2d"]);
                if (axes.Count != 2) throw new TableException($"{file}: '{o.Path}.2d' must be [inputX, inputY]");
                var n = new Blend2DNode { InputX = axes[0], InputY = axes[1] };
                foreach (var a in axes) if (m.Input(a) == null) throw new TableException($"{file}: '{o.Path}': unknown input '{a}'");
                foreach (var c in Children(o, file))
                {
                    if (c.Count != 2 || !(c[0] is JArray pos) || pos.Count != 2) throw new TableException($"{file}: '{c.Path}' must be [[x, y], node]");
                    n.Children.Add((Num(pos[0], file), Num(pos[1], file), ParseNode(c[1], m, file)));
                }
                return n;
            }
            if (o["switch"] != null)
            {
                var n = new SwitchNode { Switch = (string)o["switch"] };
                var sw = m.Switch(n.Switch) ?? throw new TableException($"{file}: '{o.Path}': unknown switch '{n.Switch}'");
                if (!(o["cases"] is JObject cases)) throw new TableException($"{file}: '{o.Path}.cases' must be an object");
                foreach (var st in sw.States)
                    n.Cases.Add((st, ParseNode(cases[st], m, file)));
                foreach (var p in cases.Properties())
                    if (!sw.States.Contains(p.Name)) throw new TableException($"{file}: '{p.Path}': switch '{sw.Name}' has no state '{p.Name}'");
                return n;
            }
            var pose = new PoseNode();
            foreach (var p in o.Properties())
            {
                if (!p.Name.Contains('.')) throw new TableException($"{file}: '{p.Path}': unknown node key '{p.Name}' (parts are written as region.name)");
                pose.Parts[p.Name] = Num(p.Value, file);
            }
            return pose;
        }

        static IEnumerable<JArray> Children(JObject o, string file)
        {
            if (!(o["children"] is JArray a)) throw new TableException($"{file}: '{o.Path}.children' must be an array");
            return a.Select(x => x as JArray ?? throw new TableException($"{file}: '{x.Path}' must be an array"));
        }

        // ---- dropping what the device or avatar does not have (R23)

        static void Resolve(TableModel m, List<string> missingKeys, List<string> missingParts)
        {
            var missing = new HashSet<string>(missingKeys);
            foreach (var i in m.Inputs.ToList())
            {
                if (!i.IsDerived && i.Expression == null) i.Expression = NativeInputs.Expression(i.Key);
                string why = null;
                if (i.IsDerived) { if (i.Average.Any(s => m.Input(s) == null)) why = "a source input is not sent"; }
                else if (missing.Contains(i.Key)) why = "the device does not send " + i.Key;
                else if (i.Expression == null) why = "no expression for " + i.Key + " (write \"expression\" in the input table)";
                if (why == null) continue;
                m.Inputs.Remove(i);
                m.Dropped.Add($"input {i.Name}: {why}");
            }

            foreach (var sw in m.Switches.ToList())
            {
                foreach (var t in sw.Transitions.ToList())
                    if (t.When.Any(c => m.Input(c.Input) == null))
                    {
                        sw.Transitions.Remove(t);
                        m.Dropped.Add($"switch {sw.Name}: transition {t.From} -> {t.To} uses an input that is not sent");
                    }
                var reach = new HashSet<string> { sw.States[0] };
                for (var changed = true; changed;)
                {
                    changed = false;
                    foreach (var t in sw.Transitions)
                        if (reach.Contains(t.From) && reach.Add(t.To)) changed = true;
                }
                foreach (var st in sw.States.Where(s => !reach.Contains(s)).ToList())
                {
                    sw.States.Remove(st);
                    m.Dropped.Add($"switch {sw.Name}: state {st} cannot be reached");
                }
                sw.Transitions.RemoveAll(t => !reach.Contains(t.From) || !reach.Contains(t.To));
                if (sw.States.Count < 2)
                {
                    m.Switches.Remove(sw);
                    m.Dropped.Add($"switch {sw.Name}: only the default state is left");
                }
            }

            var missingPartSet = new HashSet<string>(missingParts);
            foreach (var tree in m.Trees.ToList())
            {
                tree.Root = Simplify(tree.Root, m, missingPartSet, tree.Name);
                if (IsEmpty(tree.Root))
                {
                    m.Trees.Remove(tree);
                    m.Dropped.Add($"tree {tree.Name}: nothing is left");
                }
            }
        }

        static Node Simplify(Node n, TableModel m, HashSet<string> missingParts, string where)
        {
            switch (n)
            {
                case PoseNode p:
                {
                    var r = new PoseNode();
                    foreach (var kv in p.Parts)
                    {
                        if (m.Parts.ContainsKey(kv.Key)) { r.Parts[kv.Key] = kv.Value; continue; }
                        var msg = $"tree {where}: part {kv.Key} is not in the avatar layer";
                        if (missingParts.Contains(kv.Key)) { if (!m.Dropped.Contains(msg)) m.Dropped.Add(msg); }
                        else if (!m.Warnings.Contains(msg)) m.Warnings.Add(msg);
                    }
                    return r;
                }
                case Blend1DNode b:
                {
                    if (m.Input(b.Input) == null)  // the input stays 0: keep the child nearest to 0
                        return Simplify(b.Children.OrderBy(c => Math.Abs(c.threshold)).ThenBy(c => c.threshold).First().node, m, missingParts, where);
                    var r = new Blend1DNode { Input = b.Input };
                    foreach (var c in b.Children) r.Children.Add((c.threshold, Simplify(c.node, m, missingParts, where)));
                    return r.Children.All(c => IsEmpty(c.node)) ? new PoseNode() : (Node)r;
                }
                case Blend2DNode b:
                {
                    var hasX = m.Input(b.InputX) != null;
                    var hasY = m.Input(b.InputY) != null;
                    if (!hasX || !hasY)  // a missing axis stays 0: keep the children on that axis's 0 line
                    {
                        var oneD = new Blend1DNode { Input = hasX ? b.InputX : b.InputY };
                        foreach (var c in b.Children)
                            if (hasX ? c.y == 0 : c.x == 0) oneD.Children.Add((hasX ? c.x : c.y, c.node));
                        if (!hasX && !hasY || oneD.Children.Count == 0)
                        {
                            var origin = b.Children.FirstOrDefault(c => c.x == 0 && c.y == 0);
                            return origin.node != null ? Simplify(origin.node, m, missingParts, where) : new PoseNode();
                        }
                        oneD.Children.Sort((a, c) => a.threshold.CompareTo(c.threshold));
                        return Simplify(oneD, m, missingParts, where);
                    }
                    var r = new Blend2DNode { InputX = b.InputX, InputY = b.InputY };
                    foreach (var c in b.Children) r.Children.Add((c.x, c.y, Simplify(c.node, m, missingParts, where)));
                    return r.Children.All(c => IsEmpty(c.node)) ? new PoseNode() : (Node)r;
                }
                case SwitchNode s:
                {
                    var sw = m.Switch(s.Switch);
                    if (sw == null)  // the switch stays in its default state
                        return Simplify(s.Cases[0].node, m, missingParts, where);
                    var r = new SwitchNode { Switch = s.Switch };
                    foreach (var c in s.Cases)
                        if (sw.States.Contains(c.state)) r.Cases.Add((c.state, Simplify(c.node, m, missingParts, where)));
                    return r.Cases.All(c => IsEmpty(c.node)) ? new PoseNode() : (Node)r;
                }
                default:
                    throw new InvalidOperationException("unknown node");
            }
        }

        public static bool IsEmpty(Node n) => n is PoseNode p && p.Parts.Count == 0;
    }
}
