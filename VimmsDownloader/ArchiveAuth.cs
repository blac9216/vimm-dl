using System.Net.Http.Headers;

/// <summary>
/// Holds the Internet Archive S3 credentials and the archive.org "LOW" authorization header built
/// from them. Loaded once at startup and refreshed whenever an <c>archive_s3_*</c> setting is saved,
/// so key changes take effect without a restart. When either key is blank, no header is produced —
/// archive.org serves the common case anonymously over plain HTTPS. Registered as a singleton; the
/// reference swap in <see cref="Set"/> is atomic and the value is immutable, so reads are lock-free.
/// </summary>
sealed class ArchiveAuth
{
    private volatile AuthenticationHeaderValue? _header;

    /// <summary>The <c>LOW access:secret</c> header, or null when either credential is unset.</summary>
    public AuthenticationHeaderValue? Header => _header;

    /// <summary>Rebuilds the header from the given credentials (both required), trimming whitespace.</summary>
    public void Set(string? access, string? secret) =>
        _header = !string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(secret)
            ? new AuthenticationHeaderValue("LOW", $"{access.Trim()}:{secret.Trim()}")
            : null;
}

/// <summary>
/// Adds the Internet Archive S3 "LOW" auth header to archive.org requests when both keys are set.
/// Attached to the "archive" HttpClient. Never overrides an Authorization header already on the
/// request, and is a no-op when credentials are blank.
/// </summary>
sealed class ArchiveAuthHandler(ArchiveAuth auth) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization ??= auth.Header;
        return base.SendAsync(request, ct);
    }
}
