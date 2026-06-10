using System.Net;
using Sumodh.Crawler.Api.Endpoints;
using Sumodh.Crawler.Core.Extraction;
using Sumodh.Crawler.Core.Fetching;
using Sumodh.Crawler.Core.Frontier;
using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Orchestration;
using Sumodh.Crawler.Core.Seeds;
using Sumodh.Crawler.Core.Storage;
using MongoDB.Driver;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

var mongoConnection = builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
var mongoDatabase = builder.Configuration["Mongo:Database"] ?? "crawler";

var crawlDefaults = new CrawlConfig();
builder.Configuration.GetSection("Crawler:Defaults").Bind(crawlDefaults);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
{
    Title = "Topic Crawler API",
    Version = "v1",
    Description = "Autonomous topic-focused web crawler with a persistent MongoDB index. " +
                  "POST a topic to /jobs and the crawl runs unattended; query /search against everything ever crawled."
}));

builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnection));
builder.Services.AddSingleton(sp => new MongoContext(sp.GetRequiredService<IMongoClient>(), mongoDatabase));
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<PageStore>();
builder.Services.AddSingleton<MongoFrontier>();
builder.Services.AddSingleton<RobotsCache>();
builder.Services.AddSingleton<DomainRateLimiter>();
builder.Services.AddSingleton<HostCircuitBreaker>();
builder.Services.AddSingleton<HttpFetcher>();
builder.Services.AddSingleton<HtmlExtractor>();
builder.Services.AddSingleton(new HubSeedSource(ResolveHubsPath(builder)));
builder.Services.AddSingleton<SeedResolver>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService<CrawlOrchestrator>();

const string userAgent = "TopicCrawler/0.1 (+autonomous topic crawler; respects robots.txt)";

builder.Services.AddHttpClient("crawler", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 4,
    ConnectTimeout = TimeSpan.FromSeconds(10)
});

builder.Services.AddHttpClient("robots", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Topic Crawler v1"));

app.MapGet("/", () => Results.Ok(new
{
    service = "topic-crawler",
    docs = "/swagger",
    endpoints = new[] { "POST /jobs", "GET /jobs/{id}", "GET /jobs/{id}/pages", "POST /jobs/{id}/cancel", "GET /search?q=" }
})).ExcludeFromDescription();
app.MapJobs(crawlDefaults);
app.MapSearch();

app.Run();

static string ResolveHubsPath(WebApplicationBuilder builder)
{
    var configured = builder.Configuration["Crawler:HubsFilePath"] ?? "seeds/hubs.json";
    var path = Path.IsPathRooted(configured)
        ? configured
        : Path.GetFullPath(configured, builder.Environment.ContentRootPath);
    if (File.Exists(path))
        return path;

    // Fall back to walking up from the content root (e.g. running from bin/ or src/).
    for (var dir = new DirectoryInfo(builder.Environment.ContentRootPath); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "seeds", "hubs.json");
        if (File.Exists(candidate))
            return candidate;
    }
    return path;
}
