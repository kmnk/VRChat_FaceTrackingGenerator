using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The states of the switches for the preview. "Auto" follows the transitions from the current state while their
    // conditions hold, so a switch enters and leaves at different values like the generated Animator. A switch with
    // a pinned state stays there.
    public sealed class SwitchTracker
    {
        readonly Dictionary<string, string> current = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Pinned = new Dictionary<string, string>();

        public void Reset()
        {
            current.Clear();
            Pinned.Clear();
        }

        public string Current(string sw) => current.TryGetValue(sw, out var s) ? s : null;

        // Updates the states for the input values and returns switch name -> state for CurveEval.
        public Dictionary<string, string> Update(TableModel model, IDictionary<string, float> inputs)
        {
            var values = new Dictionary<string, float>(inputs);
            foreach (var d in model.Inputs.Where(i => i.IsDerived))
                values[d.Name] = d.Average.Average(s => values.TryGetValue(s, out var v) ? v : 0);
            float V(string name) => values.TryGetValue(name, out var v) ? v : 0;

            var result = new Dictionary<string, string>();
            foreach (var sw in model.Switches)
            {
                if (Pinned.TryGetValue(sw.Name, out var pin) && sw.States.Contains(pin))
                {
                    result[sw.Name] = pin;
                    continue;
                }
                var state = current.TryGetValue(sw.Name, out var c) && sw.States.Contains(c) ? c : sw.States[0];
                for (int step = 0; step < sw.States.Count; step++)
                {
                    var t = sw.Transitions.FirstOrDefault(x => x.From == state && x.When.All(w => w.Op == ">" ? V(w.Input) > w.Value : V(w.Input) < w.Value));
                    if (t == null) break;
                    state = t.To;
                }
                current[sw.Name] = state;
                result[sw.Name] = state;
            }
            foreach (var gone in current.Keys.Where(k => model.Switch(k) == null).ToList()) current.Remove(gone);
            return result;
        }
    }
}
