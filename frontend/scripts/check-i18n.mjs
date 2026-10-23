// Fails when a template or component asks for a message that no dictionary has, or a message is never used.
// Run with: npm run check:i18n
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const web = 'projects/web/src';
const shared = 'projects/shared/src';

function walk(dir, files = []) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, files);
    else if (/\.(html|ts)$/.test(name) && !/\.spec\.ts$/.test(name)) files.push(path);
  }
  return files;
}

// 'a.b.c': 'text'   (the English list is the first block in each file)
function keysOf(file) {
  const text = readFileSync(file, 'utf8');
  const enBlock = text.slice(text.indexOf('en: {'), text.indexOf('fil: {'));
  return new Set([...enBlock.matchAll(/^\s*'([\w.]+)':/gm)].map(match => match[1]));
}

const known = new Set([
  ...keysOf(`${web}/app/core/i18n/messages.ts`),
  ...keysOf(`${shared}/lib/i18n/shared-messages.ts`)
]);

const used = new Map();
const patterns = [
  /'([a-z][\w]*(?:\.[\w]+)+)'\s*\|\s*t\b/g,
  /\bt\(\s*'([a-z][\w]*(?:\.[\w]+)+)'/g,
  /\bi18n\.t\(\s*'([a-z][\w]*(?:\.[\w]+)+)'/g
];
for (const file of [...walk(web), ...walk(shared)]) {
  const text = readFileSync(file, 'utf8');
  for (const pattern of patterns) for (const match of text.matchAll(pattern)) used.set(match[1], file);
  // keys held in data and looked up later: title: 'feature.report.title', photoError.set('form.err.type'), label: 'scope.city'
  for (const match of text.matchAll(/'((?:feature|gallery|scope|form\.err|push|map|weather)\.[\w.]+)'/g))
    used.set(match[1], file);
}

// Looked up by name at run time: 'severity.' + number, 'analytics.range.' + range, the shared theme/time texts
const dynamic = [/^severity\.\d$/, /^analytics\.range\.\w+$/, /^offline\.queued\./, /^theme\./, /^time\./];

const missing = [...used].filter(([key]) => !known.has(key));
for (const [key, file] of missing) console.error(`missing text: '${key}' (used in ${file})`);

const unused = [...known].filter(key => !used.has(key) && !dynamic.some(rule => rule.test(key)));
for (const key of unused) console.error(`unused text: '${key}'`);

if (missing.length || unused.length) process.exit(1);
console.log(`i18n ok: ${known.size} texts, ${used.size} used`);
