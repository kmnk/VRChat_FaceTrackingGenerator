using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // A row's strength and curve on VR and on desktop. A device's curve is null while it is the straight line from
    // (0, 0) to (1, strength); with a curve the strength is its highest point. Written as "strength" (one number while
    // the devices are the same, else {"vr", "desktop"}) while both are straight, else as "curve" (spec R2).
    public struct RowStrength
    {
        public float Vr, Desktop;
        public CurveKeys CurveVr, CurveDesktop;
        // A "curve" the table writes but the loader would refuse: kept as it is and written back unchanged until the
        // row's strength or curve is edited (the loader and Check report it; an edit of another value must not lose it).
        public JToken Unread;
        public RowStrength(float both) { Vr = both; Desktop = both; CurveVr = CurveDesktop = null; Unread = null; }
        public RowStrength(float vr, float desktop) { Vr = vr; Desktop = desktop; CurveVr = CurveDesktop = null; Unread = null; }
        public float Of(bool desktop) => desktop ? Desktop : Vr;
        public float Of(string device) => device == Device.Desktop ? Desktop : Vr;
        public CurveKeys CurveKeysOf(string device) => device == Device.Desktop ? CurveDesktop : CurveVr;
        // The device's curve as Unity's AnimationCurve: its curve, or the straight line of its strength.
        public AnimationCurve CurveOf(string device) => CurveKeysOf(device)?.ToAnimationCurve() ?? CurveKeys.Line(Of(device));
        public bool HasCurve => CurveVr != null || CurveDesktop != null;
        // This strength with the device's value changed (device null: both): the device's curve becomes that straight line.
        public RowStrength With(string device, float value)
        {
            var r = this;
            r.Unread = null;
            if (device != Device.Desktop) { r.Vr = value; r.CurveVr = null; }
            if (device != Device.Vr) { r.Desktop = value; r.CurveDesktop = null; }
            return r;
        }
        // This with the device's curve (device null: both). A straight curve becomes the strength.
        public RowStrength WithCurve(string device, AnimationCurve curve)
        {
            var s = CurveKeys.Straight(curve);
            if (!float.IsNaN(s)) return With(device, s);
            var keys = CurveKeys.From(curve);
            var max = CurveKeys.Max(curve);
            var r = this;
            r.Unread = null;
            if (device != Device.Desktop) { r.Vr = max; r.CurveVr = keys; }
            if (device != Device.Vr) { r.Desktop = max; r.CurveDesktop = keys; }
            return r;
        }
        public bool Same => Math.Abs(Round(Vr) - Round(Desktop)) < 1e-6 && CurveKeys.Same(CurveVr, CurveDesktop);
        static float Round(float v) => (float)Math.Round((double)v, 3);
        public static readonly RowStrength One = new RowStrength(1);
    }

    // Edits of the expression table (JSON tree) made by the state machine window and by TableEditorChecks.Edit
    // ({"table": "expression", "op": ...}). design.md "設定エディタ". Keys the editor does not show are left as they are.
    //
    // A device (Device.Vr or Device.Desktop, the window's input mode) given to an edit of a transition, a strength or
    // stopVisemes changes that device's value only. A value of both devices is then split here: a transition into one
    // for the other device (the original, unchanged) and one for this device right after it; a strength into
    // {"vr", "desktop"}. The window does not know which values are shared (spec R16, R17).
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

        public static (string part, RowStrength strength) ReadRow(JToken v) =>
            v == null || v.Type == JTokenType.Null ? (null, RowStrength.One)
            : v.Type == JTokenType.String ? ((string)v, RowStrength.One)
            : ((string)v["part"], ReadStrength(v["strength"], v["curve"]));

        static RowStrength ReadStrength(JToken s, JToken curve)
        {
            var r = s is JObject o ? new RowStrength(o["vr"]?.Value<float>() ?? 1f, o["desktop"]?.Value<float>() ?? 1f)
                : s != null && s.Type != JTokenType.Null ? new RowStrength(s.Value<float>()) : RowStrength.One;
            if (curve == null || curve.Type == JTokenType.Null) return r;
            // A curve the loader would refuse is left as the straight line (Check and the loader report it).
            CurveKeys Read(JToken t)
            {
                if (t == null) return null;
                try { return CurveKeys.Parse(t, m => new ArgumentException(m)); } catch (ArgumentException) { return null; }
            }
            var vr = curve is JObject co ? Read(co["vr"]) : Read(curve);
            var desktop = curve is JObject co2 ? Read(co2["desktop"]) : vr;
            var written = curve is JObject cw ? (cw["vr"] != null ? 1 : 0) + (cw["desktop"] != null ? 1 : 0) : 1;
            var read = curve is JObject ? (vr != null ? 1 : 0) + (desktop != null ? 1 : 0) : vr != null ? 1 : 0;
            if (read < written || curve is JObject cx && cx.Properties().Any(p => !Device.IsName(p.Name)))
                return new RowStrength(1) { Unread = curve.DeepClone() };
            r = RowStrength.One;
            if (vr != null) r = r.WithCurve(Device.Vr, vr.ToAnimationCurve());
            if (desktop != null) r = r.WithCurve(Device.Desktop, desktop.ToAnimationCurve());
            return r;
        }

        static JToken Num3(float v) => Sides.Num(Math.Round((double)v, 3));

        static JToken WriteRow(string part, RowStrength strength)
        {
            if (part == null) return JValue.CreateNull();
            if (strength.Unread != null) return new JObject { ["part"] = part, ["curve"] = strength.Unread.DeepClone() };
            if (strength.HasCurve)
            {
                JArray Keys(string device) => (strength.CurveKeysOf(device) ?? CurveKeys.From(CurveKeys.Line(strength.Of(device)))).ToJson();
                var vr = Keys(Device.Vr);
                var desktop = Keys(Device.Desktop);
                return new JObject
                {
                    ["part"] = part,
                    ["curve"] = JToken.DeepEquals(vr, desktop) ? (JToken)vr : new JObject { ["vr"] = vr, ["desktop"] = desktop },
                };
            }
            if (!strength.Same) return new JObject { ["part"] = part, ["strength"] = new JObject { ["vr"] = Num3(strength.Vr), ["desktop"] = Num3(strength.Desktop) } };
            return Math.Abs(strength.Vr - 1) < 1e-6 ? (JToken)part : new JObject { ["part"] = part, ["strength"] = Num3(strength.Vr) };
        }

        // The neutral rows of a region: key -> (part, strength), in table order.
        public static List<(string key, string part, RowStrength strength)> NeutralRows(JObject e, string region) =>
            (e["neutral"]?[region] as JObject)?.Properties().Select(p => { var r = ReadRow(p.Value); return (p.Name, r.part, r.strength); }).ToList()
            ?? new List<(string, string, RowStrength)>();

        public static List<string> NeutralRegions(JObject e) => (e["neutral"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();

        // The rows a state writes for a region (Override): key -> (part or null for Don't move, strength).
        public static List<(string key, string part, RowStrength strength)> OverrideRows(JObject e, string sm, string st, string region) =>
            ((States(e, sm)?[st] as JObject)?["regions"]?[region] as JObject)?.Properties().Select(p => { var r = ReadRow(p.Value); return (p.Name, r.part, r.strength); }).ToList()
            ?? new List<(string, string, RowStrength)>();

        // The device of a transition: null (both), Device.Vr or Device.Desktop.
        public static string DeviceOf(JToken t) => t?["device"]?.Type == JTokenType.String && Device.IsName((string)t["device"]) ? (string)t["device"] : null;

        // Whether a transition is shown in an input mode (device null: every transition).
        public static bool Shows(JToken t, string device) => device == null || DeviceOf(t) == null || DeviceOf(t) == device;

        // ---- edits

        public static void AddStateMachine(JObject e, string name)
        {
            if (!(e["stateMachines"] is JObject sms)) e["stateMachines"] = sms = new JObject();
            if (sms[name] != null) throw new ArgumentException("state machine exists: " + name);
            sms[name] = new JObject { ["states"] = new JObject { [Neutral] = new JObject { ["position"] = new JArray(0, 0) } }, ["transitions"] = new JArray() };
        }

        // A state machine name: letters, digits and _ (it goes into Blendshapes Set, parameter and menu asset names).
        public static bool IsMachineName(string name) => TableLoader.IsMachineName(name);

        // Another state machine has the name, without regard to capitals (the menu asset files would collide).
        public static bool MachineNameUsed(JObject e, string name, string except = null) =>
            Machines(e)?.Properties().Any(p => p.Name != except && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == true;

        // Renames in place (the order of the state machines stays).
        public static void RenameStateMachine(JObject e, string from, string to)
        {
            var sms = Machines(e);
            var p = sms?.Property(from) ?? throw new ArgumentException("no state machine " + from);
            if (!IsMachineName(to)) throw new ArgumentException("not a state machine name: " + to);
            if (MachineNameUsed(e, to, from)) throw new ArgumentException("state machine exists: " + to);
            p.AddBeforeSelf(new JProperty(to, p.Value));
            p.Remove();
        }

        public static void RemoveStateMachine(JObject e, string name)
        {
            var sms = Machines(e);
            if (sms?.Property(name) == null) throw new ArgumentException("no state machine " + name);
            sms.Remove(name);
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
            if (tr != null) foreach (var t in tr.OfType<JObject>().Where(t => Name(t["from"]) == st || Name(t["to"]) == st).ToList()) t.Remove();
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
                if (Name(t["from"]) == from) t["from"] = to;
                if (Name(t["to"]) == from) t["to"] = to;
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

        // "stopVisemes": true (both devices), "vr" or "desktop" (that device only), or no item (a table that never had it
        // saves as before). Device null: both devices.
        public static void SetStopVisemes(JObject e, string sm, string st, bool stop, string device = null)
        {
            var o = State(e, sm, st);
            var (vr, desktop) = StopVisemesBoth(o["stopVisemes"]);
            if (device != Device.Desktop) vr = stop;
            if (device != Device.Vr) desktop = stop;
            if (vr && desktop) o["stopVisemes"] = true;
            else if (vr || desktop) o["stopVisemes"] = vr ? Device.Vr : Device.Desktop;
            else o.Remove("stopVisemes");
        }

        static (bool vr, bool desktop) StopVisemesBoth(JToken t) =>
            t == null ? (false, false)
            : t.Type == JTokenType.Boolean ? ((bool)t, (bool)t)
            : t.Type == JTokenType.String ? ((string)t == Device.Vr, (string)t == Device.Desktop)
            : (false, false);

        // Device null: on either device.
        public static bool StopVisemes(JObject e, string sm, string st, string device = null)
        {
            var (vr, desktop) = StopVisemesBoth((States(e, sm)?[st] as JObject)?["stopVisemes"]);
            return device == Device.Vr ? vr : device == Device.Desktop ? desktop : vr || desktop;
        }

        public static void SetPosition(JObject e, string sm, string st, float x, float y) =>
            State(e, sm, st)["position"] = new JArray(Sides.Num(Math.Round(x)), Sides.Num(Math.Round(y)));

        public static (float x, float y)? Position(JObject e, string sm, string st) =>
            (States(e, sm)?[st] as JObject)?["position"] is JArray a && a.Count == 2 ? (a[0].Value<float>(), a[1].Value<float>()) : ((float, float)?)null;

        // Adds a transition with one condition (a transition needs one). Returns its index.
        public static int AddTransition(JObject e, string sm, string from, string to, string input, string op = ">", float value = 0.5f, float duration = 0.2f, string device = null)
        {
            var m = Machine(e, sm) ?? throw new ArgumentException("no state machine " + sm);
            var states = StateNames(e, sm);
            if (!states.Contains(from) || !states.Contains(to)) throw new ArgumentException($"no state {sm}/{(states.Contains(from) ? to : from)}");
            if (!(m["transitions"] is JArray tr)) m["transitions"] = tr = new JArray();
            var when = new JArray();
            when.Add(Condition(input, op, value));
            var t = new JObject { ["from"] = from, ["to"] = to, ["when"] = when, ["duration"] = Sides.Num(Math.Round((double)duration, 3)) };
            if (device != null) t["device"] = device;
            tr.Add(t);
            return tr.Count - 1;
        }

        // The transition to change for a device: the same one when it is that device's (or device is null); a transition
        // of both devices becomes the other device's, and a copy for this device goes right after it. Returns its index.
        public static int ForDevice(JObject e, string sm, int index, string device)
        {
            var tr = Transitions(e, sm);
            var t = (JObject)tr[index];
            if (device == null || DeviceOf(t) == device) return index;
            if (DeviceOf(t) != null) throw new ArgumentException($"transition {index} of {sm} is not on {device}");
            var copy = (JObject)t.DeepClone();
            t["device"] = Device.Other(device);
            copy["device"] = device;
            t.AddAfterSelf(copy);
            return index + 1;
        }

        // Removes a transition; with a device, a transition of both devices stays as the other device's.
        public static void RemoveTransition(JObject e, string sm, int index, string device = null)
        {
            var t = Transitions(e, sm)[index];
            if (device != null && DeviceOf(t) != null && DeviceOf(t) != device) throw new ArgumentException($"transition {index} of {sm} is not on {device}");
            if (device != null && DeviceOf(t) == null) t["device"] = Device.Other(device);
            else t.Remove();
        }

        // The transitions whose "from" or "to" is not a state of the machine (written by hand, or by an older version),
        // by index. The loader leaves them out with a warning; the State Machine window offers to remove them.
        public static List<int> BrokenTransitions(JObject e, string sm)
        {
            var states = new HashSet<string>(StateNames(e, sm));
            var tr = Transitions(e, sm)?.ToList() ?? new List<JToken>();
            return Enumerable.Range(0, tr.Count)
                .Where(i => !(tr[i] is JObject t) || !states.Contains(Name(t["from"])) || !states.Contains(Name(t["to"]))).ToList();
        }

        public static string Name(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : "";

        public static void RemoveBrokenTransitions(JObject e, string sm)
        {
            var tr = Transitions(e, sm);
            foreach (var i in BrokenTransitions(e, sm).OrderByDescending(i => i)) tr[i].Remove();
        }

        // The edits of a transition return its index after the edit (it moves by one when a transition of both devices is
        // split for the device).
        public static int SetDuration(JObject e, string sm, int index, float duration, string device = null)
        {
            index = ForDevice(e, sm, index, device);
            Transitions(e, sm)[index]["duration"] = Sides.Num(Math.Round((double)Math.Max(0, duration), 3));
            return index;
        }

        public static int AddCondition(JObject e, string sm, int index, string input, string op = ">", float value = 0.5f, string device = null)
        {
            index = ForDevice(e, sm, index, device);
            var t = (JObject)Transitions(e, sm)[index];
            if (!(t["when"] is JArray w)) t["when"] = w = new JArray();
            w.Add(Condition(input, op, value));
            return index;
        }

        // A fourth element (the desktop value of 0.13.x development builds) is dropped.
        public static int SetCondition(JObject e, string sm, int index, int cond, string input, string op, float value, string device = null)
        {
            index = ForDevice(e, sm, index, device);
            ((JArray)Transitions(e, sm)[index]["when"])[cond] = Condition(input, op, value);
            return index;
        }

        // [input, op, value] (one JArray; a JArray given to new JArray(...) alone would be spread into it).
        static JArray Condition(string input, string op, float value)
        {
            var c = new JArray();
            c.Add(input);
            c.Add(op);
            c.Add(Sides.Num(Math.Round((double)value, 3)));
            return c;
        }

        // Removes a condition; the last one stays (a transition needs one). Returns the transition's index, as the other edits.
        public static int RemoveCondition(JObject e, string sm, int index, int cond, string device = null)
        {
            if (((JArray)Transitions(e, sm)[index]["when"]).Count <= 1) return index;
            index = ForDevice(e, sm, index, device);
            ((JArray)Transitions(e, sm)[index]["when"]).RemoveAt(cond);
            return index;
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
        public static void SetRow(JObject e, string sm, string st, string region, string key, string part, RowStrength strength)
        {
            SetRegion(e, sm, st, region, Override);
            ((JObject)Regions(e, sm, st, true)[region])[key] = WriteRow(part, strength);
        }

        public static void SetRow(JObject e, string sm, string st, string region, string key, string part, float strength = 1) =>
            SetRow(e, sm, st, region, key, part, new RowStrength(strength));

        // Changes a state's row's strength on a device (null: both); the part and the other device's strength stay.
        public static void SetRowStrength(JObject e, string sm, string st, string region, string key, float strength, string device = null)
        {
            var (part, s) = ReadRow((Regions(e, sm, st, false)?[region] as JObject)?[key]);
            if (part == null) throw new ArgumentException($"no row {sm}/{st}/{region}/{key} with a part");
            SetRow(e, sm, st, region, key, part, s.With(device, strength));
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

        public static void SetNeutralRow(JObject e, string region, string key, string part, RowStrength strength)
        {
            if (!(e["neutral"] is JObject n)) e["neutral"] = n = new JObject();
            if (!(n[region] is JObject r)) n[region] = r = new JObject();
            r[key] = WriteRow(part ?? throw new ArgumentException("a neutral row needs a part"), strength);
        }

        public static void SetNeutralRow(JObject e, string region, string key, string part, float strength = 1) =>
            SetNeutralRow(e, region, key, part, new RowStrength(strength));

        // Changes a neutral row's strength on a device (null: both); the part and the other device's strength stay.
        public static void SetNeutralStrength(JObject e, string region, string key, float strength, string device = null)
        {
            var (part, s) = ReadRow(e["neutral"]?[region]?[key]);
            if (part == null) throw new ArgumentException($"no neutral row {region}/{key}");
            SetNeutralRow(e, region, key, part, s.With(device, strength));
        }

        // Changes a neutral row's curve on a device (null: both); the part and the other device's value stay.
        public static void SetNeutralCurve(JObject e, string region, string key, AnimationCurve curve, string device = null)
        {
            var (part, s) = ReadRow(e["neutral"]?[region]?[key]);
            if (part == null) throw new ArgumentException($"no neutral row {region}/{key}");
            SetNeutralRow(e, region, key, part, s.WithCurve(device, Clean(curve)));
        }

        // Changes a state's row's curve on a device (null: both); the part and the other device's value stay.
        public static void SetRowCurve(JObject e, string sm, string st, string region, string key, AnimationCurve curve, string device = null)
        {
            var (part, s) = ReadRow((Regions(e, sm, st, false)?[region] as JObject)?[key]);
            if (part == null) throw new ArgumentException($"no row {sm}/{st}/{region}/{key} with a part");
            SetRow(e, sm, st, region, key, part, s.WithCurve(device, Clean(curve)));
        }

        // A curve as the table takes it (spec R2): keys at 0 to 1 in time and value, with keys at 0 and 1 (the curve's
        // values there when the editor left none).
        public static AnimationCurve Clean(AnimationCurve curve)
        {
            // The Keyframes keep their tangent modes (Unity stores them in the key).
            var c = new AnimationCurve(curve.keys);
            for (int i = 0; i < c.length; i++)
            {
                var k = c.keys[i];
                if (k.time >= 0 && k.time <= 1 && k.value >= 0 && k.value <= 1) continue;
                k.time = Mathf.Clamp01(k.time);
                k.value = Mathf.Clamp01(k.value);
                c.MoveKey(i, k);
            }
            if (c.length == 0 || c.keys[0].time > 1e-6f) c.AddKey(new Keyframe(0, CurveKeys.Value(curve, 0)));
            if (c.keys[c.length - 1].time < 1 - 1e-6f) c.AddKey(new Keyframe(1, CurveKeys.Value(curve, 1)));
            return c;
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
                case "renameStateMachine": RenameStateMachine(e, S("from"), S("to")); break;
                case "removeStateMachine": RemoveStateMachine(e, S("name")); break;
                case "addState": AddState(e, sm, S("state"), F("x", 250), F("y", 0)); break;
                case "removeState": RemoveState(e, sm, S("state")); break;
                case "renameState": RenameState(e, sm, S("from"), S("to")); break;
                case "setDefault": SetDefault(e, sm, S("state")); break;
                case "setMenu": SetMenu(e, sm, S("state"), (bool)op["menu"]); break;
                case "setStopVisemes": SetStopVisemes(e, sm, S("state"), (bool)op["stop"], S("device")); break;
                case "setPosition": SetPosition(e, sm, S("state"), F("x", 0), F("y", 0)); break;
                case "removeBrokenTransitions": RemoveBrokenTransitions(e, sm); break;
                case "addTransition": AddTransition(e, sm, S("from"), S("to"), S("input"), S("cmp") ?? ">", F("value", 0.5f), F("duration", 0.2f), S("device")); break;
                case "removeTransition": RemoveTransition(e, sm, I("index"), S("device")); break;
                case "setDuration": SetDuration(e, sm, I("index"), F("duration", 0), S("device")); break;
                case "addCondition": AddCondition(e, sm, I("index"), S("input"), S("cmp") ?? ">", F("value", 0.5f), S("device")); break;
                case "setCondition": SetCondition(e, sm, I("index"), I("cond"), S("input"), S("cmp"), F("value", 0), S("device")); break;
                case "removeCondition": RemoveCondition(e, sm, I("index"), I("cond"), S("device")); break;
                case "setRegion": SetRegion(e, sm, S("state"), S("region"), S("mode")); break;
                case "setRow": SetRow(e, sm, S("state"), S("region"), S("key"), S("part"), F("strength", 1)); break;
                case "setRowStrength": SetRowStrength(e, sm, S("state"), S("region"), S("key"), F("strength", 1), S("device")); break;
                case "setNeutralStrength": SetNeutralStrength(e, S("region"), S("key"), F("strength", 1), S("device")); break;
                case "removeRow": RemoveRow(e, sm, S("state"), S("region"), S("key")); break;
                case "copyRegion": CopyRegion(e, sm, S("state"), S("region"), S("from")); break;
                case "setFixed": SetFixed(e, sm, S("state"), S("part"), F("amount", 100)); break;
                case "removeFixed": RemoveFixed(e, sm, S("state"), S("part")); break;
                case "setNeutralRow": SetNeutralRow(e, S("region"), S("key"), S("part"), F("strength", 1)); break;
                case "removeNeutralRow": RemoveNeutralRow(e, S("region"), S("key")); break;
                // "curve": keys as the table writes them
                case "setNeutralCurve": SetNeutralCurve(e, S("region"), S("key"), CurveKeys.Parse(op["curve"], m => new ArgumentException(m)).ToAnimationCurve(), S("device")); break;
                case "setRowCurve": SetRowCurve(e, sm, S("state"), S("region"), S("key"), CurveKeys.Parse(op["curve"], m => new ArgumentException(m)).ToAnimationCurve(), S("device")); break;
                default: throw new ArgumentException("unknown op: " + (string)op["op"]);
            }
        }
    }
}
