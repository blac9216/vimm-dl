using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<QueueRepository>();
builder.Services.AddSingleton<Module.Ps3Pipeline.Bridge.IPs3PipelineBridge, SignalRPs3PipelineBridge>();
builder.Services.AddSingleton<Module.Ps3Pipeline.Ps3ConversionPipeline>();
// Wii U pipeline (epic #104): user-configured key provider (one singleton behind the module interface),
// the SignalR bridge, and the decrypt → extract → package pipeline.
builder.Services.AddSingleton<WiiUTitleKeyProvider>();
builder.Services.AddSingleton<Module.WiiUTools.ITitleKeyProvider>(sp => sp.GetRequiredService<WiiUTitleKeyProvider>());
builder.Services.AddSingleton<Module.WiiUPipeline.Bridge.IWiiUPipelineBridge, SignalRWiiUPipelineBridge>();
builder.Services.AddSingleton<Module.WiiUPipeline.WiiUConversionPipeline>();
builder.Services.AddSingleton<Module.Download.Bridge.IDownloadBridge, SignalRDownloadBridge>();
builder.Services.AddSingleton<Module.Download.Sources.IDownloadSource, Module.Download.Sources.VimmSource>();
builder.Services.AddSingleton<Module.Download.Sources.IDownloadSource, Module.Download.Sources.ArchiveSource>();
builder.Services.AddSingleton<Module.Download.Sources.IDownloadSource, Module.WiiUSource.WiiUNusSource>();
builder.Services.AddSingleton<Module.Download.Sources.ISourceRegistry, Module.Download.Sources.SourceRegistry>();
builder.Services.AddSingleton<Module.Download.DownloadService>();
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddSingleton<Module.Sync.Bridge.ISyncBridge, SignalRSyncBridge>();
builder.Services.AddSingleton<Module.Sync.SyncService>();
builder.Services.AddSingleton<CatalogRepository>();
builder.Services.AddSingleton<Module.Catalog.ICatalogStore>(sp => sp.GetRequiredService<CatalogRepository>());
builder.Services.AddSingleton<CatalogSyncState>();
// DAT sources: the default libretro mirror (per-system raw fetch, rate-safe) and the fresher daily
// bundle (hugo release zips). The sync endpoint picks between them on the catalog_dat_source setting.
builder.Services.AddSingleton(sp => new Module.Catalog.LibretroDatSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("libretro"),
    sp.GetRequiredService<ILogger<Module.Catalog.LibretroDatSource>>()));
builder.Services.AddSingleton(sp => new Module.Catalog.DailyBundleDatSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("datbundle"),
    sp.GetRequiredService<ILogger<Module.Catalog.DailyBundleDatSource>>()));
builder.Services.AddSingleton(sp => new Module.Catalog.CatalogSyncService(
    sp.GetRequiredService<Module.Catalog.ICatalogStore>(),
    sp.GetRequiredService<ILogger<Module.Catalog.CatalogSyncService>>()));
builder.Services.AddSingleton<CatalogScanService>();
builder.Services.AddSingleton<CatalogScanState>();
builder.Services.AddSingleton<CatalogResolveService>();
builder.Services.AddSingleton<MediaService>();
builder.Services.AddSingleton<CompatSyncService>();
builder.Services.AddSingleton<CatalogCompatState>();
builder.Services.AddSingleton<IgdbClient>();
builder.Services.AddSingleton<IgdbSyncService>();
builder.Services.AddSingleton<CatalogIgdbState>();
builder.Services.AddSingleton<CatalogVerifyService>();
builder.Services.AddSingleton<CatalogVerifyState>();
builder.Services.AddSingleton<VimmSyncService>();
builder.Services.AddSingleton<CatalogVimmState>();
builder.Services.AddSingleton<IArchiveExtractor, SevenZipArchiveExtractor>();
builder.Services.AddSingleton<ImportService>();
builder.Services.AddSingleton<CatalogImportService>();
builder.Services.AddSingleton<CatalogImportState>();
builder.Services.AddSingleton<ArchiveAuth>();
builder.Services.AddTransient<ArchiveAuthHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddHttpClient("vimms")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = true,
        CookieContainer = new CookieContainer(),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(60);
        c.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        c.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        c.DefaultRequestHeaders.Add("Pragma", "no-cache");
        c.DefaultRequestHeaders.Add("Sec-CH-UA", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        c.DefaultRequestHeaders.Add("Sec-CH-UA-Mobile", "?0");
        c.DefaultRequestHeaders.Add("Sec-CH-UA-Platform", "\"Windows\"");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        c.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        c.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        c.DefaultRequestHeaders.Add("DNT", "1");
        c.DefaultRequestHeaders.Referrer = new Uri("https://vimm.net/");
    });

