namespace EdgeBar.Services;

/// <summary>一个很轻的四则运算解析器，支持括号、小数、百分号</summary>
public static class ExpressionEvaluator
{
    public static double Evaluate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("请输入算式");
        var s = input
            .Replace("×", "*")
            .Replace("÷", "/")
            .Replace("（", "(")
            .Replace("）", ")")
            .Replace(" ", "");

        int pos = 0;
        double value = ParseExpr(s, ref pos);
        if (pos != s.Length) throw new FormatException("算式不完整");
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("无法计算");
        return value;
    }

    private static double ParseExpr(string s, ref int pos)
    {
        double value = ParseTerm(s, ref pos);
        while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
        {
            char op = s[pos++];
            double right = ParseTerm(s, ref pos);
            value = op == '+' ? value + right : value - right;
        }
        return value;
    }

    private static double ParseTerm(string s, ref int pos)
    {
        double value = ParseFactor(s, ref pos);
        while (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
        {
            char op = s[pos++];
            double right = ParseFactor(s, ref pos);
            if (op == '/' && Math.Abs(right) < 1e-12) throw new FormatException("不能除以 0");
            value = op == '*' ? value * right : value / right;
        }
        return value;
    }

    private static double ParseFactor(string s, ref int pos)
    {
        if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
        {
            char sign = s[pos++];
            double v = ParseFactor(s, ref pos);
            return sign == '-' ? -v : v;
        }
        return ParsePrimary(s, ref pos);
    }

    private static double ParsePrimary(string s, ref int pos)
    {
        if (pos >= s.Length) throw new FormatException("算式不完整");

        double value;
        if (s[pos] == '(')
        {
            pos++;
            value = ParseExpr(s, ref pos);
            if (pos >= s.Length || s[pos] != ')') throw new FormatException("括号不匹配");
            pos++;
        }
        else
        {
            int start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            if (start == pos) throw new FormatException("无法识别的字符");
            if (!double.TryParse(s[start..pos], out value)) throw new FormatException("数字格式有误");
        }

        while (pos < s.Length && s[pos] == '%')
        {
            value /= 100.0;
            pos++;
        }
        return value;
    }
}
