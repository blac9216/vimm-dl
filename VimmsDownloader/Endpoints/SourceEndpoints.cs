using Module.Download.Sources;

static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered download sources, for the toolbar source picker.
        app.MapGet("/api/sources", (ISourceRegistry registry) =>
            registry.All
                .Select(s => new SourceInfo(s.Id, s.DisplayName, Catalog: s is ICatalogSource))
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

        // Browse: search a source's catalog for sets/collections.
        app.MapGet("/api/sources/{source}/sets", async (string source, string? q,
            ISourceRegistry registry, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (registry.Get(source) is not ICatalogSource cat) return Results.NotFound();
            var http = httpFactory.CreateClient(((IDownloadSource)cat).HttpClientName);
            var result = await cat.SearchSetsAsync(q ?? "", http, ct);
            return result.IsOk
                ? Results.Ok(result.Value!.ToList())
                : Results.Text(result.Error!, statusCode: StatusCodes.Status502BadGateway); // upstream archive.org failure
        });

        // Browse: list the downloadable files in a set, optionally filtered by name.
        app.MapGet("/api/sources/{source}/files", async (string source, string? set, string? q,
            ISourceRegistry registry, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(set)) return Results.BadRequest("set is required");
            if (registry.Get(source) is not ICatalogSource cat) return Results.NotFound();
            var http = httpFactory.CreateClient(((IDownloadSource)cat).HttpClientName);
            var result = await cat.ListFilesAsync(set, q, http, ct);
            return result.IsOk
                ? Results.Ok(result.Value!.ToList())
                : Results.Text(result.Error!, statusCode: StatusCodes.Status502BadGateway); // upstream archive.org failure
        });
    }
}
