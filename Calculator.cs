using System.Globalization;
using System.Text;

namespace Winfred;

/// <summary>
/// The inline calculator: type "10+25" and the answer is the first result.
/// Recursive-descent parser over + - * / ^ mod, parentheses, functions, constants and
/// Alfred-style percentages ("120 + 15%", "20% of 300", "50 - 10%").
/// </summary>
public static class Calculator
{
    /// <summary>A parsed value; percent literals stay tagged so operators can interpret them.</summary>
    private readonly record struct Value(double Number, bool IsPercent)
    {
        public double Resolved => IsPercent ? Number / 100.0 : Number;
    }

    public sealed class Result
    {
        public required string Expression { get; init; }
        public required double Number { get; init; }
        public required string Formatted { get; init; }
        /// <summary>Extra readings of the same answer, e.g. hex and binary for integers.</summary>
        public string Alternates { get; init; } = "";
    }

    /// <summary>
    /// Evaluates <paramref name="raw"/> if it looks like arithmetic, otherwise returns null.
    /// A leading "=" forces calculator mode even for a bare number.
    /// </summary>
    public static Result? TryEvaluate(string raw)
    {
        if (raw == null) return null;
        string text = raw.Trim();
        bool forced = text.StartsWith('=');
        if (forced) text = text[1..].Trim();
        while (text.EndsWith('=')) text = text[..^1].TrimEnd();
        if (text.Length == 0) return null;

        if (Config.Current.Calculator.RequireEqualsPrefix && !forced) return null;
        if (!forced && !LooksLikeMath(text)) return null;

        try
        {
            var parser = new Parser(text);
            var value = parser.ParseExpression();
            parser.ExpectEnd();
            double number = value.Resolved;
            if (double.IsNaN(number) || double.IsInfinity(number)) return null;

            return new Result
            {
                Expression = text,
                Number = number,
                Formatted = Format(number),
                Alternates = Alternates(number),
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Cheap gate so ordinary words never reach the parser.</summary>
    private static bool LooksLikeMath(string text)
    {
        bool hasDigit = false, hasOperator = false;
        foreach (char c in text)
        {
            if (char.IsDigit(c)) hasDigit = true;
            else if ("+-*/^%×÷".Contains(c)) hasOperator = true;
        }
        // A bare constant is unambiguous enough to answer; a bare number ("2026") is not.
        if (BareConstants.Any(c => text.Equals(c, StringComparison.OrdinalIgnoreCase))) return true;
        if (!hasDigit) return false;
        if (hasOperator || HasTimesLetter(text)) return true;

        // Function calls and "20 of 300"-style forms count as math even without a symbol.
        foreach (var name in FunctionNames)
            if (text.Contains(name + "(", StringComparison.OrdinalIgnoreCase)) return true;
        return ContainsWord(text, "mod") || ContainsWord(text, "of");
    }

    private static readonly string[] BareConstants = { "pi", "tau", "phi" };

    /// <summary>Spots "10 x 5" without matching words like "max 10" or "linux 5".</summary>
    private static bool HasTimesLetter(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) != 'x') continue;

            int before = i - 1;
            while (before >= 0 && text[before] == ' ') before--;
            if (before < 0 || !char.IsDigit(text[before])) continue;
            if (i > 0 && char.IsLetter(text[i - 1])) continue;

            int after = i + 1;
            while (after < text.Length && text[after] == ' ') after++;
            if (after < text.Length && (char.IsDigit(text[after]) || text[after] == '(')) return true;
        }
        return false;
    }