// Internet Archive serves plain HTTPS files + a public metadata API anonymously; the
// ArchiveAuthHandler adds the S3 "LOW" auth header only when the user configures both keys.
builder.Services.AddHttpClient("archive")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(60);
        c.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    })
    .AddHttpMessageHandler<ArchiveAuthHandler>();

// Wii U NUS/CCS CDN — plain HTTP, anonymous, with the console's own user-agent. A long timeout
// covers multi-GB content; redirects are followed. One title fans out to many files (W4 loop).
builder.Services.AddHttpClient("wiiu")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(60);
        c.DefaultRequestHeaders.Add("User-Agent", "wii libnup/1.0");
    });

// libretro-database raw files (the No-Intro/Redump mirror) — plain HTTPS, no auth.
builder.Services.AddHttpClient("libretro")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(5);
        c.DefaultRequestHeaders.Add("User-Agent", "vimm-dl");
    });

// hugo auto-datfile-generator daily bundle zips, served as GitHub release assets (302 → a CDN
// object; AllowAutoRedirect is on by default). One ~tens-of-MB zip per group, so a longer timeout.
builder.Services.AddHttpClient("datbundle")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(10);
        c.DefaultRequestHeaders.Add("User-Agent", "vimm-dl");
    });

// Emulator compatibility sources (RPCS3, …) — a browser User-Agent avoids 403s from sites like rpcs3.net.
builder.Services.AddHttpClient("compat")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(5);
        c.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    });

// libretro-thumbnails CDN (catalog box art / title screens, epic #122) — plain HTTPS, no auth; the
// images are static, so a short-ish timeout is fine and misses (404s) come back fast.
builder.Services.AddHttpClient("thumbnails")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(2);
        c.DefaultRequestHeaders.Add("User-Agent", "vimm-dl");
    });

// IGDB metadata API (epic #122 / M2) — the Twitch OAuth token request and the Apicalypse game queries
// both go through this one client (different hosts, so no BaseAddress). Plain HTTPS; creds per request.
builder.Services.AddHttpClient("igdb")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(2);
        c.DefaultRequestHeaders.Add("User-Agent", "vimm-dl");
    });

var app = builder.Build();

// Init DB
var repo = app.Services.GetRequiredService<QueueRepository>();
await repo.InitAsync(app.Configuration.GetConnectionString("Default"),
    app.Services.GetRequiredService<ILogger<QueueRepository>>());

// Catalog shares queue.db (tables created by migration 012, run above).
var catalogRepo = app.Services.GetRequiredService<CatalogRepository>();
catalogRepo.Configure(app.Configuration.GetConnectionString("Default"));

// Catalog image cache lives under the data dir (sibling of downloads/): data/media/.
app.Services.GetRequiredService<MediaService>().Configure(Path.Combine(repo.GetDataPath(), "media"));

// Seed the default archive.org download sets once (ported from RomGoGetter). Guarded by a flag so
// deleting a default set doesn't bring it back; users can edit/delete them freely.
if (await repo.GetSettingAsync(SettingsKeys.DefaultSetsSeeded) != "true")
{
    foreach (var (setName, console, items) in DefaultSets.All)
        await catalogRepo.AddSetAsync(setName, console, DefaultSets.Links(items));
    await repo.SaveSettingAsync(SettingsKeys.DefaultSetsSeeded, "true");
}

