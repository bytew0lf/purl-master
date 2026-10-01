using System.Text;
using System.Text.RegularExpressions;

namespace PurlMaster;

public enum CpeValueKind { Concrete, Any, NotApplicable, Pattern }

public sealed record CpeValue(string Text, CpeValueKind Kind);

public sealed record CpeName(IReadOnlyList<CpeValue> Values)
{
    public static readonly string[] AttributeNames =
        ["part", "vendor", "product", "version", "update", "edition", "language",
         "sw_edition", "target_sw", "target_hw", "other"];

    private const string QuotedCharacters = "\\*?!\"#$%&'()+,/:;<=>@[]^`{|}~";
    private static readonly Regex Language = new("^[a-z]{2,3}(?:-(?:[a-z]{2}|[0-9]{3}))?$",
        RegexOptions.CultureInvariant);

    public static CpeName Parse(string text)
    {
        CpeValue[] values;
        if (text.StartsWith("cpe:2.3:", StringComparison.Ordinal))
        {
            var fields = SplitFormatted(text[8..]);
            if (fields.Count != 11)
                throw new FormatException("A CPE 2.3 formatted string must contain exactly eleven attributes.");
            values = fields.Select(ParseFormattedValue).ToArray();
        }
        else if (text.StartsWith("cpe:/", StringComparison.Ordinal))
        {
            var fields = text[5..].Split(':');
            if (fields.Length > 7)
                throw new FormatException("A CPE URI must contain at most seven attributes.");
            values = Enumerable.Repeat(new CpeValue("", CpeValueKind.Any), 11).ToArray();
            for (var i = 0; i < fields.Length; i++)
            {
                if (i == 5 && fields[i].StartsWith('~'))
                {
                    var packed = fields[i][1..].Split('~');
                    if (packed.Length != 5)
                        throw new FormatException("A packed CPE edition must contain five attributes.");
                    int[] indices = [5, 7, 8, 9, 10];
                    for (var j = 0; j < packed.Length; j++)
                        values[indices[j]] = ParseUriValue(packed[j]);
                }
                else
                    values[i] = ParseUriValue(fields[i]);
            }
        }
        else
            throw new FormatException("Expected a CPE 2.3 formatted string or a cpe:/ URI.");

        if (values[0].Kind is not (CpeValueKind.Any or CpeValueKind.NotApplicable) &&
            values[0].Text is not ("a" or "o" or "h"))
            throw new FormatException("CPE part must be a, o, h, ANY or NA.");
        if (values[6].Kind is not (CpeValueKind.Any or CpeValueKind.NotApplicable) &&
            !Language.IsMatch(values[6].Text))
            throw new FormatException("Invalid CPE language tag.");
        return new CpeName(values);
    }

    private static List<string> SplitFormatted(string value)
    {
        var fields = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                if (++i == value.Length)
                    throw new FormatException("Trailing escape in CPE formatted string.");
            }
            else if (value[i] == ':')
            {
                fields.Add(value[start..i]);
                start = i + 1;
            }
        }
        fields.Add(value[start..]);
        return fields;
    }

    private static CpeValue ParseFormattedValue(string value)
    {
        if (value.Length == 0)
            throw new FormatException("CPE formatted attributes cannot be empty; use * for ANY.");
        if (value == "*") return new("", CpeValueKind.Any);
        if (value == "-") return new("", CpeValueKind.NotApplicable);

        var tokens = new List<(char Character, bool Wildcard)>();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\')
            {
                if (++i == value.Length || !QuotedCharacters.Contains(value[i]))
                    throw new FormatException("Invalid quoted character in CPE formatted attribute.");
                tokens.Add((value[i], false));
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
                tokens.Add((c, false));
            else if (c is '*' or '?')
                tokens.Add((c, true));
            else
                throw new FormatException("CPE formatted punctuation must be escaped and characters must be ASCII.");
        }
        return MakeValue(tokens);
    }

    private static CpeValue ParseUriValue(string value)
    {
        if (value.Length == 0) return new("", CpeValueKind.Any);
        if (value == "-") return new("", CpeValueKind.NotApplicable);
        var tokens = new List<(char Character, bool Wildcard)>();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !byte.TryParse(value.AsSpan(i + 1, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var octet))
                    throw new FormatException("Malformed percent escape in CPE URI.");
                i += 2;
                if (octet is 1 or 2)
                    tokens.Add((octet == 1 ? '?' : '*', true));
                else if (QuotedCharacters.Contains((char)octet))
                    tokens.Add(((char)octet, false));
                else
                    throw new FormatException($"Unsupported CPE URI escape %{octet:X2}.");
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '~')
                tokens.Add((c, false));
            else
                throw new FormatException("Invalid character in CPE URI attribute.");
        }
        return MakeValue(tokens);
    }

    private static CpeValue MakeValue(List<(char Character, bool Wildcard)> tokens)
    {
        var firstLiteral = tokens.FindIndex(t => !t.Wildcard);
        var lastLiteral = tokens.FindLastIndex(t => !t.Wildcard);
        if (firstLiteral < 0 ||
            tokens.Skip(firstLiteral).Take(lastLiteral - firstLiteral + 1).Any(t => t.Wildcard) ||
            !ValidWildcardRun(tokens.Take(firstLiteral)) ||
            !ValidWildcardRun(tokens.Skip(lastLiteral + 1)))
            throw new FormatException("CPE wildcards must be a single * or a run of ? at attribute boundaries.");
        var text = new StringBuilder();
        foreach (var token in tokens) text.Append(char.ToLowerInvariant(token.Character));
        return new(text.ToString(), tokens.Any(t => t.Wildcard) ? CpeValueKind.Pattern : CpeValueKind.Concrete);
    }

    private static bool ValidWildcardRun(IEnumerable<(char Character, bool Wildcard)> run)
    {
        var chars = run.Select(t => t.Character).ToArray();
        return chars.Length == 0 || chars.All(c => c == '?') || chars is ['*'];
    }
}
