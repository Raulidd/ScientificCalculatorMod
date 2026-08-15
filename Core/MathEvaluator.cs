using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScientificCalculatorMod.Core
{
    /// <summary>
    /// Evaluation context: holds the "x" variable (used when graphing),
    /// the last result ("Ans"), memory (M) and the angle mode.
    /// </summary>
    public class CalcContext
    {
        public double X;
        public double Ans;
        public double Memory;
        public bool DegreeMode = true; // true = degrees (Deg), false = radians (Rad)

        // Values of the graph's Y1/Y2/Y3 functions at the current X, evaluated
        // in order so e.g. Y3 can reference Y1 and Y2 ("y1 + y2").
        public double Y1;
        public double Y2;
        public double Y3;
    }

    public class MathException : Exception
    {
        public MathException(string message) : base(message) { }
    }

    internal enum TokKind { Num, Ident, Plus, Minus, Star, Slash, Percent, Caret, Bang, LParen, RParen, Comma, End }

    internal struct Tok
    {
        public TokKind Kind;
        public string Text;
        public double Num;
    }

    /// <summary>
    /// A node in an evaluable expression tree. Compiled once and can be
    /// re-evaluated thousands of times (used by graph mode to sample f(x)).
    /// </summary>
    internal abstract class Node
    {
        public abstract double Eval(CalcContext ctx);
    }

    internal class NumberNode : Node
    {
        public double Value;
        public override double Eval(CalcContext ctx) => Value;
    }

    internal class VarNode : Node
    {
        public string Name;
        public override double Eval(CalcContext ctx)
        {
            switch (Name)
            {
                case "x": return ctx.X;
                case "pi": return Math.PI;
                case "e": return Math.E;
                case "ans": return ctx.Ans;
                case "m": return ctx.Memory;
                case "y1": return ctx.Y1;
                case "y2": return ctx.Y2;
                case "y3": return ctx.Y3;
                default: throw new MathException("Unknown variable: " + Name);
            }
        }
    }

    internal class UnaryNode : Node
    {
        public char Op;
        public Node Operand;
        public override double Eval(CalcContext ctx)
        {
            double v = Operand.Eval(ctx);
            return Op == '-' ? -v : v;
        }
    }

    internal class BinaryNode : Node
    {
        public char Op;
        public Node Left, Right;
        public override double Eval(CalcContext ctx)
        {
            double a = Left.Eval(ctx);
            double b = Right.Eval(ctx);
            switch (Op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                case '/':
                    if (b == 0) throw new MathException("Division by zero");
                    return a / b;
                case '%':
                    if (b == 0) throw new MathException("Division by zero");
                    return a % b;
                case '^': return Math.Pow(a, b);
                default: throw new MathException("Unknown operator");
            }
        }
    }

    internal class FactorialNode : Node
    {
        public Node Operand;
        public override double Eval(CalcContext ctx)
        {
            double v = Operand.Eval(ctx);
            if (v < 0 || Math.Abs(v - Math.Round(v)) > 1e-9 || v > 170)
                throw new MathException("x! domain error");
            int n = (int)Math.Round(v);
            double r = 1;
            for (int i = 2; i <= n; i++) r *= i;
            return r;
        }
    }

    internal class FuncNode : Node
    {
        public string Name;
        public Node[] Args;

        public override double Eval(CalcContext ctx)
        {
            double[] a = new double[Args.Length];
            for (int i = 0; i < Args.Length; i++) a[i] = Args[i].Eval(ctx);

            double ToRad(double v) => ctx.DegreeMode ? v * Math.PI / 180.0 : v;
            double ToOut(double v) => ctx.DegreeMode ? v * 180.0 / Math.PI : v;

            switch (Name)
            {
                case "sin": Require(1); return Math.Sin(ToRad(a[0]));
                case "cos": Require(1); return Math.Cos(ToRad(a[0]));
                case "tan": Require(1); return Math.Tan(ToRad(a[0]));
                case "asin": Require(1); return ToOut(Math.Asin(a[0]));
                case "acos": Require(1); return ToOut(Math.Acos(a[0]));
                case "atan": Require(1); return ToOut(Math.Atan(a[0]));
                case "sinh": Require(1); return Math.Sinh(a[0]);
                case "cosh": Require(1); return Math.Cosh(a[0]);
                case "tanh": Require(1); return Math.Tanh(a[0]);
                case "asinh": Require(1); return Math.Log(a[0] + Math.Sqrt(a[0] * a[0] + 1));
                case "acosh": Require(1); return Math.Log(a[0] + Math.Sqrt(a[0] * a[0] - 1));
                case "atanh": Require(1); return 0.5 * Math.Log((1 + a[0]) / (1 - a[0]));
                case "sqrt": Require(1); if (a[0] < 0) throw new MathException("sqrt domain error"); return Math.Sqrt(a[0]);
                case "cbrt": Require(1); return Math.Sign(a[0]) * Math.Pow(Math.Abs(a[0]), 1.0 / 3.0);
                case "ln": Require(1); if (a[0] <= 0) throw new MathException("ln domain error"); return Math.Log(a[0]);
                case "log":
                    if (Args.Length == 1) { if (a[0] <= 0) throw new MathException("log domain error"); return Math.Log10(a[0]); }
                    Require(2); return Math.Log(a[1], a[0]);
                case "exp": Require(1); return Math.Exp(a[0]);
                case "abs": Require(1); return Math.Abs(a[0]);
                case "floor": Require(1); return Math.Floor(a[0]);
                case "ceil": Require(1); return Math.Ceiling(a[0]);
                case "round": Require(1); return Math.Round(a[0]);
                case "sign": Require(1); return Math.Sign(a[0]);
                case "min": Require(2); return Math.Min(a[0], a[1]);
                case "max": Require(2); return Math.Max(a[0], a[1]);
                case "gcd": Require(2); return Gcd((long)a[0], (long)a[1]);
                case "lcm": Require(2); { long g = Gcd((long)a[0], (long)a[1]); return g == 0 ? 0 : Math.Abs(a[0] * a[1]) / g; }
                case "npr": Require(2); return Perm(a[0], a[1]);
                case "ncr": Require(2); return Comb(a[0], a[1]);
                case "pow": Require(2); return Math.Pow(a[0], a[1]);
                default: throw new MathException("Unknown function: " + Name);
            }

            void Require(int n) { if (Args.Length != n) throw new MathException(Name + " requires " + n + " argument(s)"); }
        }

        private static long Gcd(long a, long b) { a = Math.Abs(a); b = Math.Abs(b); while (b != 0) { long t = b; b = a % b; a = t; } return a; }
        private static double Perm(double n, double r) { if (r > n || r < 0) throw new MathException("nPr domain error"); double res = 1; for (int i = 0; i < (int)r; i++) res *= (n - i); return res; }
        private static double Comb(double n, double r) { if (r > n || r < 0) throw new MathException("nCr domain error"); return Perm(n, r) / Factorial((int)r); }
        private static double Factorial(int n) { double r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }
    }

    /// <summary>
    /// Tokenizes and parses a math expression written as text (calculator-style,
    /// including implicit multiplication: "2x", "2(3+1)", "3sin(30)") and returns a
    /// reusable Node tree.
    /// </summary>
    public class MathEvaluator
    {
        private List<Tok> _toks;
        private int _pos;

        /// <summary>Evaluates an expression once.</summary>
        public static double Evaluate(string expr, CalcContext ctx)
        {
            Node node = Compile(expr);
            double v = node.Eval(ctx);
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new MathException("Math error");
            return v;
        }

        /// <summary>Compiles the expression into a reusable tree (fast to re-evaluate, useful for graphing).</summary>
        public static Func<double, CalcContext, double> CompileForX(string expr)
        {
            Node node = Compile(expr);
            return (x, ctx) =>
            {
                ctx.X = x;
                return node.Eval(ctx);
            };
        }

        internal static Node Compile(string expr)
        {
            var ev = new MathEvaluator();
            ev._toks = Tokenize(expr);
            ev._pos = 0;
            Node n = ev.ParseAddSub();
            if (ev.Peek().Kind != TokKind.End) throw new MathException("Syntax error");
            return n;
        }

        // ---- Tokenizer ----
        private static List<Tok> Tokenize(string s)
        {
            var list = new List<Tok>();
            int i = 0;
            s = s.Replace(" ", "").Replace("×", "*").Replace("÷", "/").Replace("−", "-").ToLowerInvariant();
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    // scientific exponent: 1e10, 2.5e-3
                    if (i < s.Length && s[i] == 'e' && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || ((s[i + 1] == '+' || s[i + 1] == '-') && i + 2 < s.Length && char.IsDigit(s[i + 2]))))
                    {
                        i++;
                        if (s[i] == '+' || s[i] == '-') i++;
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                    }
                    string numStr = s.Substring(start, i - start);
                    list.Add(new Tok { Kind = TokKind.Num, Num = double.Parse(numStr, CultureInfo.InvariantCulture) });
                    continue;
                }
                if (char.IsLetter(c))
                {
                    int start = i;
                    i++; // first char is a letter
                    while (i < s.Length && (char.IsLetter(s[i]) || char.IsDigit(s[i]))) i++;
                    list.Add(new Tok { Kind = TokKind.Ident, Text = s.Substring(start, i - start) });
                    continue;
                }
                switch (c)
                {
                    case '+': list.Add(new Tok { Kind = TokKind.Plus }); break;
                    case '-': list.Add(new Tok { Kind = TokKind.Minus }); break;
                    case '*': list.Add(new Tok { Kind = TokKind.Star }); break;
                    case '/': list.Add(new Tok { Kind = TokKind.Slash }); break;
                    case '%': list.Add(new Tok { Kind = TokKind.Percent }); break;
                    case '^': list.Add(new Tok { Kind = TokKind.Caret }); break;
                    case '!': list.Add(new Tok { Kind = TokKind.Bang }); break;
                    case '(': list.Add(new Tok { Kind = TokKind.LParen }); break;
                    case ')': list.Add(new Tok { Kind = TokKind.RParen }); break;
                    case ',': list.Add(new Tok { Kind = TokKind.Comma }); break;
                    default: throw new MathException("Invalid character: " + c);
                }
                i++;
            }
            list.Add(new Tok { Kind = TokKind.End });
            return list;
        }

        private Tok Peek() => _toks[_pos];
        private Tok Next() => _toks[_pos++];

        // expr := addsub
        private Node ParseAddSub()
        {
            Node left = ParseMulDiv();
            while (Peek().Kind == TokKind.Plus || Peek().Kind == TokKind.Minus)
            {
                char op = Next().Kind == TokKind.Plus ? '+' : '-';
                Node right = ParseMulDiv();
                left = new BinaryNode { Op = op, Left = left, Right = right };
            }
            return left;
        }

        private Node ParseMulDiv()
        {
            Node left = ParseUnary();
            while (true)
            {
                TokKind k = Peek().Kind;
                if (k == TokKind.Star || k == TokKind.Slash || k == TokKind.Percent)
                {
                    char op = k == TokKind.Star ? '*' : (k == TokKind.Slash ? '/' : '%');
                    Next();
                    Node right = ParseUnary();
                    left = new BinaryNode { Op = op, Left = left, Right = right };
                }
                else if (k == TokKind.Num || k == TokKind.Ident || k == TokKind.LParen)
                {
                    // implicit multiplication: 2x, 2(3+1), 3sin(30)
                    Node right = ParseUnary();
                    left = new BinaryNode { Op = '*', Left = left, Right = right };
                }
                else break;
            }
            return left;
        }

        private Node ParseUnary()
        {
            if (Peek().Kind == TokKind.Minus) { Next(); return new UnaryNode { Op = '-', Operand = ParseUnary() }; }
            if (Peek().Kind == TokKind.Plus) { Next(); return ParseUnary(); }
            return ParsePower();
        }

        private Node ParsePower()
        {
            Node baseNode = ParsePostfix();
            if (Peek().Kind == TokKind.Caret)
            {
                Next();
                Node exp = ParseUnary(); // permite 2^-3
                return new BinaryNode { Op = '^', Left = baseNode, Right = exp };
            }
            return baseNode;
        }

        private Node ParsePostfix()
        {
            Node n = ParsePrimary();
            while (Peek().Kind == TokKind.Bang)
            {
                Next();
                n = new FactorialNode { Operand = n };
            }
            return n;
        }

        private Node ParsePrimary()
        {
            Tok t = Peek();
            if (t.Kind == TokKind.Num) { Next(); return new NumberNode { Value = t.Num }; }
            if (t.Kind == TokKind.LParen)
            {
                Next();
                Node inner = ParseAddSub();
                Expect(TokKind.RParen);
                return inner;
            }
            if (t.Kind == TokKind.Ident)
            {
                Next();
                string name = t.Text;
                if (Peek().Kind == TokKind.LParen)
                {
                    Next();
                    var args = new List<Node>();
                    if (Peek().Kind != TokKind.RParen)
                    {
                        args.Add(ParseAddSub());
                        while (Peek().Kind == TokKind.Comma) { Next(); args.Add(ParseAddSub()); }
                    }
                    Expect(TokKind.RParen);
                    return new FuncNode { Name = name, Args = args.ToArray() };
                }
                return new VarNode { Name = name };
            }
            throw new MathException("Syntax error");
        }

        private void Expect(TokKind k)
        {
            if (Peek().Kind != k) throw new MathException("Missing parenthesis");
            Next();
        }
    }
}
