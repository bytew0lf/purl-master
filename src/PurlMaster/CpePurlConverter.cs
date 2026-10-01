using System.Text;

namespace PurlMaster;

public static class CpePurlConverter
{
    public static string Convert(string cpe)
    {
        var values = CpeName.Parse(cpe).Values;
        if (values.Any(v => v.Kind == CpeValueKind.Pattern))
            throw new FormatException("CPE contains a wildcard pattern; no concrete PURL can be asserted.");
        if (values[2].Kind != CpeValueKind.Concrete)
            throw new FormatException("CPE product is ANY or NA; a PURL requires a concrete name.");
        if (values[1].Kind == CpeValueKind.Concrete && values[1].Text.Contains('/'))
            throw new FormatException("CPE vendor contains / and cannot form one PURL namespace segment.");

        var purl = new StringBuilder("pkg:generic/");
        if (values[1].Kind == CpeValueKind.Concrete)
            purl.Append(Encode(values[1].Text)).Append('/');
        purl.Append(Encode(values[2].Text));
        if (values[3].Kind == CpeValueKind.Concrete)
            purl.Append('@').Append(Encode(values[3].Text));

        var qualifiers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < values.Count; i++)
            if (i is not (1 or 2 or 3) && values[i].Kind == CpeValueKind.Concrete)
                qualifiers.Add("cpe_" + CpeName.AttributeNames[i], Encode(values[i].Text));
        if (qualifiers.Count > 0)
            purl.Append('?').AppendJoin('&', qualifiers.Select(q => q.Key + "=" + q.Value));
        return purl.ToString();
    }

    private static string Encode(string value)
    {
        // ECMA-427 requires literal colons to remain unencoded, including in data.
        var result = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '~' or ':')
                result.Append(c);
            else
                result.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }
}
