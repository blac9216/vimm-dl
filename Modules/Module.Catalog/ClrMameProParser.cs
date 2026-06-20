using System.Globalization;
using System.Text;

namespace Module.Catalog;

/// <summary>
/// Streaming parser for the clrmamepro "parenthesis" DAT text format used by the
/// libretro-database mirror of the No-Intro and Redump datfiles. Forward-only and
/// allocation-light: yields one <see cref="DatGame"/> at a time so multi-MB DATs parse
/// in constant memory. Pure string parsing, no reflection — AOT-safe.
/// </summary>
/// <remarks>
/// Grammar per block: <c>IDENT '(' ( IDENT value | IDENT '(' ... ')' )* ')'</c>, where a
/// value is a quoted "string" or a bare token. Quotes guard the inner parentheses of names
/// like <c>"Game (USA) (Disc 1)"</c>, so the lexer — not paren-counting — defines structure.
/// Unknown keys/blocks are skipped, so new DAT fields never break parsing.
/// </remarks>
public sealed class ClrMameProParser
{
    /// <summary>Set from the leading <c>clrmamepro</c> block during enumeration (it is the first block).</summary>
    public DatHeader? Header { get; private set; }

    /// <summary>Stream games from a clrmamepro DAT. Enumerate to drive parsing.</summary>
    public IEnumerable<DatGame> Parse(TextReader reader)
    {
        var lexer = new Lexer(reader);
        while (true)
        {
            var tok = lexer.Next();
            if (tok.Kind == TokKind.Eof) yield break;
            if (tok.Kind != TokKind.Ident) continue; // tolerate stray tokens

            var blockName = tok.Text;
            if (lexer.Next().Kind != TokKind.LParen) continue; // not a block — skip

            if (blockName is "clrmamepro" or "datafile" or "header")
                Header = ReadHeader(lexer);
            else if (blockName is "game" or "machine")
                yield return ReadGame(lexer);
            else
                SkipBlock(lexer);
        }
    }

    private static DatHeader ReadHeader(Lexer lexer)
    {
        string? name = null, version = null;
        foreach (var (key, value) in Attributes(lexer))
        {
            if (key == "name") name = value;
            else if (key == "version") version = value;
        }
        return new DatHeader(name ?? "", version);
    }

    private static DatGame ReadGame(Lexer lexer)
    {
        string name = "";
        string? region = null, serial = null;
        List<DatRom>? roms = null;

        while (true)
        {
            var t = lexer.Next();
            if (t.Kind is TokKind.RParen or TokKind.Eof) break;
            if (t.Kind != TokKind.Ident) continue;
            var key = t.Text;

            var v = lexer.Next();
            if (v.Kind == TokKind.LParen)
            {
                if (key == "rom") (roms ??= []).Add(ReadRom(lexer));
                else SkipBlock(lexer); // release, disk, etc.
                continue;
            }
            if (v.Kind is TokKind.RParen or TokKind.Eof) break;

            switch (key)
            {
                case "name": name = v.Text; break;
                case "region": region = v.Text; break;
                case "serial": serial = v.Text; break;
            }
        }

        return new DatGame(name, region, serial,
            roms ?? (IReadOnlyList<DatRom>)[], ParseLanguages(name));
    }