    private static bool ContainsWord(string text, string word)
    {
        int index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool leftOk = index == 0 || !char.IsLetter(text[index - 1]);
            int end = index + word.Length;
            bool rightOk = end >= text.Length || !char.IsLetter(text[end]);
            if (leftOk && rightOk) return true;
            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static string Format(double number)
    {
        var cfg = Config.Current.Calculator;
        double magnitude = Math.Abs(number);
        if (magnitude != 0 && (magnitude >= 1e15 || magnitude < 1e-9))
            return number.ToString("0.#####e+0", CultureInfo.InvariantCulture);

        double rounded = Math.Round(number, cfg.Decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // drop "-0"
        string pattern = cfg.ThousandsSeparator ? "#,##0" : "0";
        if (cfg.Decimals > 0) pattern += "." + new string('#', cfg.Decimals);
        return rounded.ToString(pattern, CultureInfo.InvariantCulture);
    }

    private static string Alternates(double number)
    {
        if (number != Math.Floor(number) || Math.Abs(number) > int.MaxValue) return "";
        long integer = (long)number;
        if (integer is 0 or 1) return "";
        var parts = new List<string> { $"hex 0x{integer:X}" };
        if (Math.Abs(integer) < 1 << 20)
            parts.Add($"bin 0b{Convert.ToString(Math.Abs(integer), 2)}");
        return string.Join(" · ", parts);
    }

    private static readonly string[] FunctionNames =
    {
        "sqrt", "cbrt", "abs", "round", "floor", "ceil", "ln", "log", "log2", "log10",
        "exp", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh",
        "min", "max", "sum", "avg", "pow", "sign", "fact", "rad", "deg",
    };

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) => _text = text;

        public void ExpectEnd()
        {
            SkipSpace();
            if (_pos < _text.Length) throw new FormatException($"unexpected '{_text[_pos]}'");
        }

        public Value ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipSpace();
                if (Take('+')) left = Add(left, ParseTerm(), 1);
                else if (TakeMinus()) left = Add(left, ParseTerm(), -1);
                else return left;
            }
        }

        private Value ParseTerm()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipSpace();
                if (Take('*') || Take('×') || TakeTimesLetter())
                {
                    left = new Value(left.Resolved * ParseUnary().Resolved, false);
                }
                else if (Take('/') || Take('÷'))
                {
                    double divisor = ParseUnary().Resolved;
                    if (divisor == 0) throw new FormatException("divide by zero");
                    left = new Value(left.Resolved / divisor, false);
                }
                else if (TakeWord("mod"))
                {
                    double divisor = ParseUnary().Resolved;
                    if (divisor == 0) throw new FormatException("divide by zero");
                    left = new Value(left.Resolved % divisor, false);
                }
                else if (TakeWord("of"))
                {
                    // "20% of 300" — the left side is the share, the right the whole.
                    left = new Value(left.Resolved * ParseUnary().Resolved, false);
                }
                else
                {
                    return left;
                }
            }
        }

        private Value ParseUnary()
        {
            SkipSpace();
            if (Take('+')) return ParseUnary();
            if (TakeMinus()) return new Value(-ParseUnary().Resolved, false);
            return ParsePower();
        }

        private Value ParsePower()
        {
            var baseValue = ParsePostfix();
            SkipSpace();
            if (Peek() == '^' || (Peek() == '*' && Peek(1) == '*'))
            {
                _pos += Peek() == '^' ? 1 : 2;
                var exponent = ParseUnary(); // right-associative
                return new Value(Math.Pow(baseValue.Resolved, exponent.Resolved), false);
            }
            return baseValue;
        }

        private Value ParsePostfix()
        {
            var value = ParsePrimary();
            SkipSpace();
            if (Peek() == '%')
            {
                _pos++;
                return new Value(value.Number, true);
            }
            if (Take('!'))
                return new Value(Factorial(value.Resolved), false);
            return value;
        }

        private Value ParsePrimary()
        {
            SkipSpace();
            if (_pos >= _text.Length) throw new FormatException("unexpected end");

            char c = _text[_pos];
            if (c is '(' or '[')
            {
                _pos++;
                var inner = ParseExpression();
                SkipSpace();
                if (_pos >= _text.Length || (_text[_pos] != ')' && _text[_pos] != ']'))
                    throw new FormatException("missing )");
                _pos++;
                return inner;
            }

            if (char.IsDigit(c) || c == '.' || c == '$' || c == '£' || c == '€')
                return new Value(ParseNumber(), false);

            if (char.IsLetter(c) || c == 'π') return ParseIdentifier();

            throw new FormatException($"unexpected '{c}'");
        }

        private Value ParseIdentifier()
        {
            if (Take('π')) return new Value(Math.PI, false);

            int start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
            string name = _text[start.._pos];
            SkipSpace();

            if (_pos < _text.Length && _text[_pos] == '(')
            {
                _pos++;
                var args = new List<double>();
                SkipSpace();
                if (_pos < _text.Length && _text[_pos] == ')') _pos++;
                else
                {
                    while (true)
                    {
                        args.Add(ParseExpression().Resolved);
                        SkipSpace();
                        if (Take(',') || Take(';')) continue;
                        if (Take(')')) break;
                        throw new FormatException("missing )");
                    }
                }
                return new Value(CallFunction(name, args), false);
            }

            return new Value(Constant(name), false);
        }

        private static double Constant(string name) => name.ToLowerInvariant() switch
        {
            "pi" => Math.PI,
            "tau" => Math.Tau,
            "e" => Math.E,
            "phi" => (1 + Math.Sqrt(5)) / 2,
            _ => throw new FormatException($"unknown name '{name}'"),
        };

        private static double CallFunction(string name, List<double> args)
        {
            double One()
            {
                if (args.Count != 1) throw new FormatException($"{name} takes 1 argument");
                return args[0];
            }

            return name.ToLowerInvariant() switch
            {
                "sqrt" => Math.Sqrt(One()),
                "cbrt" => Math.Cbrt(One()),
                "abs" => Math.Abs(One()),
                "round" => args.Count == 2
                    ? Math.Round(args[0], (int)Math.Clamp(args[1], 0, 15), MidpointRounding.AwayFromZero)
                    : Math.Round(One(), MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(One()),
                "ceil" => Math.Ceiling(One()),
                "ln" => Math.Log(One()),
                "log" => args.Count == 2 ? Math.Log(args[0], args[1]) : Math.Log10(One()),
                "log10" => Math.Log10(One()),
                "log2" => Math.Log2(One()),
                "exp" => Math.Exp(One()),
                "sin" => Math.Sin(One()),
                "cos" => Math.Cos(One()),
                "tan" => Math.Tan(One()),
                "asin" => Math.Asin(One()),
                "acos" => Math.Acos(One()),
                "atan" => Math.Atan(One()),
                "sinh" => Math.Sinh(One()),
                "cosh" => Math.Cosh(One()),
                "tanh" => Math.Tanh(One()),
                "sign" => Math.Sign(One()),
                "rad" => One() * Math.PI / 180,
                "deg" => One() * 180 / Math.PI,
                "fact" => Factorial(One()),
                "pow" => args.Count == 2 ? Math.Pow(args[0], args[1]) : throw new FormatException("pow takes 2"),
                "min" => args.Count > 0 ? args.Min() : throw new FormatException("min needs arguments"),
                "max" => args.Count > 0 ? args.Max() : throw new FormatException("max needs arguments"),
                "sum" => args.Sum(),
                "avg" => args.Count > 0 ? args.Average() : throw new FormatException("avg needs arguments"),
                _ => throw new FormatException($"unknown function '{name}'"),
            };
        }

        private static double Factorial(double value)
        {
            if (value < 0 || value != Math.Floor(value) || value > 170)
                throw new FormatException("factorial needs 0–170");
            double result = 1;
            for (int i = 2; i <= (int)value; i++) result *= i;
            return result;
        }

        private double ParseNumber()
        {
            while (_pos < _text.Length && (_text[_pos] is '$' or '£' or '€')) _pos++;

            if (_text[_pos] == '0' && _pos + 1 < _text.Length)
            {
                char prefix = char.ToLowerInvariant(_text[_pos + 1]);
                if (prefix is 'x' or 'b' or 'o')
                {
                    int numberBase = prefix switch { 'x' => 16, 'b' => 2, _ => 8 };
                    int start = _pos + 2;
                    int end = start;
                    while (end < _text.Length && IsDigitInBase(_text[end], numberBase)) end++;
                    if (end > start)
                    {
                        string digits = _text[start..end];
                        _pos = end;
                        return Convert.ToInt64(digits, numberBase);
                    }
                }
            }

            var buffer = new StringBuilder();
            bool seenDot = false;
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (char.IsDigit(c)) buffer.Append(c);
                else if (c == ',' && !seenDot && buffer.Length > 0 && IsThousandsGroup(_pos))
                {
                    // "1,234" is one number; "min(1,2)" is two arguments, so require a full group of 3
                }
                else if (c == '.' && !seenDot)
                {
                    seenDot = true;
                    buffer.Append('.');
                }
                else if ((c == 'e' || c == 'E') && buffer.Length > 0 && _pos + 1 < _text.Length &&
                         (char.IsDigit(_text[_pos + 1]) ||
                          ((_text[_pos + 1] == '-' || _text[_pos + 1] == '+') &&
                           _pos + 2 < _text.Length && char.IsDigit(_text[_pos + 2]))))
                {
                    buffer.Append('e');
                    _pos++;
                    if (_text[_pos] is '-' or '+') buffer.Append(_text[_pos++]);
                    continue;
                }
                else break;
                _pos++;
            }

            if (buffer.Length == 0 || buffer.ToString() == ".") throw new FormatException("bad number");
            return double.Parse(buffer.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>True when the comma at <paramref name="comma"/> is followed by exactly 3 digits.</summary>
        private bool IsThousandsGroup(int comma)
        {
            for (int i = 1; i <= 3; i++)
                if (comma + i >= _text.Length || !char.IsDigit(_text[comma + i])) return false;
            return comma + 4 >= _text.Length || !char.IsDigit(_text[comma + 4]);
        }

        /// <summary>"10 x 5" and "10x5" multiply, but only where 'x' isn't part of a word.</summary>
        private bool TakeTimesLetter()
        {
            SkipSpace();
            if (_pos >= _text.Length || char.ToLowerInvariant(_text[_pos]) != 'x') return false;
            if (_pos > 0 && char.IsLetter(_text[_pos - 1])) return false;
            if (_pos + 1 < _text.Length && char.IsLetter(_text[_pos + 1])) return false;
            _pos++;
            return true;
        }

        private static bool IsDigitInBase(char c, int numberBase) => numberBase switch
        {
            2 => c is '0' or '1',
            8 => c >= '0' && c <= '7',
            _ => char.IsDigit(c) || (char.ToLowerInvariant(c) >= 'a' && char.ToLowerInvariant(c) <= 'f'),
        };

        private static Value Add(Value left, Value right, int sign)
        {
            // "120 + 15%" means 15% *of 120*, matching how Alfred and desk calculators behave.
            if (right.IsPercent && !left.IsPercent)
                return new Value(left.Number + sign * left.Number * right.Number / 100.0, false);
            return new Value(left.Resolved + sign * right.Resolved, false);
        }

        private void SkipSpace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        private char Peek(int offset = 0) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

        private bool Take(char c)
        {
            SkipSpace();
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        /// <summary>Accepts '-' and the unicode minus/en-dash people paste in.</summary>
        private bool TakeMinus() => Take('-') || Take('−') || Take('–');

        private bool TakeWord(string word)
        {
            SkipSpace();
            if (_pos + word.Length > _text.Length) return false;
            if (string.Compare(_text, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            int end = _pos + word.Length;
            if (end < _text.Length && char.IsLetterOrDigit(_text[end])) return false;
            if (_pos > 0 && char.IsLetterOrDigit(_text[_pos - 1])) return false;
            _pos = end;
            return true;
        }
    }
}
