// Builds the page a shared report link opens first (/r/<id>). Facebook, Messenger, Viber, X and
// WhatsApp read a link's preview from the HTML they get, and they do not run JavaScript, so the
// single-page app alone would give every link the same bland card. This page carries the report's
// own title, description and photo in Open Graph tags, then sends people on to the map.
//
// Pure functions on purpose (no Cloudflare APIs): they are unit-tested with `npm run test:functions`.

const SEVERITY_NAME = { 1: 'Passable', 2: 'Knee-deep', 3: 'Waist-deep', 4: 'Impassable' };
const STATUS_NAME = { 2: 'Resolved', 3: 'Expired' };

// Report text is typed by strangers, so every value that reaches the HTML goes through this
export function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// Only a real report id gets as far as an API call or a URL
export function isReportId(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value ?? '');
}

function place(report) {
  return [report.city, report.province].filter(Boolean).join(', ') || report.region || 'the Philippines';
}

// Absolute time in Philippine time: a preview can be cached for a while, so "5 minutes ago" would go stale
function when(report) {
  return new Intl.DateTimeFormat('en-PH', {
    timeZone: 'Asia/Manila',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  }).format(new Date(report.reportedAt));
}

function clip(text, max) {
  const clean = String(text ?? '').replace(/\s+/g, ' ').trim();
  return clean.length > max ? clean.slice(0, max - 1).trimEnd() + '…' : clean;
}

// The words and image a link preview shows
export function describeReport(report, origin) {
  const state = STATUS_NAME[report.status];
  const severity = SEVERITY_NAME[report.severity] ?? 'Flooding';

  const title = `${state ? state + ': ' : ''}${severity} flooding in ${place(report)}`;

  const parts = [`Reported ${when(report)} (PHT)`, `${report.confidenceScore} confirmation${report.confidenceScore === 1 ? '' : 's'}`];
  const notes = clip(report.notes, 140);
  const description = (notes ? `“${notes}” · ` : '') + parts.join(' · ') + '. See it live on the MasidBaha flood map.';

  // The reporter's photo makes the best preview; without one, the branded card
  return { title, description, image: report.photoUrl || `${origin}/og-default.png` };
}

export const GENERIC = {
  title: 'MasidBaha: real-time flood map of the Philippines',
  description: 'Know before you go. Community flood reports, confirmed by neighbours, on a live map.'
};

// `report` is null when the id is unknown, hidden, or the API could not be reached: the link still
// opens the map with the standard card, so a share never turns into an error page
export function buildSharePage({ origin, id, report }) {
  const known = report ? describeReport(report, origin) : { ...GENERIC, image: `${origin}/og-default.png` };
  const target = report && isReportId(id) ? `/map?report=${encodeURIComponent(id)}` : '/map';
  const canonical = report && isReportId(id) ? `${origin}/r/${id}` : `${origin}/`;

  const title = escapeHtml(known.title);
  const description = escapeHtml(known.description);
  const image = escapeHtml(known.image);
  const url = escapeHtml(canonical);
  const next = escapeHtml(target);

  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>${title}</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="${description}">
<link rel="canonical" href="${url}">
<meta property="og:type" content="website">
<meta property="og:site_name" content="MasidBaha">
<meta property="og:title" content="${title}">
<meta property="og:description" content="${description}">
<meta property="og:url" content="${url}">
<meta property="og:image" content="${image}">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="${title}">
<meta name="twitter:description" content="${description}">
<meta name="twitter:image" content="${image}">
<meta http-equiv="refresh" content="0;url=${next}">
</head>
<body>
<p><a href="${next}">Open the report on the MasidBaha map</a></p>
<script>location.replace(${JSON.stringify(target).replace(/</g, '\\u003c')});</script>
</body>
</html>
`;
}
