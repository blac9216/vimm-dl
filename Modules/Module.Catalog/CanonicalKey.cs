using System.Security.Cryptography;
using System.Text;

namespace Module.Catalog;

/// <summary>
/// The catalog's <b>cross-source content identity</b>: a single deterministic key for a game derived
/// from its ROM hashes, so the same game collapses to one identity regardless of which DAT/source
/// described it or what its files are named (D2 / #129). This is hash identity — orthogonal to
/// <see cref="Dedup"/>'s name-heuristic 1G1R grouping.
///
/// <para>Rules:</para>
/// <list type="bullet">
///   <item>Per ROM, take its strongest available hash: <b>SHA1, else MD5</b>. CRC32 is never used on
///   its own — at catalog scale CRC32 collisions are likely, so a CRC-only ROM yields no key rather
///   than a weak one.</item>
///   <item><b>Single-ROM</b> game → that ROM's hash token (e.g. <c>sha1:abc…</c>).</item>
///   <item><b>Multi-ROM</b> game (Redump discs: several tracks) → <c>set:</c> + SHA1 of the
///   <i>sorted</i>, concatenated per-ROM tokens, so the key is order-independent (a set identity).</item>
///   <item>If any ROM has no usable hash (or the game has no ROMs) → <c>null</c>: identity is unknown,
///   so the game is never deduped against another.</item>
/// </list>
///
/// <para>Note on headers: the key uses the DAT's <i>recorded</i> hashes. No-Intro/Redump DATs (across
/// the libretro mirror and the daily mirrors D3 adds) share one hashing convention, so recorded
/// hashes for the same dump already agree — no byte-level header stripping is possible or needed here
/// (we hold no ROM bytes at sync time). Header-aware re-hashing belongs to the local-import path
/// (#118), which hashes real files.</para>
/// </summary>
public static class CanonicalKey
{
    /// <summary>
    /// Compute the canonical content key for a game's ROMs, or null when identity is unknown
    /// (no ROMs, or any ROM lacks a SHA1/MD5).
    /// </summary>
    public static string? Compute(IReadOnlyList<DatRom> roms)
    {
        if (roms is null || roms.Count == 0) return null;

        var tokens = new List<string>(roms.Count);
        foreach (var rom in roms)
        {
            var token = RomToken(rom);
            if (token is null) return null;   // one unhashable ROM ⇒ the whole game has no reliable key
            tokens.Add(token);
        }

        if (tokens.Count == 1) return tokens[0];

        tokens.Sort(StringComparer.Ordinal);  // order-independent set identity
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join('|', tokens)));
        return "set:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>A ROM's strongest hash as an algorithm-tagged lowercase token, or null if CRC-only/empty.</summary>
    private static string? RomToken(DatRom rom)
    {
        if (!string.IsNullOrWhiteSpace(rom.Sha1)) return "sha1:" + rom.Sha1.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(rom.Md5)) return "md5:" + rom.Md5.Trim().ToLowerInvariant();
        return null;  // CRC32 alone is too collision-prone to anchor identity
    }
}
