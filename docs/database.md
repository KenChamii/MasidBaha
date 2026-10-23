# Database (PostgreSQL + PostGIS)

MasidBaha stores reports in PostgreSQL with the PostGIS extension, accessed through EF Core
(Npgsql + NetTopologySuite). The schema lives in EF migrations under
`backend/MasidBaha.Application/Persistence/Migrations`.

## Run it locally

```bash
docker compose up -d db                 # PostGIS on 127.0.0.1:5433
pwsh ./scripts/init-dev-secrets.ps1     # press Enter to use the docker database
dotnet run --project backend/MasidBaha.WebAPI
```

On startup the API applies pending migrations (`Database:MigrateOnStartup`, default `true`)
and, in Development, loads demo reports (`Database:SeedDemoData`). The seeder only replaces
its own rows (session id `demo-seed`) and refreshes their expiry, so the live map is never
empty during a demo.

## Change the schema

Edit the entities or `MasidBahaDbContext`, then add a migration:

```bash
cd backend
dotnet ef migrations add <Name> \
  --project MasidBaha.Application --startup-project MasidBaha.WebAPI \
  --output-dir Persistence/Migrations
```

The tools read the connection string from user-secrets or the
`ConnectionStrings__MasidBahaDb` environment variable.

## Design notes

| Topic | Decision |
|---|---|
| Location | `geography(Point, 4326)`. PostGIS order is **longitude first**; use `FloodReportMappings.ToPoint(lat, lng)` so it's never hand-written. |
| Radius search | `Location.IsWithinDistance(point, metres)` becomes `ST_DWithin`, served by a GiST index. |
| Time | `timestamptz`, always UTC. Query-string dates are normalised with `ToUtc()`. |
| Enums | stored as `smallint` (a signed type, which is what fixed the deny-vote bug). CHECK constraints guard severity, status and vote type. |
| Expiry | Reads filter on `expires_at > now`, so correctness doesn't depend on the background sweeper (important on free hosts that sleep). The sweeper only flips the stored status and notifies SignalR clients. |
| Voting | One transaction with `SELECT ... FOR UPDATE` on the report row, so concurrent votes are serialized. A unique index on (report, voter session) is the backstop. |
| Naming | `snake_case` tables and columns via `EFCore.NamingConventions`. |

## Tests

Integration tests (`backend/MasidBaha.Tests/Integration`) start a real `postgis/postgis`
container with Testcontainers, so Docker must be running for `dotnet test`.

## Moving data from the old SQL Server database

The previous SQL Server schema and stored procedures were removed. If you have data worth
keeping, export `FloodReports` (with `Location.Lat` / `Location.Long`) to CSV and import it
with `COPY`, building the point with `ST_SetSRID(ST_MakePoint(lng, lat), 4326)::geography`.
For test data, just re-seed.
