// Run with: npm run test:functions  (Node's built-in test runner, no extra packages)
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { buildSharePage, describeReport, escapeHtml, isReportId } from './share-page.mjs';

const ID = '01a1675d-8d3c-7df1-8f79-2fe928692607';
const ORIGIN = 'https://masidbaha.pages.dev';

const report = {
  id: ID,
  lat: 14.65,
  lng: 121.1,
  severity: 2,
  notes: 'Water is rising near the bridge',
  photoUrl: null,
  reportedAt: '2026-10-23T09:12:00Z',
  confidenceScore: 3,
  status: 1,
  city: 'Marikina',
  province: 'Metro Manila',
  region: 'NCR'
};

describe('isReportId', () => {
  it('accepts a guid and nothing else', () => {
    assert.equal(isReportId(ID), true);
    assert.equal(isReportId('../../etc/passwd'), false);
    assert.equal(isReportId('<script>'), false);
    assert.equal(isReportId(''), false);
    assert.equal(isReportId(undefined), false);
  });
});

describe('escapeHtml', () => {
  it('neutralises markup and quotes', () => {
    assert.equal(escapeHtml('<img src=x onerror="a()">&\''), '&lt;img src=x onerror=&quot;a()&quot;&gt;&amp;&#39;');
  });
});

describe('describeReport', () => {
  it('names the severity and place, and carries the notes and confirmations', () => {
    const { title, description } = describeReport(report, ORIGIN);

    assert.equal(title, 'Knee-deep flooding in Marikina, Metro Manila');
    assert.match(description, /Water is rising near the bridge/);
    assert.match(description, /3 confirmations/);
    assert.match(description, /PHT/);
  });

  it('uses the singular for one confirmation', () => {
    assert.match(describeReport({ ...report, confidenceScore: 1 }, ORIGIN).description, /1 confirmation\b/);
  });

  it('says so when a report is resolved or expired', () => {
    assert.match(describeReport({ ...report, status: 2 }, ORIGIN).title, /^Resolved: /);
    assert.match(describeReport({ ...report, status: 3 }, ORIGIN).title, /^Expired: /);
  });

  it("prefers the reporter's photo, else the branded card", () => {
    assert.equal(describeReport({ ...report, photoUrl: 'https://x.example/p.jpg' }, ORIGIN).image, 'https://x.example/p.jpg');
    assert.equal(describeReport(report, ORIGIN).image, `${ORIGIN}/og-default.png`);
  });

  it('shortens very long notes', () => {
    const { description } = describeReport({ ...report, notes: 'a'.repeat(400) }, ORIGIN);
    assert.ok(description.length < 330);
    assert.match(description, /…/);
  });
});

describe('buildSharePage', () => {
  it('carries the report in Open Graph and Twitter tags, and points on to the map', () => {
    const html = buildSharePage({ origin: ORIGIN, id: ID, report });

    assert.match(html, /<meta property="og:title" content="Knee-deep flooding in Marikina, Metro Manila">/);
    assert.match(html, new RegExp(`<meta property="og:url" content="${ORIGIN}/r/${ID}">`));
    assert.match(html, new RegExp(`<meta property="og:image" content="${ORIGIN}/og-default.png">`));
    assert.match(html, /<meta name="twitter:card" content="summary_large_image">/);
    assert.match(html, new RegExp(`http-equiv="refresh" content="0;url=/map\\?report=${ID}"`));
  });

  it('cannot be turned into script by what someone typed in the notes', () => {
    const html = buildSharePage({
      origin: ORIGIN,
      id: ID,
      report: { ...report, notes: '"><script>alert(1)</script>', city: '<b>x</b>' }
    });

    assert.equal(html.includes('<script>alert(1)</script>'), false);
    assert.equal(html.includes('<b>x</b>'), false);
    assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  });

  it('falls back to the standard card, and the plain map, when there is no report', () => {
    const html = buildSharePage({ origin: ORIGIN, id: ID, report: null });

    assert.match(html, /og:title" content="MasidBaha: real-time flood map/);
    assert.match(html, /url=\/map"/);
    assert.equal(html.includes('report='), false);
  });

  it('never puts a malformed id into a link', () => {
    const html = buildSharePage({ origin: ORIGIN, id: '"><x>', report });

    assert.equal(html.includes('<x>'), false);
    assert.equal(html.includes('report='), false);
  });
});
