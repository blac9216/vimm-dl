namespace Module.Catalog;

/// <summary>
/// One scalar line from a YAML document: its <see cref="Indent"/> (leading spaces), <see cref="Key"/>,
/// and <see cref="Value"/> (null for a key-only line — a top-level serial header or a nested-map opener
/// like <c>compatibility:</c>; surrounding quotes stripped).
/// </summary>
public readonly record struct YamlLine(int Indent, string Key, string? Value);

/// <summary>
/// A minimal, AOT-safe YAML line scanner for the flat "serial → fields" emulator game databases
/// (PCSX2's <c>GameIndex.yaml</c>, DuckStation's <c>gamedb.yaml</c>). It is NOT a general YAML parser:
/// it yields one <see cref="YamlLine"/> per <c>key:</c> / <c>key: value</c> line, tracking indentation,
/// and skips blanks, comments, and list items. That is enough to pull a scalar field (compat / rating)
/// out of each top-level (indent-0) entry. Hand-rolled like <c>ClrMameProParser</c> to avoid
/// YamlDotNet's reflection (AOT).
/// </summary>
public static class YamlScanner
{
    public static IEnumerable<YamlLine> Scan(string text)
    {
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            // Indent = leading spaces (these files don't use tabs).
            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ') indent++;
            if (indent == raw.Length) continue;              // blank line

            var content = raw.AsSpan(indent);
            if (content[0] is '#' or '-') continue;          // comment or list item — not a scalar field

            int colon = content.IndexOf(':');
            if (colon <= 0) continue;                        // no key — skip

            var key = content[..colon].Trim().ToString();
            if (key.Length == 0) continue;

            var valueSpan = content[(colon + 1)..].Trim();
            string? value = valueSpan.Length == 0 ? null : Unquote(valueSpan).ToString();
            yield return new YamlLine(indent, key, value);
        }
    }

    /// <summary>Strip a single pair of surrounding single/double quotes, if present.</summary>
    private static ReadOnlySpan<char> Unquote(ReadOnlySpan<char> s) =>
        s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))
            ? s[1..^1]
            : s;
}
