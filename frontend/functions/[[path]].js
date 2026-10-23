// Cloudflare Pages Function: forwards the API paths to the backend on Render, so the
// browser only ever talks to this site's own origin (no CORS, and the session cookies
// stay first-party). _routes.json limits it to /api, /hubs and /uploads, so static
// files never touch it.
//
// Settings, in the Pages project (Settings > Variables and Secrets):
//   API_ORIGIN    https://<your-service>.onrender.com   (no trailing slash needed)
//   PROXY_SECRET  the same value as Security__TrustedProxySecret on the API
export async function onRequest({ request, env }) {
  // Says which setting is missing (names only, never values), so the fix is obvious
  const missing = ['API_ORIGIN', 'PROXY_SECRET'].filter(name => !env[name]);
  if (missing.length > 0) {
    return new Response(`The API proxy is not configured: missing ${missing.join(' and ')}.`, { status: 500 });
  }

  const incoming = new URL(request.url);
  const target = new URL(incoming.pathname + incoming.search, env.API_ORIGIN);

  const headers = new Headers(request.headers);
  // The API only believes X-Client-IP when it comes with the secret. Both are set here,
  // so whatever a visitor sent in those headers is overwritten.
  headers.set('X-Proxy-Secret', env.PROXY_SECRET);
  headers.set('X-Client-IP', request.headers.get('CF-Connecting-IP') ?? '');
  // The address the visitor actually used, so the API builds links (uploaded photos) on this site
  // rather than on its own onrender.com host
  headers.set('X-Forwarded-Host', incoming.host);
  headers.set('X-Forwarded-Proto', incoming.protocol.replace(':', ''));

  // Passing the original request along keeps streaming bodies and WebSocket upgrades (SignalR) intact
  return fetch(new Request(target, new Request(request, { headers, redirect: 'manual' })));
}
