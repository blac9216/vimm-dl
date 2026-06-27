using Module.Core.Pipeline;

namespace Module.WiiUPipeline;

/// <summary>
/// Wii U-specific pipeline sub-phases. Extends <see cref="PipelinePhase"/> with the
/// decrypt → extract → package steps unique to the Wii U workflow. (Fetching covers
/// reading the downloaded WUP set's metadata before decryption begins.)
/// </summary>
public static class WiiUPhase
{
    // Universal (from PipelinePhase)
    public const string Queued = PipelinePhase.Queued;
    public const string Done = PipelinePhase.Done;
    public const string Error = PipelinePhase.Error;
    public const string Skipped = PipelinePhase.Skipped;

    // Wii U-specific
    public const string Fetching = "fetching";
    public const string Decrypting = "decrypting";
    public const string Extracting = "extracting";
    public const string Packaging = "packaging";
}
