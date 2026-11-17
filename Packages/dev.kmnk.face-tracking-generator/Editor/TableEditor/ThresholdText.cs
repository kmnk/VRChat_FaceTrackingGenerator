using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // The notes on a condition that the Devices tab and the State Machine window show (spec R16, R18): talking alone goes
    // above it, or the other side's value could not be computed.
    public static class ThresholdText
    {
        static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        public static string Value(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string L(string side) => Measurements.Label(side);

        public static List<string> Notes(Measurements ms, Condition c)
        {
            var notes = new List<string>();
            if (ms == null || !ms.HasBoth) return notes;
            var talk = Measurements.Sides.Select(s => (s, v: Thresholds.Talk(ms, c, s))).Where(x => x.v != null)
                .Select(x => L(x.s) + " " + F(x.v.Value)).ToList();
            if (talk.Count > 0)
                notes.Add(string.Format(EditorText.Get("note.talk"), string.Join(EditorText.Current == EditorText.Language.Japanese ? "・" : ", ", talk)));
            var r = Thresholds.For(ms, c, ms.OtherSide);
            if (r.Kind == Thresholds.Kind.OutOfRange && !r.Never)
                notes.Add(string.Format(EditorText.Get(r.Above ? "note.alwaysAbove" : "note.alwaysBelow"), L(ms.Main), L(ms.OtherSide), F(r.Edge), Value(r.Value.Value)));
            else if (r.Kind == Thresholds.Kind.OutOfRange)
                notes.Add(string.Format(EditorText.Get(r.Above ? "note.outAbove" : "note.outBelow"), L(ms.Main), L(ms.OtherSide), F(r.Edge), EditorText.Get(c.Op == ">" ? "note.enter" : "note.move")));
            else if (r.Kind == Thresholds.Kind.Unusable)
                notes.Add(string.Format(EditorText.Get(r.NotSent ? "note.notSent" : "note.tooLittle"), L(r.Side), L(ms.Main)));
            return notes;
        }

        // The other side's value as the State Machine window shows it: "Desktop 0.154", or "Desktop —" when it never holds.
        public static string Other(Measurements ms, Condition c)
        {
            var r = Thresholds.For(ms, c, ms.OtherSide);
            return L(ms.OtherSide) + " " + (r.Value == null ? "—" : Value(r.Value.Value));
        }
    }
}
