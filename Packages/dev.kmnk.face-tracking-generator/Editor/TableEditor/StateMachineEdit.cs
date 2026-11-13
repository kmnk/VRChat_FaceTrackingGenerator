using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Edits of the expression table (JSON tree) made by the state machine window and by TableEditorChecks.Edit
    // ({"table": "expression", "op": ...}). design.md "設定エディタ". Keys the editor does not show are left as they are.
    public static class StateMachineEdit
    {
        public const string Neutral = "neutral", Override = "override", Off = "off";

        // ---- reading

        public static JObject Machines(JObject e) => e["stateMachines"] as JObject;
        public static JObject Machine(JObject e, string sm) => Machines(e)?[sm] as JObject;
        public static JObject States(JObject e, string sm) => Machine(e, sm)?["states"] as JObject;
        public static JArray Transitions(JObject e, string sm) => Machine(e, sm)?["transitions"] as JArray;

        // A state's object, made when it is written as null or {}.
        static JObject State(JObject e, string sm, string st)
        {
            var states = States(e, sm) ?? throw new ArgumentException("no state machine " + sm);
            var p = states.Property(st) ?? throw new ArgumentException($"no state {sm}/{st}");
            if (!(p.Value is JObject o)) p.Value = o = new JObject();
            return o;
        }

        public static List<string> StateNames(JObject e, string sm) => States(e, sm)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();

        // Neutral, Override or Off for a state's region.
        public static string RegionMode(JObject e, string sm, string st, string region)
        {
            var v = (States(e, sm)?[st] as JObject)?["regions"]?[region];
            if (v == null) return Neutral;
            return v.Type == JTokenType.String && (string)v == TableLoader.Off ? Off : Override;
        }

        public static (string part, float strength) ReadRow(JToken v) =>
            v == null || v.Type == JTokenType.Null ? (null, 1f)
            : v.Type == JTokenType.String ? ((string)v, 1f)
            : ((string)v["part"], v["strength"]?.Value<float>() ?? 1f);

        static JToken WriteRow(string part, float strength) =>
            part == null ? JValue.CreateNull() : Math.Abs(strength - 1) < 1e-6 ? (JToken)part : new JObject { ["part"] = part, ["strength"] = Sides.Num(Math.Round((double)strength, 3)) };

        // The neutral rows of a region: key -> (part, strength), in table order.
        public static List<(string key, string part, float strength)> NeutralRows(JObject e, string region) =>
            (e["neutral"]?[region] as JObject)?.Properties().Select(p => { var r = ReadRow(p.Value); return (p.Name, r.part, r.strength); }).ToList()
            ?? new List<(string, string, float)>();

        public static List<string> NeutralRegions(JObject e) => (e["neutral"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();

        // The rows a state writes for a region (Override): key -> (part or null for Don't move, strength).
        public static List<(string key, string part, float strength)> OverrideRows(JObject e, string sm, string st, string region) =>
            ((States(e, sm)?[st] as JObject)?["regions"]?[region] as JObject)?.Properties().Select(p => { var r = ReadRow(p.Value); return (p.Name, r.part, r.strength); }).ToList()
            ?? new List<(string, string, float)>();

        // ---- edits

        public static void AddStateMachine(JObject e, string name)
        {
            if (!(e["stateMachines"] is JObject sms)) e["stateMachines"] = sms = new JObject();
            if (sms[name] != null) throw new ArgumentException("state machine exists: " + name);
            sms[name] = new JObject { ["states"] = new JObject { [Neutral] = new JObject { ["position"] = new JArray(0, 0) } }, ["transitions"] = new JArray() };
        }

        public static void AddState(JObject e, string sm, string st, float x = 250, float y = 0)
        {
            var states = States(e, sm) ?? throw new ArgumentException("no state machine " + sm);
            if (states[st] != null) throw new ArgumentException("state exists: " + st);
            states[st] = new JObject { ["position"] = new JArray(Sides.Num(Math.Round(x)), Sides.Num(Math.Round(y))) };
        }

        public static void RemoveState(JObject e, string sm, string st)
        {
            var states = States(e, sm);
            if (states == null || states.Count <= 1) throw new ArgumentException("a state machine keeps one state");
            states.Remove(st);
            var tr = Transitions(e, sm);
            if (tr != null) foreach (var t in tr.OfType<JObject>().Where(t => (string)t["from"] == st || (string)t["to"] == st).ToList()) t.Remove();
        }

        public static void RenameState(JObject e, string sm, string from, string to)
        {
            var states = States(e, sm);
            var p = states?.Property(from) ?? throw new ArgumentException($"no state {sm}/{from}");
            if (states[to] != null) throw new ArgumentException("state exists: " + to);
            p.AddBeforeSelf(new JProperty(to, p.Value));
            p.Remove();
            foreach (var t in Transitions(e, sm)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                if ((string)t["from"] == from) t["from"] = to;
                if ((string)t["to"] == from) t["to"] = to;
            }
        }

        // The default state is the first: moves the state to the top.
        public static void SetDefault(JObject e, string sm, string st)
        {
            var states = States(e, sm);
            var p = states?.Property(st) ?? throw new ArgumentException($"no state {sm}/{st}");
            p.Remove();
            states.AddFirst(p);
        }

        public static void SetMenu(JObject e, string sm, string st, bool menu)
        {
            var o = State(e, sm, st);
            if (menu) o["menu"] = true; else o.Remove("menu");
        }

        public static void SetPosition(JObject e, string sm, string st, float x, float y) =>
            State(e, sm, st)["position"] = new JArray(Sides.Num(Math.Round(x)), Sides.Num(Math.Round(y)));

        public static (float x, float y)? Position(JObject e, string sm, string st) =>
            (States(e, sm)?[st] as JObject)?["position"] is JArray a && a.Count == 2 ? (a[0].Value<float>(), a[1].Value<float>()) : ((float, float)?)null;

        // Adds a transition with one condition (a transition needs one). Returns its index.
        public static int AddTransition(JObject e, string sm, string from, string to, string input, string op = ">", float value = 0.5f, float duration = 0.2f)
        {
            var m = Machine(e, sm) ?? throw new ArgumentException("no state machine " + sm);
            if (!(m["transitions"] is JArray tr)) m["transitions"] = tr = new JArray();
            var when = new JArray();
            when.Add(Condition(input, op, value));
            tr.Add(new JObject { ["from"] = from, ["to"] = to, ["when"] = when, ["duration"] = Sides.Num(Math.Round((double)duration, 3)) });
            return tr.Count - 1;
        }

        public static void RemoveTransition(JObject e, string sm, int index) => Transitions(e, sm)[index].Remove();

        public static void SetDuration(JObject e, string sm, int index, float duration) =>
            Transitions(e, sm)[index]["duration"] = Sides.Num(Math.Round((double)Math.Max(0, duration), 3));

        public static void AddCondition(JObject e, string sm, int index, string input, string op = ">", float value = 0.5f)
        {
            var t = (JObject)Transitions(e, sm)[index];
            if (!(t["when"] is JArray w)) t["when"] = w = new JArray();
            w.Add(Condition(input, op, value));
        }

        public static void SetCondition(JObject e, string sm, int index, int cond, string input, string op, float value) =>
            ((JArray)Transitions(e, sm)[index]["when"])[cond] = Condition(input, op, value);

        // [input, op, value] (one JArray; a JArray given to new JArray(...) alone would be spread into it).
        static JArray Condition(string input, string op, float value)
        {
            var c = new JArray();
            c.Add(input);
            c.Add(op);
            c.Add(Sides.Num(Math.Round((double)value, 3)));
            return c;
        }

        // Removes a condition; the last one stays (a transition needs one).
        public static bool RemoveCondition(JObject e, string sm, int index, int cond)
        {
            var w = (JArray)Transitions(e, sm)[index]["when"];
            if (w.Count <= 1) return false;
            w.RemoveAt(cond);
            return true;
        }

        static JObject Regions(JObject e, string sm, string st, bool make)
        {
            var o = State(e, sm, st);
            if (!(o["regions"] is JObject r) && make) o["regions"] = r = new JObject();
            return o["regions"] as JObject;
        }

        // Neutral removes the region and its rows; Off writes "off"; Override starts with no rows (all keys neutral).
        public static void SetRegion(JObject e, string sm, string st, string region, string mode)
        {
            if (mode == Neutral)
            {
                var r = Regions(e, sm, st, false);
                r?.Remove(region);
                if (r != null && r.Count == 0) State(e, sm, st).Remove("regions");
                return;
            }
            var regions = Regions(e, sm, st, true);
            if (mode == Off) regions[region] = TableLoader.Off;
            else if (!(regions[region] is JObject)) regions[region] = new JObject();
        }

        // Writes a state's row for a key (part null: Don't move). The region becomes Override.
        public static void SetRow(JObject e, string sm, string st, string region, string key, string part, float strength = 1)
        {
            SetRegion(e, sm, st, region, Override);
            ((JObject)Regions(e, sm, st, true)[region])[key] = WriteRow(part, strength);
        }

        // Removes a state's row: the key goes back to neutral.
        public static void RemoveRow(JObject e, string sm, string st, string region, string key) =>
            (Regions(e, sm, st, false)?[region] as JObject)?.Remove(key);

        // Copies the rows another state writes for the region (not the neutral rows it takes).
        public static void CopyRegion(JObject e, string sm, string st, string region, string from)
        {
            SetRegion(e, sm, st, region, Override);
            var to = (JObject)Regions(e, sm, st, true)[region];
            foreach (var (key, part, strength) in OverrideRows(e, sm, from, region)) to[key] = WriteRow(part, strength);
        }

        public static void SetFixed(JObject e, string sm, string st, string part, float amount)
        {
            var o = State(e, sm, st);
            if (!(o["fixed"] is JObject f)) o["fixed"] = f = new JObject();
            f[part] = Sides.Num(Math.Round(Math.Min(100, Math.Max(0, amount))));
        }

        public static void RemoveFixed(JObject e, string sm, string st, string part)
        {
            var o = State(e, sm, st);
            (o["fixed"] as JObject)?.Remove(part);
            if (o["fixed"] is JObject f && f.Count == 0) o.Remove("fixed");
        }

        public static void SetNeutralRow(JObject e, string region, string key, string part, float strength = 1)
        {
            if (!(e["neutral"] is JObject n)) e["neutral"] = n = new JObject();
            if (!(n[region] is JObject r)) n[region] = r = new JObject();
            r[key] = WriteRow(part ?? throw new ArgumentException("a neutral row needs a part"), strength);
        }

        public static void RemoveNeutralRow(JObject e, string region, string key)
        {
            var n = e["neutral"] as JObject;
            (n?[region] as JObject)?.Remove(key);
            if (n?[region] is JObject r && r.Count == 0) n.Remove(region);
        }

        // ---- the command form (TableEditorChecks.Edit, Capture -ftSmEdits)

        public static void Apply(JObject e, JObject op)
        {
            string S(string k) => op[k]?.Type == JTokenType.Null ? null : (string)op[k];
            float F(string k, float d) => op[k] != null ? op[k].Value<float>() : d;
            int I(string k) => op[k].Value<int>();
            var sm = S("sm");
            switch ((string)op["op"])
            {
                case "addStateMachine": AddStateMachine(e, S("name")); break;
                case "addState": AddState(e, sm, S("state"), F("x", 250), F("y", 0)); break;
                case "removeState": RemoveState(e, sm, S("state")); break;
                case "renameState": RenameState(e, sm, S("from"), S("to")); break;
                case "setDefault": SetDefault(e, sm, S("state")); break;
                case "setMenu": SetMenu(e, sm, S("state"), (bool)op["menu"]); break;
                case "setPosition": SetPosition(e, sm, S("state"), F("x", 0), F("y", 0)); break;
                case "addTransition": AddTransition(e, sm, S("from"), S("to"), S("input"), S("cmp") ?? ">", F("value", 0.5f), F("duration", 0.2f)); break;
                case "removeTransition": RemoveTransition(e, sm, I("index")); break;
                case "setDuration": SetDuration(e, sm, I("index"), F("duration", 0)); break;
                case "addCondition": AddCondition(e, sm, I("index"), S("input"), S("cmp") ?? ">", F("value", 0.5f)); break;
                case "setCondition": SetCondition(e, sm, I("index"), I("cond"), S("input"), S("cmp"), F("value", 0)); break;
                case "removeCondition": RemoveCondition(e, sm, I("index"), I("cond")); break;
                case "setRegion": SetRegion(e, sm, S("state"), S("region"), S("mode")); break;
                case "setRow": SetRow(e, sm, S("state"), S("region"), S("key"), S("part"), F("strength", 1)); break;
                case "removeRow": RemoveRow(e, sm, S("state"), S("region"), S("key")); break;
                case "copyRegion": CopyRegion(e, sm, S("state"), S("region"), S("from")); break;
                case "setFixed": SetFixed(e, sm, S("state"), S("part"), F("amount", 100)); break;
                case "removeFixed": RemoveFixed(e, sm, S("state"), S("part")); break;
                case "setNeutralRow": SetNeutralRow(e, S("region"), S("key"), S("part"), F("strength", 1)); break;
                case "removeNeutralRow": RemoveNeutralRow(e, S("region"), S("key")); break;
                default: throw new ArgumentException("unknown op: " + (string)op["op"]);
            }
        }
    }
}
