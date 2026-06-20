using Module.Download.Sources;

static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered download sources, for the toolbar source picker.
        app.MapGet("/api/sources", (ISourceRegistry registry) =>
            registry.All
                .Select(s => new SourceInfo(s.Id, s.DisplayName))
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }
}
