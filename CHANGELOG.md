# Changelog

All notable changes to MasidBaha. Versions follow [Semantic Versioning](https://semver.org/);
commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

## [Unreleased]

### Added
- **A landing page that explains the app.** A mock of the report form, the path a report takes from phone to map, how the confidence score moves, the features, an architecture and spec section, privacy and an honest list of limits. In English and Filipino. The app is now described as a flood monitoring app, page titles and share cards use that wording, and the share image was redrawn to match.
- **Works on every screen.** The whole site was checked from 320 px phones to wide desktops, upright and
  sideways. The map toolbar no longer runs off the screen, panels become bottom sheets on phones, the report
  form and list never cover each other, analytics stacks its filters above the map, and the admin tables
  turn into cards on a phone. Uses the phone's dynamic viewport height and safe areas (notches), touch-sized
  buttons, and no zoom-on-focus in form fields.
- **English and Filipino.** A language button on the landing page and the map; a first visit follows the
  browser's language, and the choice is remembered. Everything on the public site is translated, including
  marker popups and the weather panel. A check script and unit tests fail the build when a text is missing in
  one language.
- **Clustered markers.** Reports close together become one numbered badge in the colour of the worst flood
  inside. Tapping it zooms in, and choosing a report from the list still opens it when it was inside a cluster.
- **Reports made without signal are kept and sent later.** The report is saved on the phone (photo included)
  and drawn on the map as waiting. It is sent by itself when the connection returns, even after a reload.
  Each report carries its own id, so the server saves it once even when a send is repeated after a lost
  answer (new nullable column and unique index). Sent while the app is open; there is no background sync.
- **Loading skeletons** instead of the bare word "Loading", in the report list, analytics, the admin tables
  and the dashboard. They stop shimmering for visitors who prefer reduced motion.
- **Watch areas.** Visitors choose up to five places (home, work, school) and are alerted only about new
  reports near them. Nobody signs up: the places belong to the anonymous session. Matching runs in
  PostGIS (`ST_DWithin` on a GiST index). People who never choose a place keep getting every alert.
- **Weather on the map.** Switch on rain radar (RainViewer, with a 2-hour playback), satellite rainfall
  (NASA IMERG) and typhoons: the storm's position, track, forecast cone and wind areas, from JTWC data
  through GDACS. Every layer is clearly marked as not official, with links to PAGASA's bulletins, because
  PAGASA publishes no map-data service. The typhoon data is refreshed by the API in the background (the
  source is slow) and saved to the database, so a restart serves it at once.
- **Shareable report links.** A "share" button on every report opens the phone's share sheet (or copies
  the link). The link, `/r/<id>`, shows a preview card on Facebook, Messenger, Viber and X with the
  report's severity, place and photo, then opens the map on that report. Served by a Cloudflare Pages
  Function, since link previews are read from HTML and not from the single-page app.
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
- **Photo uploads (security).** The upload endpoint decoded whatever format the bytes really were, so a GIF
  or TIFF sent with an image label reached those decoders and their known bugs. Only JPEG, PNG and WebP are
  decoded now, metadata is not parsed, and ImageSharp moves to 3.1.12. The remaining advisories are fixed only
  in ImageSharp 4, which needs a paid licence key; the restriction keeps the affected code out of reach.
- A damaged or cut-off image made the upload answer 500. It is now a 400.
- A report that staff hid (or that came from a shadow-banned source) could still be fetched by its id.
  It is now a 404 for everyone except the person who sent it.
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
