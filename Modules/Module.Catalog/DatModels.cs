namespace Module.Catalog;

/// <summary>Header of a clrmamepro / No-Intro / Redump DAT: the system name + dat version.</summary>
public sealed record DatHeader(string Name, string? Version);

/// <summary>
/// One entry from a DAT — a single title/region/revision (pre-1G1R; dedup happens later).
/// <see cref="Languages"/> is best-effort, parsed from the No-Intro name tag and often empty.
/// </summary>
public sealed record DatGame(
    string Name,
    string? Region,
    string? Serial,
    IReadOnlyList<DatRom> Roms,
    IReadOnlyList<string> Languages);

/// <summary>A single file within a game (disc-based games carry one per track/disc).</summary>
public sealed record DatRom(
    string Name,
    long Size,
    string? Crc,
    string? Md5,
    string? Sha1,
    string? Serial);
