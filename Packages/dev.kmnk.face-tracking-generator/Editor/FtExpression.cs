using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRC.SDK3.Avatars.Components;

namespace Kmnk.FaceTrackingGenerator
{
    // A small reader of the Custom expressions of VRChat's Face Tracking parameter behaviors: numbers, names,
    // + - * /, unary -, parentheses, abs / min / max / clamp. It shows mistakes early, lists the tracking keys an
    // expression uses, and gives the range of its value from ranges of the keys. Whether VRChat accepts an
    // expression (length, tokens, instructions) is left to the SDK's FaceTrackingExpressionValidator.
    public static class FtExpression
    {
        public sealed class ParseException : Exception
        {
            public ParseException(string message) : base(message) { }
        }

        // Names an expression may use: the tracking keys, Voice and the viseme inputs, as the SDK's validator lists them.
        static HashSet<string> keys, names;

        public static IReadOnlyCollection<string> Keys { get { Init(); return keys; } }
        public static bool IsKey(string name) { Init(); return keys.Contains(name); }
        public static bool IsName(string name) { Init(); return names.Contains(name); }

        static void Init()
        {
            if (keys != null) return;
            keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (int)VRCAvatarDescriptor.FaceTrackingKey.Max; i++)
                keys.Add(((VRCAvatarDescriptor.FaceTrackingKey)i).ToString());
            names = new HashSet<string>(keys, StringComparer.Ordinal) { "Voice" };
            for (int i = 0; i < (int)VRCAvatarDescriptor.Viseme.Count; i++)
                names.Add(VRCAvatarDescriptor.GetFaceTrackingVisemeInputName((VRCAvatarDescriptor.Viseme)i));
        }

        public sealed class Expr
        {
            internal Node Root;
            public string Text;
            // The names the expression uses, in the order they first appear.
            public List<string> Names = new List<string>();

            // The range of the value when each name is in the range `of` gives (null: the name is always 0).
            // The result is clamped to -1..1 as Forward to Animator does. Names that appear twice are treated as
            // independent, so the range can be wider than the real one.
            public (float lo, float hi) Range(Func<string, (float lo, float hi)?> of)
            {
                var (lo, hi) = Root.Range(of);
                return (Clamp(lo), Clamp(hi));
            }

            public float Evaluate(Func<string, float> of) => Math.Max(-1f, Math.Min(1f, Root.Eval(of)));

            static float Clamp(float v) => float.IsNaN(v) ? 0 : Math.Max(-1f, Math.Min(1f, v));
        }

