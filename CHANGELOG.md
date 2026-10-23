# Changelog

All notable changes to MasidBaha. Versions follow [Semantic Versioning](https://semver.org/);
commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

## [Unreleased]

### Added
- **Watch areas.** Visitors choose up to five places (home, work, school) and are alerted only about new
  reports near them. Nobody signs up: the places belong to the anonymous session. Matching runs in
  PostGIS (`ST_DWithin` on a GiST index). People who never choose a place keep getting every alert.
- **Area-limited announcements.** Staff can send a broadcast only to people who watch a place inside a
  chosen circle (for example, a typhoon warning for Metro Manila).
- Docker images for the API and for each Angular app (nginx), and a `docker compose --profile app up`
  that runs the whole stack with one command.
- GitHub Actions: CI (backend tests, frontend lint, tests and build, Docker builds), CodeQL, Dependabot
  and an automatic deploy of both sites to Cloudflare Pages.
- Deployment to Neon (database), Render (API) and Cloudflare Pages (sites), with a Pages Function that
  proxies the API so cookies stay first-party. See `docs/deploy.md`.
- `Security:TrustedProxySecret`: lets the Cloudflare proxy pass the visitor's real IP to the API for rate
  limits and bans, only when it presents the shared secret.

### Fixed
- Uploaded photos could not be attached to a report on Linux servers (a path starting with `/` was
  mistaken for an absolute URL).
- The OpenStreetMap map tiles were blocked on the deployed sites by an overly strict referrer policy.

## 0.4.0 - Admin portal

### Added
- Staff accounts: a SuperAdmin creates, changes the role of, deactivates and resets staff. Temporary
  passwords must be changed at first sign-in. The last active SuperAdmin cannot be removed.
- Two-factor sign-in with an authenticator app and one-time recovery codes.
- Moderation queue with automatic flags (low-trust reporter, many "it's gone" votes, reporter flooding)
  and bulk actions.
- Reports table with search, filters, sorting and CSV export; report detail with votes, reporter trust,
  bans and history; admin map.
- Session and IP bans: Block refuses a reporter, Shadow accepts but hides their reports. IPs are kept only
  as a keyed hash.
- Dashboard with KPIs and charts, broadcast push to all subscribers, runtime settings stored in the
  database, and an audit log of every staff action.

### Changed
- Admin API code moved out of the shared library into the admin app.

## 0.3.0 - Frontend workspace

### Changed
- Angular upgraded from 18 to 22 (TypeScript 6).
- The frontend is now a workspace of three projects: `web` (public PWA), `admin` and `shared`.
- ESLint, Prettier, husky and lint-staged added; unit tests run on Vitest.

## 0.2.0 - Backend hardening and staff login

### Added
- ASP.NET Identity for back-office accounts (roles SuperAdmin, Moderator, Viewer) with cookie sign-in,
  lockout and rate-limited attempts. Replaces the shared admin API key.
- Anonymous visitors get a signed `HttpOnly` session cookie; ids sent in request bodies are ignored.
- Typed and validated options, FluentValidation, RFC 7807 problem details, API versioning (`/api/v1`),
  Serilog, health checks and OpenTelemetry.
- Photos can be stored on S3-compatible storage (Cloudflare R2).

## 0.1.0 - Platform and design

### Added
- Migrated from SQL Server to PostgreSQL + PostGIS with EF Core (`geography` points, GiST index,
  `ST_DWithin` radius search).
- Redesigned UI with a flood/weather palette, dark mode, new logo and icons.

### Fixed
- A "Deny" vote added 255 to a report's confidence score instead of subtracting one.
- Secrets that had been committed to the repository were removed from tracked files and must be rotated.
