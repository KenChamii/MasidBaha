// Cloudflare Pages Function: forwards the API paths to the backend on Render, so the
// browser only ever talks to this site's own origin (no CORS, and the session cookies
// stay first-party). _routes.json limits it to /api, /hubs and /uploads, so static
// files never touch it.
//
// Settings, in the Pages project (Settings > Variables and Secrets):
//   API_ORIGIN    https://<your-service>.onrender.com   (no trailing slash needed)
//   PROXY_SECRET  the same value as Security__TrustedProxySecret on the API
export async function onRequest({ request, env }) {
  if (!env.API_ORIGIN || !env.PROXY_SECRET) {
    return new Response('The API proxy is not configured (API_ORIGIN / PROXY_SECRET).', { status: 500 });
  }

  const incoming = new URL(request.url);
  const target = new URL(incoming.pathname + incoming.search, env.API_ORIGIN);

  const headers = new Headers(request.headers);
  // The API only believes X-Client-IP when it comes with the secret. Both are set here,
  // so whatever a visitor sent in those headers is overwritten.
  headers.set('X-Proxy-Secret', env.PROXY_SECRET);
  headers.set('X-Client-IP', request.headers.get('CF-Connecting-IP') ?? '');

  // Passing the original request along keeps streaming bodies and WebSocket upgrades (SignalR) intact
  return fetch(new Request(target, new Request(request, { headers, redirect: 'manual' })));
}