        public static Expr Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ParseException("the expression is empty");
            var p = new Parser(text);
            var root = p.Sum();
            if (!p.AtEnd) throw new ParseException($"unexpected '{p.Rest}' in \"{text}\"");
            var e = new Expr { Root = root, Text = text };
            root.CollectNames(e.Names);
            Init();
            var unknown = e.Names.Where(n => !names.Contains(n)).ToList();
            if (unknown.Count > 0) throw new ParseException($"unknown name {string.Join(", ", unknown)} in \"{text}\" (use tracking keys, Voice or Viseme_*)");
            return e;
        }

        // ---- nodes

        internal abstract class Node
        {
            public abstract (float lo, float hi) Range(Func<string, (float, float)?> of);
            public abstract float Eval(Func<string, float> of);
            public virtual void CollectNames(List<string> into) { }
        }

        sealed class Num : Node
        {
            public float V;
            public override (float, float) Range(Func<string, (float, float)?> of) => (V, V);
            public override float Eval(Func<string, float> of) => V;
        }

        sealed class Name : Node
        {
            public string N;
            public override (float, float) Range(Func<string, (float, float)?> of) => of(N) ?? (0f, 0f);
            public override float Eval(Func<string, float> of) => of(N);
            public override void CollectNames(List<string> into) { if (!into.Contains(N)) into.Add(N); }
        }

        sealed class Neg : Node
        {
            public Node A;
            public override (float, float) Range(Func<string, (float, float)?> of) { var (lo, hi) = A.Range(of); return (-hi, -lo); }
            public override float Eval(Func<string, float> of) => -A.Eval(of);
            public override void CollectNames(List<string> into) => A.CollectNames(into);
        }

        sealed class Bin : Node
        {
            public char Op;
            public Node A, B;
            public override (float, float) Range(Func<string, (float, float)?> of)
            {
                var (a, b) = A.Range(of);
                var (c, d) = B.Range(of);
                switch (Op)
                {
                    case '+': return (a + c, b + d);
                    case '-': return (a - d, b - c);
                    case '*': { var v = new[] { a * c, a * d, b * c, b * d }; return (v.Min(), v.Max()); }
                    default:
                        if (c <= 0 && d >= 0) return (-1f, 1f);  // the divisor can be 0: anything after the clamp
                        { var v = new[] { a / c, a / d, b / c, b / d }; return (v.Min(), v.Max()); }
                }
            }
            public override float Eval(Func<string, float> of)
            {
                var x = A.Eval(of); var y = B.Eval(of);
                switch (Op) { case '+': return x + y; case '-': return x - y; case '*': return x * y; default: return y == 0 ? 0 : x / y; }
            }
            public override void CollectNames(List<string> into) { A.CollectNames(into); B.CollectNames(into); }
        }

        sealed class Call : Node
        {
            public string F;
            public List<Node> Args;
            public override (float, float) Range(Func<string, (float, float)?> of)
            {
                var r = Args.Select(a => a.Range(of)).ToList();
                switch (F)
                {
                    case "abs":
                        var (lo, hi) = r[0];
                        return lo >= 0 ? (lo, hi) : hi <= 0 ? (-hi, -lo) : (0f, Math.Max(-lo, hi));
                    case "min": return (r.Min(x => x.Item1), r.Min(x => x.Item2));
                    case "max": return (r.Max(x => x.Item1), r.Max(x => x.Item2));
                    default:  // clamp(x, lo, hi)
                        return (Math.Min(Math.Max(r[0].Item1, r[1].Item1), r[2].Item2), Math.Min(Math.Max(r[0].Item2, r[1].Item1), r[2].Item2));
                }
            }
            public override float Eval(Func<string, float> of)
            {
                var v = Args.Select(a => a.Eval(of)).ToList();
                switch (F)
                {
                    case "abs": return Math.Abs(v[0]);
                    case "min": return v.Min();
                    case "max": return v.Max();
                    default: return Math.Min(Math.Max(v[0], v[1]), v[2]);
                }
            }
            public override void CollectNames(List<string> into) { foreach (var a in Args) a.CollectNames(into); }
        }

        // ---- parser: sum := product (('+'|'-') product)*, product := unary (('*'|'/') unary)*,
        //      unary := '-' unary | atom, atom := number | name | name '(' sum (',' sum)* ')' | '(' sum ')'

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; }
            public bool AtEnd { get { Skip(); return i >= s.Length; } }
            public string Rest => s.Substring(i);

            void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            bool Eat(char c) { Skip(); if (i < s.Length && s[i] == c) { i++; return true; } return false; }

            public Node Sum()
            {
                var n = Product();
                while (true)
                {
                    if (Eat('+')) n = new Bin { Op = '+', A = n, B = Product() };
                    else if (Eat('-')) n = new Bin { Op = '-', A = n, B = Product() };
                    else return n;
                }
            }

            Node Product()
            {
                var n = Unary();
                while (true)
                {
                    if (Eat('*')) n = new Bin { Op = '*', A = n, B = Unary() };
                    else if (Eat('/')) n = new Bin { Op = '/', A = n, B = Unary() };
                    else return n;
                }
            }

            Node Unary() => Eat('-') ? new Neg { A = Unary() } : Atom();

            Node Atom()
            {
                Skip();
                if (i >= s.Length) throw new ParseException($"the expression \"{s}\" ends too early");
                if (Eat('('))
                {
                    var n = Sum();
                    if (!Eat(')')) throw new ParseException($"')' is missing in \"{s}\"");
                    return n;
                }
                var c = s[i];
                if (char.IsDigit(c) || c == '.')
                {
                    var start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (!float.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ParseException($"'{s.Substring(start, i - start)}' is not a number in \"{s}\"");
                    return new Num { V = v };
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    var word = s.Substring(start, i - start);
                    if (!Eat('(')) return new Name { N = word };
                    var argc = word == "abs" ? 1 : word == "clamp" ? 3 : word == "min" || word == "max" ? -2 : 0;
                    if (argc == 0) throw new ParseException($"unknown function '{word}' in \"{s}\" (use abs, min, max or clamp)");
                    var args = new List<Node> { Sum() };
                    while (Eat(',')) args.Add(Sum());
                    if (!Eat(')')) throw new ParseException($"')' is missing in \"{s}\"");
                    if (argc > 0 ? args.Count != argc : args.Count < 2)
                        throw new ParseException($"'{word}' takes {(argc > 0 ? argc.ToString() : "two or more")} arguments in \"{s}\"");
                    return new Call { F = word, Args = args };
                }
                throw new ParseException($"unexpected '{c}' in \"{s}\"");
            }
        }
    }
}
