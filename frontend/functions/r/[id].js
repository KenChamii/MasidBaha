// Cloudflare Pages Function for shared report links: /r/<report id>.
// Looks the report up on the API and answers with a page whose Open Graph tags describe it
// (see functions-lib/share-page.mjs), which then moves the visitor on to the map.
//
// Only runs for the public site: _routes.json lists /r/* there and not on the admin site.
import { buildSharePage, isReportId } from '../../functions-lib/share-page.mjs';

const HTML = { 'Content-Type': 'text/html; charset=utf-8' };

export async function onRequest({ request, params, env }) {
  const incoming = new URL(request.url);
  const origin = incoming.origin;
  const id = String(params.id ?? '');

  let report = null;
  if (isReportId(id) && env.API_ORIGIN && env.PROXY_SECRET) {
    try {
      const response = await fetch(new URL(`/api/v1/flood-reports/${id}`, env.API_ORIGIN), {
        headers: {
          'X-Proxy-Secret': env.PROXY_SECRET,
          // So photo links in the answer are built on this site, not on the API's own host
          'X-Forwarded-Host': incoming.host,
          'X-Forwarded-Proto': 'https'
        },
        signal: AbortSignal.timeout(8000)
      });
      if (response.ok) report = await response.json();
    } catch {
      // A slow or sleeping API must not break a share: fall back to the standard card
    }
  }

  return new Response(buildSharePage({ origin, id, report }), {
    status: 200,
    headers: {
      ...HTML,
      // Short, so a resolved report soon says so; the crawlers cache on their side anyway
      'Cache-Control': report ? 'public, max-age=300' : 'public, max-age=60',
      'X-Robots-Tag': 'noindex'
    }
  });
}
