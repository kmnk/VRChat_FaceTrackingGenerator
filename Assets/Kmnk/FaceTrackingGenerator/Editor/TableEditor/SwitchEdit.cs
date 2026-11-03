using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Switches without drawing: the definitions in "switches" (states, transitions with conditions) and the switch boxes
    // of the trees. A switch is referred to as a box writes it: "EyelidSmileLeft", or "EyelidSmile{side}" in a left/right
    // tree. A left/right reference edits the definition written with "sides", or the two definitions written per side.
    public static class SwitchEdit
    {
        static JObject Switches(JObject expression, bool create = false)
        {
            if (expression["switches"] is JObject s) return s;
            if (!create) return null;
            expression["switches"] = s = new JObject();
            return s;
        }

        static IEnumerable<string> Sides(string reference) =>
            reference.Contains("{side}") ? new[] { "Left", "Right" } : new string[0];

        // The definitions a reference edits, with the side each one is for (null for one written once).
        public static List<(JObject def, string side)> Defs(JObject expression, string reference)
        {
            var result = new List<(JObject, string)>();
            var sws = Switches(expression);
            if (sws == null || reference == null) return result;
            if (sws[reference] is JObject one) { result.Add((one, null)); return result; }
            foreach (var side in Sides(reference))
                if (sws[reference.Replace("{side}", side)] is JObject d) result.Add((d, side));
            return result;
        }

        public static List<string> States(JObject expression, string reference)
        {
            var d = Defs(expression, reference).FirstOrDefault().def;
            return d?["states"] is JArray a ? a.Select(x => (string)x).ToList() : new List<string>();
        }

        // Switches a box of the tree can use: in a left/right tree, those written with "sides" or per side (as "X{side}");
        // otherwise those without "{".
        public static List<string> Choices(JObject expression, string tree, bool sided = false)
        {
            var hasSides = sided || (tree != null && expression?["trees"]?[tree]?["sides"] != null);
            var names = Switches(expression)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
            var set = new HashSet<string>(names);
            var result = new List<string>();
            foreach (var n in names)
            {
                if (!hasSides) { if (!n.Contains("{")) result.Add(n); continue; }
                if (n.Contains("{")) { result.Add(n); continue; }
                if (n.Contains("Left") && set.Contains(n.Replace("Left", "Right"))) result.Add(n.Replace("Left", "{side}"));
            }
            return result.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        // ---- new switches and switch boxes

        static bool Taken(JObject expression, string name)
        {
            var sws = Switches(expression);
            if (sws == null) return false;
            foreach (var p in sws.Properties())
            {
                if (p.Name == name || p.Name == name + "{side}" || p.Name == name + "Left" || p.Name == name + "Right") return true;
                if (p.Name.Contains("{side}") && (p.Name.Replace("{side}", "Left") == name || p.Name.Replace("{side}", "Right") == name)) return true;
            }
            return false;
        }

        // A switch named Switch1, Switch2, … (left/right: "SwitchN{side}" with "sides") with states state1 and state2 and two
        // transitions on the input: state1 -> state2 when input > 0.5, back when input < 0.4. Returns the reference.
        public static string NewSwitch(JObject expression, bool sided, string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var k = 1;
            while (Taken(expression, "Switch" + k)) k++;
            var name = "Switch" + k + (sided ? "{side}" : "");
            var def = new JObject();
            if (sided) def["sides"] = new JArray("Left", "Right");
            def["states"] = new JArray("state1", "state2");
            def["transitions"] = new JArray(Transition("state1", "state2", input, ">", 0.5), Transition("state2", "state1", input, "<", 0.4));
            Switches(expression, true).Add(name, def);
            return name;
        }

        static JObject Transition(string from, string to, string input, string op, double value) => new JObject
        {
            ["from"] = from, ["to"] = to, ["when"] = new JArray((object)new JArray(input, op, Tuning.Num(value))), ["duration"] = 0,  // (object): new JArray(JArray) would copy the items
        };

        // A switch box: every state's case is a copy of "content", or with "keepFirst", the first state gets "content"
        // and the others neutral.
        public static JObject Box(JObject expression, string reference, JToken content, bool keepFirst)
        {
            var states = States(expression, reference);
            if (states.Count < 2) return null;
            var cases = new JObject();
            for (int i = 0; i < states.Count; i++)
                cases[states[i]] = keepFirst && i > 0 ? new JObject() : (content == null || content.Type == JTokenType.Null ? new JObject() : content.DeepClone());
            return new JObject { ["switch"] = reference, ["cases"] = cases };
        }

        // ---- the boxes that use a switch

        static IEnumerable<JObject> Nodes(JToken t)
        {
            if (!(t is JObject o)) yield break;
            if (o["switch"] != null) yield return o;
            if (o["children"] is JArray ch)
                foreach (var c in ch.OfType<JArray>().Where(c => c.Count == 2)) foreach (var n in Nodes(c[1])) yield return n;
            if (o["cases"] is JObject cases)
                foreach (var p in cases.Properties()) foreach (var n in Nodes(p.Value)) yield return n;
        }

        static HashSet<string> Names(string reference)
        {
            var names = new HashSet<string> { reference };
            foreach (var side in Sides(reference)) names.Add(reference.Replace("{side}", side));
            return names;
        }

        static IEnumerable<(string tree, JObject node)> Boxes(JObject expression, string reference)
        {
            var names = Names(reference);
            if (!(expression["trees"] is JObject trees)) yield break;
            foreach (var p in trees.Properties())
                foreach (var n in Nodes(p.Value?["tree"]))
                    if (names.Contains((string)n["switch"])) yield return (p.Name, n);
        }

        // The expressions that use the switch (names without side marks).
        public static List<string> Users(JObject expression, string reference) =>
            Boxes(expression, reference).Select(b => Tuning.Label(b.tree)).Distinct().ToList();

        // ---- edits

        public static bool CanRename(JObject expression, string reference, string name) =>
            !string.IsNullOrWhiteSpace(name) && !name.Contains("{") && !Taken(expression, name.Trim());

        public static bool Rename(JObject expression, string reference, string name)
        {
            if (!CanRename(expression, reference, name)) return false;
            name = name.Trim();
            var sws = Switches(expression);
            foreach (var (def, side) in Defs(expression, reference))
            {
                var prop = sws.Properties().First(p => ReferenceEquals(p.Value, def));
                var newKey = side != null ? name + side : name + (reference.Contains("{side}") ? "{side}" : "");
                prop.AddBeforeSelf(new JProperty(newKey, def.DeepClone()));
                prop.Remove();
            }
            var map = new Dictionary<string, string> { [reference] = name + (reference.Contains("{side}") ? "{side}" : "") };
            foreach (var side in Sides(reference)) map[reference.Replace("{side}", side)] = name + side;
            foreach (var (_, node) in Boxes(expression, reference).ToList()) node["switch"] = map[(string)node["switch"]];
            return true;
        }

        public static bool RenameState(JObject expression, string reference, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(to) || States(expression, reference).Contains(to.Trim()) || !States(expression, reference).Contains(from)) return false;
            to = to.Trim();
            foreach (var (def, _) in Defs(expression, reference))
            {
                var states = (JArray)def["states"];
                for (int i = 0; i < states.Count; i++) if ((string)states[i] == from) states[i] = to;
                foreach (var t in (def["transitions"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if ((string)t["from"] == from) t["from"] = to;
                    if ((string)t["to"] == from) t["to"] = to;
                }
            }
            foreach (var (_, node) in Boxes(expression, reference).ToList())
                if (node["cases"] is JObject cases && cases[from] != null)
                {
                    var p = cases.Property(from);
                    p.AddBeforeSelf(new JProperty(to, p.Value));
                    p.Remove();
                }
            return true;
        }

        public static string AddState(JObject expression, string reference)
        {
            var states = States(expression, reference);
            if (states.Count == 0) return null;
            var k = states.Count + 1;
            while (states.Contains("state" + k)) k++;
            var name = "state" + k;
            foreach (var (def, _) in Defs(expression, reference)) ((JArray)def["states"]).Add(name);
            foreach (var (_, node) in Boxes(expression, reference).ToList())
                if (node["cases"] is JObject cases) cases[name] = new JObject();
            return name;
        }

        public static bool RemoveState(JObject expression, string reference, string state)
        {
            var states = States(expression, reference);
            if (states.Count <= 2 || !states.Contains(state)) return false;
            foreach (var (def, _) in Defs(expression, reference))
            {
                var arr = (JArray)def["states"];
                foreach (var s in arr.Where(x => (string)x == state).ToList()) s.Remove();
                if (def["transitions"] is JArray ts)
                    foreach (var t in ts.OfType<JObject>().Where(t => (string)t["from"] == state || (string)t["to"] == state).ToList()) t.Remove();
            }
            foreach (var (_, node) in Boxes(expression, reference).ToList()) (node["cases"] as JObject)?.Remove(state);
            return true;
        }

        static IEnumerable<(JArray transitions, string side)> Transitions(JObject expression, string reference)
        {
            foreach (var (def, side) in Defs(expression, reference))
            {
                if (!(def["transitions"] is JArray ts)) def["transitions"] = ts = new JArray();
                yield return (ts, side);
            }
        }

        // An input written for a definition: "EyeLid{side}" becomes "EyeLidLeft" in the definition for the left side.
        static string For(string input, string side) => side != null ? input.Replace("{side}", side) : input;

        public static void AddTransition(JObject expression, string reference, string input)
        {
            var states = States(expression, reference);
            if (states.Count < 2 || string.IsNullOrEmpty(input)) return;
            foreach (var (ts, side) in Transitions(expression, reference).ToList())
                ts.Add(Transition(states[0], states[1], For(input, side), ">", 0.5));
        }

        public static void RemoveTransition(JObject expression, string reference, int index)
        {
            foreach (var (ts, _) in Transitions(expression, reference).ToList())
                if (index >= 0 && index < ts.Count) ts[index].Remove();
        }

        public static void SetTransition(JObject expression, string reference, int index, string from, string to, double duration)
        {
            foreach (var (ts, _) in Transitions(expression, reference).ToList())
                if (index >= 0 && index < ts.Count && ts[index] is JObject t)
                {
                    t["from"] = from; t["to"] = to; t["duration"] = Tuning.Num(duration);
                }
        }

        public static void AddCondition(JObject expression, string reference, int index, string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            foreach (var (ts, side) in Transitions(expression, reference).ToList())
                if (index >= 0 && index < ts.Count && ts[index] is JObject t)
                {
                    if (!(t["when"] is JArray w)) t["when"] = w = new JArray();
                    w.Add(new JArray(For(input, side), ">", Tuning.Num(0.5)));
                }
        }

        // A transition keeps at least one condition (the table needs one).
        public static bool RemoveCondition(JObject expression, string reference, int index, int condition)
        {
            var done = false;
            foreach (var (ts, _) in Transitions(expression, reference).ToList())
                if (index >= 0 && index < ts.Count && ts[index]["when"] is JArray w && w.Count > 1 && condition >= 0 && condition < w.Count)
                {
                    w[condition].Remove();
                    done = true;
                }
            return done;
        }

        public static void SetCondition(JObject expression, string reference, int index, int condition, string input, string op, double value)
        {
            foreach (var (ts, side) in Transitions(expression, reference).ToList())
                if (index >= 0 && index < ts.Count && ts[index]["when"] is JArray w && condition >= 0 && condition < w.Count)
                    w[condition] = new JArray(For(input, side), op == "<" ? "<" : ">", Tuning.Num(value));
        }

        // The transitions as the first definition writes them, with inputs in the reference's form ("EyeLid{side}").
        public sealed class TransitionView
        {
            public string From, To;
            public double Duration;
            public List<(string input, string op, double value)> When = new List<(string, string, double)>();
        }

        public static List<TransitionView> View(JObject expression, string reference)
        {
            var (def, side) = Defs(expression, reference).FirstOrDefault();
            var result = new List<TransitionView>();
            if (def == null) return result;
            foreach (var t in (def["transitions"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var v = new TransitionView { From = (string)t["from"], To = (string)t["to"], Duration = t["duration"]?.Value<double>() ?? 0 };
                foreach (var c in (t["when"] as JArray ?? new JArray()).OfType<JArray>().Where(c => c.Count == 3))
                {
                    var input = (string)c[0];
                    if (side != null) input = input.Replace(side, "{side}");
                    v.When.Add((input, (string)c[1], c[2].Value<double>()));
                }
                result.Add(v);
            }
            return result;
        }
    }
}