    private static DatRom ReadRom(Lexer lexer)
    {
        string name = "";
        long size = 0;
        string? crc = null, md5 = null, sha1 = null, serial = null;

        foreach (var (key, value) in Attributes(lexer))
        {
            switch (key)
            {
                case "name": name = value; break;
                case "size": long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size); break;
                case "crc": crc = value; break;
                case "md5": md5 = value; break;
                case "sha1": sha1 = value; break;
                case "serial": serial = value; break;
            }
        }
        return new DatRom(name, size, crc, md5, sha1, serial);
    }

    /// <summary>Yield the <c>key value</c> attribute pairs of a block, skipping nested blocks, until its ')'.</summary>
    private static IEnumerable<(string Key, string Value)> Attributes(Lexer lexer)
    {
        while (true)
        {
            var t = lexer.Next();
            if (t.Kind is TokKind.RParen or TokKind.Eof) yield break;
            if (t.Kind != TokKind.Ident) continue;
            var key = t.Text;

            var v = lexer.Next();
            if (v.Kind == TokKind.LParen) { SkipBlock(lexer); continue; }
            if (v.Kind is TokKind.RParen or TokKind.Eof) yield break;
            yield return (key, v.Text);
        }
    }

    /// <summary>Consume a balanced block whose opening '(' was already read.</summary>
    private static void SkipBlock(Lexer lexer)
    {
        int depth = 1;
        while (depth > 0)
        {
            var t = lexer.Next();
            if (t.Kind == TokKind.Eof) break;
            if (t.Kind == TokKind.LParen) depth++;
            else if (t.Kind == TokKind.RParen) depth--;
        }
    }

    /// <summary>
    /// Extract languages from the No-Intro name convention, e.g. "Game (USA) (En,Fr,De)" → [En,Fr,De].
    /// A parenthetical group qualifies only if every comma/plus-separated token is a 2-letter "Xx" code,
    /// so region/revision groups like "(USA)" or "(Rev 1)" are ignored. First matching group wins.
    /// </summary>
    internal static IReadOnlyList<string> ParseLanguages(string name)
    {
        int i = 0;
        while (i < name.Length)
        {
            int open = name.IndexOf('(', i);
            if (open < 0) break;
            int close = name.IndexOf(')', open + 1);
            if (close < 0) break;
            var inner = name.Substring(open + 1, close - open - 1);
            i = close + 1;

            var tokens = inner.Split(',', '+');
            var langs = new List<string>(tokens.Length);
            bool allLang = true;
            foreach (var raw in tokens)
            {
                var tok = raw.Trim();
                if (tok.Length == 2 && char.IsAsciiLetterUpper(tok[0]) && char.IsAsciiLetterLower(tok[1]))
                    langs.Add(tok);
                else { allLang = false; break; }
            }
            if (allLang && langs.Count > 0) return langs;
        }
        return [];
    }

    private enum TokKind { Ident, String, LParen, RParen, Eof }

    private readonly struct Tok(TokKind kind, string text)
    {
        public readonly TokKind Kind = kind;
        public readonly string Text = text;
    }

    private sealed class Lexer(TextReader reader)
    {
        private readonly TextReader _r = reader;
        private readonly StringBuilder _sb = new();

        public Tok Next()
        {
            int c = SkipWhitespace();
            return c switch
            {
                < 0 => new Tok(TokKind.Eof, ""),
                '(' => Take(TokKind.LParen),
                ')' => Take(TokKind.RParen),
                '"' => ReadString(),
                _ => ReadIdent(),
            };
        }

        private Tok Take(TokKind kind) { _r.Read(); return new Tok(kind, ""); }

        private int SkipWhitespace()
        {
            while (true)
            {
                int c = _r.Peek();
                if (c < 0) return -1;
                if (c is ' ' or '\t' or '\r' or '\n') { _r.Read(); continue; }
                return c;
            }
        }

        private Tok ReadString()
        {
            _r.Read(); // opening quote
            _sb.Clear();
            while (true)
            {
                int c = _r.Read();
                if (c < 0 || c == '"') break;
                if (c == '\\') { int n = _r.Read(); if (n < 0) break; _sb.Append((char)n); continue; }
                _sb.Append((char)c);
            }
            return new Tok(TokKind.String, _sb.ToString());
        }

        private Tok ReadIdent()
        {
            _sb.Clear();
            while (true)
            {
                int c = _r.Peek();
                if (c < 0 || c is ' ' or '\t' or '\r' or '\n' or '(' or ')') break;
                _sb.Append((char)c);
                _r.Read();
            }
            return new Tok(TokKind.Ident, _sb.ToString());
        }
    }
}
