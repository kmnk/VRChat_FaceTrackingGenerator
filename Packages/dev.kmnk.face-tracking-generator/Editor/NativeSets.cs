using System;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // The tracking keys of the Face Tracking section (VRCAvatarDescriptor.FaceTrackingKey), by name.
    public static class NativeKeys
    {
        static Dictionary<string, int> index;

        static Dictionary<string, int> Map()
        {
            if (index != null) return index;
            index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (VRCAvatarDescriptor.FaceTrackingKey k in Enum.GetValues(typeof(VRCAvatarDescriptor.FaceTrackingKey)))
            {
                var i = (int)k;
                // Max counts the keys and is not a key itself.
                if (i < 0 || k == VRCAvatarDescriptor.FaceTrackingKey.Max) continue;
                index[k.ToString()] = i;
            }
            return index;
        }

        // The index of a key in a Blendshapes Set's faceTrackingBlendshapeNames, or -1 for an unknown name.
        public static int Index(string key) => key != null && Map().TryGetValue(key, out var i) ? i : -1;

        public static int Count => (int)VRCAvatarDescriptor.FaceTrackingKey.Max;

        public static IEnumerable<string> Names => Map().OrderBy(kv => kv.Value).Select(kv => kv.Key);
    }

    // The Blendshapes Sets the generator writes, and when each has weight 1 (design.md "Blendshapes Set の組み立て"):
    // one per region of neutral (FTG/neutral/<region>) and one per region a state overrides
    // (FTG/<state machine>/<state>/<region>).
    public static class NativeSets
    {
        public const string Prefix = "FTG/";
        public const string NeutralName = "neutral";
        public const int MaxNameLength = 100;
        public const float MaxLerpSpeed = 100;

        public sealed class Row
        {
            public string Key, Part, Mesh, Shape;
            public float Multiplier;
        }

        // A condition on an Animator parameter: true when the value is above 0.5 (High) or below it.
        public struct Cond : IComparable<Cond>
        {
            public string Param;
            public bool High;
            public int CompareTo(Cond o)
            {
                var c = string.CompareOrdinal(Param, o.Param);
                return c != 0 ? c : High.CompareTo(o.High);
            }
            public override string ToString() => Param + (High ? " > 0.5" : " < 0.5");
        }

        public sealed class SetDef
        {
            public string Name, Region;
            public string StateMachine, State;  // null for a neutral set
            public List<Row> Rows = new List<Row>();
            public List<Cond> Conditions = new List<Cond>();
            public float LerpSpeed;
            // Conditions and lerp speed as one string: sets with the same signature share a weight layer.
            public string Signature => string.Join(" & ", Conditions.Select(c => c.ToString())) + " @" + LerpSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string SetName(string stateMachine, string state, string region) => Prefix + stateMachine + "/" + state + "/" + region;
        public static string NeutralSetName(string region) => Prefix + NeutralName + "/" + region;

        public static string Region(string part) => PartNames.Region(part);

        // The rows of a region while a state overrides it: the neutral rows, with the state's rows in place of the same
        // keys (Don't move takes the key out) and the state's other keys added.
        public static List<TrackRow> OverrideRows(TableModel m, string region, RegionDef over)
        {
            var rows = (m.Neutral.TryGetValue(region, out var n) ? n : new List<TrackRow>()).ToList();
            foreach (var r in over.Rows)
            {
                var i = rows.FindIndex(x => x.Key == r.Key);
                if (r.Part == null) { if (i >= 0) rows.RemoveAt(i); continue; }
                if (i >= 0) rows[i] = r; else rows.Add(r);
            }
            return rows;
        }

        // The states (state machine, state) that take a region off its neutral set: Override or Off.
        public static IEnumerable<(StateMachineDef sm, StateDef st)> Takers(TableModel m, string region) =>
            m.StateMachines.SelectMany(sm => sm.States.Where(st => st.Regions.ContainsKey(region)).Select(st => (sm, st)));

        public static List<SetDef> Build(TableModel m)
        {
            var sets = new List<SetDef>();
            var talk = new HashSet<string>(m.StopWhileTalking);
            Cond Gate(string region) => new Cond { Param = talk.Contains(region) ? FtBuilder.MouthActive : FtBuilder.Active, High = true };

            SetDef Make(string name, string region, IEnumerable<TrackRow> rows)
            {
                var set = new SetDef { Name = name, Region = region };
                foreach (var r in rows)
                {
                    if (r.Part == null || !m.Parts.TryGetValue(r.Part, out var target)) continue;
                    set.Rows.Add(new Row { Key = r.Key, Part = r.Part, Mesh = target.Mesh, Shape = target.Shape, Multiplier = r.Strength * target.Scale });
                }
                return set;
            }

            float Lerp(IEnumerable<StateMachineDef> sms)
            {
                var dur = sms.Select(x => x.MaxDuration).DefaultIfEmpty(0).Max();
                return dur > 0 ? Math.Min(MaxLerpSpeed, 1f / dur) : 0f;
            }

            foreach (var kv in m.Neutral)
            {
                var set = Make(NeutralSetName(kv.Key), kv.Key, kv.Value);
                if (set.Rows.Count == 0) continue;
                var conds = new SortedSet<Cond> { Gate(kv.Key) };
                var takers = Takers(m, kv.Key).ToList();
                foreach (var (sm, st) in takers) conds.Add(new Cond { Param = FtBuilder.SwitchParam(sm.Name, st.Name), High = false });
                set.Conditions = conds.ToList();
                set.LerpSpeed = Lerp(takers.Select(x => x.sm).Distinct());
                sets.Add(set);
            }

            foreach (var sm in m.StateMachines)
                foreach (var st in sm.States)
                    foreach (var kv in st.Regions.Where(x => !x.Value.Off))
                    {
                        var set = Make(SetName(sm.Name, st.Name, kv.Key), kv.Key, OverrideRows(m, kv.Key, kv.Value));
                        if (set.Rows.Count == 0) continue;
                        set.StateMachine = sm.Name;
                        set.State = st.Name;
                        set.Conditions = new SortedSet<Cond> { Gate(kv.Key), new Cond { Param = FtBuilder.SwitchParam(sm.Name, st.Name), High = true } }.ToList();
                        set.LerpSpeed = Lerp(new[] { sm });
                        sets.Add(set);
                    }
            return sets;
        }
    }
}
