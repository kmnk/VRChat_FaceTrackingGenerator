using System;
using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The value a condition compares on a side (spec R9, design.md "しきい値"). The tables are written for the main
    // side; with recordings for both sides, the other side's value is the table value moved with the recordings.
    public static class Thresholds
    {
        public enum Kind
        {
            Table,      // the table value: the main side, or recordings for both sides are missing
            Computed,   // moved from the main side with the recordings
            Unusable,   // the key is not sent, or moves too little, on a side: the table value
            OutOfRange, // the table value is outside the main side's range: when the condition never holds on the main
                        // side, it never holds on the side either (Never; Value 1 for ">", 0 for "<", the ends of the
                        // inputs), else it always holds (Value: as far outside the side's range as the table value is
                        // outside the main side's)
        }

        public struct Result
        {
            public Kind Kind;
            public float? Value;
            public bool Never;          // OutOfRange: the condition never holds on the side
            public string Side;         // Unusable: the side that cannot use the key
            public bool NotSent;        // Unusable: not sent (else it moves less than Measurements.MinUsable)
            public bool Above;          // OutOfRange: above the main side's largest value (else below its neutral)
            public float Edge;          // OutOfRange: the main side's largest value (Above) or neutral value
        }

        const float Eps = 1e-4f;

        public static Result For(Measurements ms, Condition c, string side)
        {
            if (ms == null || !ms.HasBoth || side == ms.Main) return new Result { Kind = Kind.Table, Value = c.Value };
            foreach (var s in new[] { ms.Of(ms.Main), ms.Of(side) })
                if (s.Usable(c.Input) == null)
                    return new Result { Kind = Kind.Unusable, Value = c.Value, Side = s.Name, NotSent = !s.Key(c.Input).Sent };
            var main = ms.Of(ms.Main).Key(c.Input);
            var other = ms.Of(side).Key(c.Input);
            if (c.Value > main.Max + Eps)
                return new Result { Kind = Kind.OutOfRange, Above = true, Edge = main.Max,
                    Never = c.Op == ">", Value = c.Op == ">" ? 1f : (float)Math.Round(other.Max + (c.Value - main.Max), 3) };
            if (c.Value < main.Neutral - Eps)
                return new Result { Kind = Kind.OutOfRange, Above = false, Edge = main.Neutral,
                    Never = c.Op == "<", Value = c.Op == "<" ? 0f : (float)Math.Round(other.Neutral - (main.Neutral - c.Value), 3) };
            var (v, _) = ms.Remap(c.Input, c.Value, ms.Main, side);
            return new Result { Kind = Kind.Computed, Value = v };
        }

        // The values of a transition's conditions on a side, or null when it never happens there (spec R9).
        public static List<float> Values(Measurements ms, TransitionDef t, string side)
        {
            var list = new List<float>();
            foreach (var c in t.When)
            {
                var r = For(ms, c, side);
                if (r.Value == null) return null;
                list.Add(r.Value.Value);
            }
            return list;
        }

        // Whether the transition is made once per side (spec R10): the other side compares other values, or never happens.
        public static bool Splits(Measurements ms, TransitionDef t)
        {
            if (ms == null || !ms.HasBoth) return false;
            var other = Values(ms, t, ms.OtherSide);
            return other == null || other.Where((v, i) => v != t.When[i].Value).Any();
        }

        // The largest value of reading with a neutral face on a side, when it is above the side's value of a ">"
        // condition (spec R16); null otherwise.
        public static float? Talk(Measurements ms, Condition c, string side)
        {
            if (c.Op != ">" || ms?.Of(side) == null) return null;
            var read = ms.Of(side).Key(c.Input).ReadMax;
            var r = For(ms, c, side);
            return read != null && !r.Never && r.Value != null && read.Value > r.Value.Value ? read : null;
        }
    }
}
