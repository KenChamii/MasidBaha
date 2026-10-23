# Backend guide

ASP.NET Core (.NET 10), organised as **vertical slices**: each use case keeps its request, DTO,
validator and service together under `MasidBaha.Application/<Area>/<UseCase>`
(`FloodReports/CreateReport`, `FloodReports/VoteOnReport`, ...). `MasidBaha.WebAPI` hosts them:
controllers, middleware, SignalR and configuration. There is deliberately no Domain/Infrastructure
project split; business rules that deserve unit tests live on the entities
(`FloodReport.ApplyVote`, `FloodReport.IsOpen`).

## API

- Base path `/api/v1`. The OpenAPI document is at `/swagger/v1/swagger.json`, a Scalar reference
  at `/docs`, and Swagger UI at `/swagger` in Development.
- Every error is an RFC 7807 `application/problem+json` response with a `traceId`:
  400 validation (with an `errors` map), 401/503 admin key, 404, 409 (duplicate or closed vote),
  429 (with `Retry-After`), 500 (generic message; details only in the logs).
- `POST /flood-reports` answers `201 Created` with a `Location` header.
- Reports are geofenced to the Philippines. Radius search (`/flood-reports?lat&lng&radiusMeters`) accepts
  any coordinate so a viewer abroad can still open the map.

## Identity: the session cookie

Nobody logs in to report or vote, so the server issues each browser a random id in an
HMAC-signed, `HttpOnly` cookie (`mb_session`). The reporter or voter is whoever holds that cookie;
ids sent in request bodies are ignored. The cookie is only issued on write endpoints.

- `Security:SessionSigningKey` (32+ characters) is required outside Development. In Development a
  throwaway key is generated and sessions reset on restart.
- `Security:SessionCookieSameSite` is `Lax` by default, which works when the site and API share a
  registrable domain (and for `localhost`). Use `None` only if they are on different sites (needs HTTPS).
- The Angular app adds `withCredentials` to API calls (`apiCredentialsInterceptor`), and CORS
  allows credentials for the origins in `Cors:AllowedOrigins`.

## Configuration

Settings are typed options validated at startup (a bad value fails the boot, not a request).
`appsettings.Example.json` lists every key with its default. Secrets never go in tracked files:
use `scripts/init-dev-secrets.ps1` locally and environment variables in production
(`Security__SessionSigningKey`, `ConnectionStrings__MasidBahaDb`, ...).

| Section | Controls |
|---|---|
| `Reports` | report lifetime (hours), max top-reports page |
| `Expiry` | how often the sweeper runs |
| `Push` / `Vapid` | minimum severity that triggers a notification, queue size, web-push keys |
| `Photos` | max upload size, max image side, JPEG quality |
| `RateLimits` | per-IP limits for report, vote and push writes |
| `Geocoding` | Nominatim URL, timeout, 1 request/second gate, cache size and lifetime |
| `Storage` | `Local` (wwwroot/uploads) or `S3` (Cloudflare R2, Supabase Storage, MinIO, AWS S3) |
| `Security` | session cookie, HTTPS redirect + HSTS, trusting `X-Forwarded-*` |

## Behind a reverse proxy / PaaS

Set `Security:TrustForwardedHeaders=true` so rate limiting sees the real client IP and the
scheme is `https`, then `Security:RequireHttps=true` for the redirect and HSTS. Only do this
when the API is not directly reachable from the internet.

## Photos

Uploads are decoded, resized, re-encoded as JPEG (this strips EXIF/GPS) and stored through
`IPhotoStorageService`. A report may only reference a photo this deployment produced
(`IPhotoUrlPolicy`): same host under `/uploads/` for local storage, or the configured
`Storage:S3:PublicBaseUrl` for S3. Free hosts have ephemeral disks, so use S3 in production.

## Performance and resilience

- `GET /flood-reports/top` (15 s) and `/heatmap` (60 s) use output caching; every write evicts them.
- Push notifications are queued (`IPushQueue`) and sent by a background worker, never inside the
  request. One shared `WebPushClient` is used.
- Reverse geocoding is cached by rounded coordinates (about 110 m), spaced to one request per
  second, and wrapped in a retry / circuit-breaker pipeline. A failed lookup never blocks a report.

## Observability

- Serilog: readable console in Development, one JSON object per line elsewhere.
- `/health` (process is up) and `/health/ready` (database and photo storage answer; 503 otherwise).
- OpenTelemetry traces and metrics for ASP.NET Core, outgoing HTTP and Npgsql. Nothing is exported
  until `Observability:OtlpEndpoint` (or `OTEL_EXPORTER_OTLP_ENDPOINT`) is set.

## Tests

`dotnet test backend/MasidBaha.slnx` (Docker must be running). Unit tests need nothing; the
integration and API tests start a real PostGIS container, and the S3 tests an S3-compatible
container, with Testcontainers. The API tests host the real app in-process, with a cookie-keeping
client per simulated browser.