// Load Internet Archive S3 credentials (if the user has set them) so archive.org requests are
// authenticated from the first download; refreshed on save via the settings endpoint.
{
    var all = await repo.GetAllSettingsAsync();
    app.Services.GetRequiredService<ArchiveAuth>().Set(
        all.GetValueOrDefault(SettingsKeys.ArchiveS3Access),
        all.GetValueOrDefault(SettingsKeys.ArchiveS3Secret));
    // Load the user's Wii U common key (if set) so titles decrypt from the first download; refreshed on save.
    app.Services.GetRequiredService<WiiUTitleKeyProvider>().SetCommonKey(
        all.GetValueOrDefault(SettingsKeys.WiiUCommonKey));
}

// Prune old events (7-day retention, 50k max rows)
await repo.PruneEventsAsync();

// Ensure download subdirectories exist
{
    var dlBase = repo.GetDownloadPath();
    Directory.CreateDirectory(Path.Combine(dlBase, "downloading"));
    Directory.CreateDirectory(Path.Combine(dlBase, "completed"));
    // Local-import drop + reject folders (epic #118). Defaults; custom paths are created on demand.
    Directory.CreateDirectory(Path.Combine(dlBase, "import"));
    Directory.CreateDirectory(Path.Combine(dlBase, "rejected"));
}

// Seed pipeline state from DB + clean up orphans
{
    var dlBase = repo.GetDownloadPath();
    var ps3Pipeline = app.Services.GetRequiredService<Module.Ps3Pipeline.Ps3ConversionPipeline>();
    var parallelism = int.TryParse(await repo.GetSettingAsync(SettingsKeys.Ps3Parallelism), out var p) ? p : 3;
    ps3Pipeline.Configure(parallelism);

    // Migrate .ps3converted file to DB (one-time)
    await repo.MigratePs3ConvertedFileAsync(dlBase);

    // Seed converted set from DB before CleanupOrphans checks IsConverted()
    var convertedNames = await repo.GetConvertedFilenamesAsync();
    ps3Pipeline.SeedConverted(convertedNames);

    ps3Pipeline.CleanupOrphans(dlBase);

    // Wii U pipeline: same converted-set seeding (titles already decrypted won't re-run) + worker count.
    var wiiuPipeline = app.Services.GetRequiredService<Module.WiiUPipeline.WiiUConversionPipeline>();
    wiiuPipeline.Configure(parallelism);
    wiiuPipeline.SeedConverted(convertedNames);

    app.Services.GetRequiredService<Module.Sync.SyncService>().Configure(dlBase, await repo.GetSyncPathAsync());
}

// Auto-resume: if there are queued URLs, start downloading on app launch
if (await repo.HasQueuedUrlsAsync())
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        var queue = app.Services.GetRequiredService<DownloadQueue>();
        if (!queue.IsRunning)
            await queue.StartAsync(repo.GetDownloadPath());
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<DownloadHub>("/hub");

// Map API endpoints
app.MapFileEndpoints();
app.MapDownloadEndpoints();
app.MapMetadataEndpoints();
app.MapSourceEndpoints();
app.MapCatalogEndpoints();
app.MapPs3Endpoints();
app.MapSyncEndpoints();
app.MapSettingsEndpoints();
app.MapEventEndpoints();
app.MapMetricsEndpoints();

// Auto-resume: if there are queued items, start downloading on startup
{
    var logger = app.Services.GetRequiredService<ILogger<DownloadQueue>>();
    var queueRepo = app.Services.GetRequiredService<QueueRepository>();
    var dlQueue = app.Services.GetRequiredService<DownloadQueue>();
    var hasQueued = await queueRepo.HasQueuedUrlsAsync();
    logger.LogInformation("Auto-resume check: hasQueued={HasQueued}, isRunning={IsRunning}", hasQueued, dlQueue.IsRunning);
    if (hasQueued && !dlQueue.IsRunning)
    {
        logger.LogInformation("Auto-resuming download queue");
        await dlQueue.StartAsync(null);
    }
}

app.Run();
