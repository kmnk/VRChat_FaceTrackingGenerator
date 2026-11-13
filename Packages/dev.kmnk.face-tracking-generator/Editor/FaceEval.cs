using System;
using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Computes blendshape weights from the table model as the generated avatar moves them (spec R15, design.md
    // "まねる計算"), without Unity: each Blendshapes Set whose conditions hold in the given states has weight 1 (FT on,
    // not talking); a row gives key value x Multiplier x 100 and a blendshape takes the largest row; a state's fixed
    // amounts take the larger of theirs and the sets' (how VRChat combines the Animator and the sets is not known).
    public static class FaceEval
    {
        public sealed class Source
        {
            public string Part, Shape;  // Shape: "mesh/shape"
            public float Value;
            public string Key;          // the tracking key, or null for a fixed amount
            public string StateMachine, State;  // the set's or the fixed amount's state, or null for neutral
        }

        // inputs: "key:<key>" -> the key's value (other names are ignored; missing = 0). states: state machine ->
        // state (missing = default state). Returns "mesh/shape" -> weight.
        public static SortedDictionary<string, float> Evaluate(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states = null)
        {
            var result = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (var s in Sources(m, inputs, states))
                result[s.Shape] = Math.Max(result.TryGetValue(s.Shape, out var v) ? v : 0, s.Value);
            return result;
        }

        // The same before the parts become blendshapes: part -> value.
        public static SortedDictionary<string, float> EvaluateParts(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states = null)
        {
            var result = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (var s in Sources(m, inputs, states))
                result[s.Part] = Math.Max(result.TryGetValue(s.Part, out var v) ? v : 0, s.Value);
            return result;
        }

        // The state of each state machine: the given one, or the default state.
        public static Dictionary<string, string> States(TableModel m, IDictionary<string, string> states)
        {
            var r = new Dictionary<string, string>();
            foreach (var sm in m.StateMachines)
                r[sm.Name] = states != null && states.TryGetValue(sm.Name, out var s) && sm.State(s) != null ? s : sm.States[0].Name;
            return r;
        }

        // Every value that moves a blendshape, with where it comes from (the Preview tab's Source column).
        public static List<Source> Sources(TableModel m, IDictionary<string, float> inputs, IDictionary<string, string> states)
        {
            var st = States(m, states);
            float Key(string k) => inputs != null && inputs.TryGetValue(InputDef.TrackPrefix + k, out var v) ? v : 0;
            var list = new List<Source>();
            foreach (var set in NativeSets.Build(m))
            {
                var on = set.StateMachine == null
                    ? NativeSets.Takers(m, set.Region).All(x => st[x.sm.Name] != x.st.Name)
                    : st[set.StateMachine] == set.State;
                if (!on) continue;
                foreach (var r in set.Rows)
                {
                    var v = Key(r.Key) * r.Multiplier * 100;
                    if (v <= 0) continue;
                    list.Add(new Source { Part = r.Part, Shape = r.Mesh + "/" + r.Shape, Value = v, Key = r.Key, StateMachine = set.StateMachine, State = set.State });
                }
            }
            foreach (var sm in m.StateMachines)
            {
                var state = sm.State(st[sm.Name]);
                foreach (var kv in state.Fixed)
                {
                    var t = m.Parts[kv.Key];
                    if (kv.Value > 0) list.Add(new Source { Part = kv.Key, Shape = t.Mesh + "/" + t.Shape, Value = kv.Value, StateMachine = sm.Name, State = state.Name });
                }
            }
            return list;
        }

        // The range of an input for sliders: tracking keys 0 to 1; inputs from the values their transitions compare with.
        public static (float min, float max) Range(TableModel m, string input)
        {
            var vals = m.StateMachines.SelectMany(sm => sm.Transitions).SelectMany(t => t.When).Where(c => c.Input == input).Select(c => c.Value).ToList();
            float lo = Math.Min(0, vals.DefaultIfEmpty(0).Min()), hi = Math.Max(1, vals.DefaultIfEmpty(1).Max());
            return (lo, hi);
        }
    }
}
