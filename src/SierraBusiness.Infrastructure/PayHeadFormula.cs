using System.Globalization;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Bounded arithmetic only; never evaluates code, properties or external data.</summary>
public static class PayHeadFormula
{
    public static void Validate(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > 240) throw new BusinessException("Enter a pay formula of at most 240 characters.");
        var names = new Dictionary<string, decimal> { ["BASIC"] = 1, ["ALLOWANCES"] = 1, ["DAYS"] = 1, ["HOURS"] = 1, ["OVERTIME_HOURS"] = 1 };
        var parser = new Parser(formula, names, true); parser.Expression(); parser.Space();
        if (parser.Position != formula.Length) throw new BusinessException("Check the pay formula near character " + (parser.Position + 1) + ".");
    }
    public static decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> values)
    {
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > 240) throw new BusinessException("Enter a pay formula of at most 240 characters.");
        var parser = new Parser(formula, values);
        try { var result = parser.Expression(); parser.Space(); if (parser.Position != formula.Length) throw new BusinessException("Check the pay formula near character " + (parser.Position + 1) + "."); if (result < 0 || result > 1_000_000_000) throw new BusinessException("Pay formulas must produce a nonnegative amount up to one billion."); return AccountingEngine.Money(result); }
        catch (Exception e) when (e is OverflowException or DivideByZeroException) { throw new BusinessException("The pay formula overflowed or divided by zero."); }
    }
    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> values, bool validate = false)
    {
        public int Position; private int depth;
        public void Space() { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }
        private bool Take(char c) { Space(); if (Position < text.Length && text[Position] == c) { Position++; return true; } return false; }
        public decimal Expression() { var value = Product(); while (true) { if (Take('+')) { var next = Product(); value = validate ? 1 : value + next; } else if (Take('-')) { var next = Product(); value = validate ? 1 : value - next; } else return value; } }
        private decimal Product() { var value = Atom(); while (true) { if (Take('*')) { var next = Atom(); value = validate ? 1 : value * next; } else if (Take('/')) { var next = Atom(); value = validate ? 1 : value / next; } else return value; } }
        private decimal Atom()
        {
            if (++depth > 16) throw new BusinessException("Use at most 16 nested formula levels.");
            try
            {
                Space(); if (Take('+')) return Atom(); if (Take('-')) { var negative = Atom(); return validate ? 1 : -negative; }
                if (Take('(')) { var value = Expression(); if (!Take(')')) throw new BusinessException("Close each formula parenthesis."); return value; }
                var start = Position;
                if (Position < text.Length && (char.IsLetter(text[Position]) || text[Position] == '_'))
                {
                    while (Position < text.Length && (char.IsLetter(text[Position]) || text[Position] == '_')) Position++;
                    var name = text[start..Position].ToUpperInvariant();
                    if (!values.TryGetValue(name, out var value)) throw new BusinessException("Unknown pay variable: " + name + ". Use BASIC, ALLOWANCES, DAYS, HOURS or OVERTIME_HOURS."); return value;
                }
                while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.')) Position++;
                if (start == Position || !decimal.TryParse(text[start..Position], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) throw new BusinessException("Use numbers, pay variables, +, -, *, / and parentheses in the formula.");
                return validate ? 1 : number;
            }
            finally { depth--; }
        }
    }
}
