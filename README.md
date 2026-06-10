# Topic Crawler

An autonomous, topic-focused web crawler with a persistent search index. Give it a topic via one API call; it discovers, scores, and stores relevant pages on its own — **no external search engine or API involved**. Built with ASP.NET Core, MongoDB, and AngleSharp.

## How it works

1. **Seeds** — new jobs first query the crawler's *own* MongoDB text index (pages from earlier crawls) for warm seeds, then fall back to a bundled list of high-fanout hub sites ([seeds/hubs.json](seeds/hubs.json)).
2. **Focused crawl (shark-search)** — every discovered link gets a *predicted* relevance score from its anchor text, surrounding context, parent page score, and depth — before any fetch. Low-scoring links are pruned, so bandwidth goes only to promising URLs. At shallow depth a small exploration budget keeps cold starts from generic hubs alive.
3. **Persistent index** — pages are stored once, shared across jobs, with a Mongo text index. Repeat or adjacent topics reuse fresh pages without re-fetching and get instant warm seeds. The system gets faster and cheaper with every crawl.
4. **Politeness** — robots.txt rules and Crawl-delay honored per host, per-host rate limiting (default 1 req/s), identifying User-Agent, retry with backoff, per-host circuit breaker.

## Running

```bash
# 1. Start MongoDB (any of):
docker compose up -d                  # if you have Docker
# or run a local mongod on localhost:27017

# 2. Start the API
dotnet run --project src/Sumodh.Crawler.Api --urls http://localhost:5080
```

Interactive API docs (Swagger UI): http://localhost:5080/swagger

## API

```bash
# Start a crawl (returns immediately with a job id)
curl -X POST http://localhost:5080/jobs \
  -H "content-type: application/json" \
  -d '{"topic": "quantum error correction", "maxPages": 100}'

# Watch progress
curl http://localhost:5080/jobs/{jobId}

# Results, best first
curl "http://localhost:5080/jobs/{jobId}/pages?limit=10"

# Search everything ever crawled (the persistent index)
curl "http://localhost:5080/search?q=stabilizer+codes"

# Stop a running job
curl -X POST http://localhost:5080/jobs/{jobId}/cancel
```

`POST /jobs` options (all optional except `topic`): `keywords` (extra steering terms), `urls` (your own starting URLs — crawled at top priority, while public-web discovery via index + hub seeds still always runs alongside), `maxPages` (default 500), `maxDepth` (6), `workers` (8), `minScoreToStore` (0.15), `minScoreToExpand` (0.08), `maxRuntimeMinutes` (30).

```bash
# Crawl with your own starting URLs (still explores the public web too)
curl -X POST http://localhost:5080/jobs \
  -H "content-type: application/json" \
  -d '{"topic": "rust async runtimes", "urls": ["https://tokio.rs/", "https://github.com/smol-rs/smol"], "maxPages": 50}'
```

## Configuration

Defaults live in [src/Sumodh.Crawler.Api/appsettings.json](src/Sumodh.Crawler.Api/appsettings.json) (`Crawler:Defaults`), the Mongo connection under `Mongo:`. The hub seed list ([seeds/hubs.json](seeds/hubs.json)) is plain JSON — add hub sites for your interest areas to improve cold starts.

## Layout

- `src/Sumodh.Crawler.Core` — crawl pipeline: frontier, seeds, fetcher, extractor, scorer, robots, rate limiter, Mongo stores
- `src/Sumodh.Crawler.Api` — minimal API + background orchestrator
- `tests/Sumodh.Crawler.Tests` — unit tests (`dotnet test`)

## MongoDB collections

| Collection | Purpose |
|---|---|
| `jobs` | job status, config, progress counters |
| `pages` | the shared index: text, links, content hash, per-job relevance refs; text-indexed |
| `frontier` | per-job priority queue (atomic claims, restart-safe) |
