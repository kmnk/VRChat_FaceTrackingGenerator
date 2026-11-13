using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The states of the state machines for the preview. "Auto" follows the transitions from the current state while
    // their conditions hold, so a state machine enters and leaves at different values like the generated Animator.
    // A state machine with a pinned state (chosen in the Preview's Menu) stays there, as the menu does.
    public sealed class SwitchTracker
    {
        readonly Dictionary<string, string> current = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Pinned = new Dictionary<string, string>();

        public void Reset()
        {
            current.Clear();
            Pinned.Clear();
        }

        public string Current(string sm) => current.TryGetValue(sm, out var s) ? s : null;

        // Updates the states for the input values and returns state machine -> state for FaceEval.
        public Dictionary<string, string> Update(TableModel model, IDictionary<string, float> inputs)
        {
            float V(string name) => inputs.TryGetValue(name, out var v) ? v : 0;
            var result = new Dictionary<string, string>();
            foreach (var sm in model.StateMachines)
            {
                if (Pinned.TryGetValue(sm.Name, out var pin) && sm.State(pin) != null)
                {
                    result[sm.Name] = pin;
                    continue;
                }
                // Leaving the menu goes back to the default state (the Animator's Menu/<state> -> default).
                var state = current.TryGetValue(sm.Name, out var c) && sm.State(c) != null ? c : sm.States[0].Name;
                for (int step = 0; step < sm.States.Count; step++)
                {
                    var t = sm.Transitions.FirstOrDefault(x => x.From == state && x.When.All(w => w.Op == ">" ? V(w.Input) > w.Value : V(w.Input) < w.Value));
                    if (t == null) break;
                    state = t.To;
                }
                current[sm.Name] = state;
                result[sm.Name] = state;
            }
            foreach (var gone in current.Keys.Where(k => model.StateMachine(k) == null).ToList()) current.Remove(gone);
            return result;
        }

        // Chooses a state from the menu (null: none, back to the default state and the transitions).
        public void Choose(string sm, string state)
        {
            if (state == null) { Pinned.Remove(sm); current.Remove(sm); }
            else Pinned[sm] = state;
        }
    }
}
