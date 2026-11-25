// Headless browser check of the published demos: loads the page, runs one operation per demo, saves a screenshot
// per demo and fails on any console error, page error or failed request.
//
//   node demos/G3Demos/e2e/smoke.mjs <publish>/wwwroot <three package dir> <screenshot dir> [cases.json filter out.txt]
//
// With the optional arguments it also runs the cases of cases.json whose name matches the regular expression `filter` in WebAssembly
// (JS interop into Probe.RunCase, the code path of the UI) and writes one line per case to out.txt, for a comparison
// with the same cases run natively (see README).
// <three package dir> is the unpacked `npm pack three@0.170.0` (the folder with build/ and examples/): the CDN URLs of
// the import map are served from it, so the check needs no network. Uses the pre-installed Playwright + Chromium.
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const [root, threeDir, shotDir, casesFile, caseFilter, caseOut] = process.argv.slice(2);
if (!root || !threeDir || !shotDir) {
    console.error('usage: node smoke.mjs <wwwroot> <three package dir> <screenshot dir>');
    process.exit(2);
}
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.dll': 'application/octet-stream', '.dat': 'application/octet-stream',
    '.pdb': 'application/octet-stream', '.webcil': 'application/octet-stream' };
const server = createServer(async (req, res) => {
    const path = normalize(decodeURIComponent(new URL(req.url, 'http://x').pathname)).replace(/^(\.\.[/\\])+/, '');
    try {
        const file = join(root, path.endsWith('/') ? path + 'index.html' : path);
        const body = await readFile(file);
        res.writeHead(200, { 'content-type': types[extname(file)] || 'application/octet-stream' });
        res.end(body);
    } catch {
        res.writeHead(404); res.end();
    }
}).listen(0);
const port = server.address().port;
await mkdir(shotDir, { recursive: true });

const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 860 }, deviceScaleFactor: 1 });
const problems = [];
const log = [];
page.on('console', m => { log.push(m.type() + ': ' + m.text()); if (m.type() === 'error') problems.push('console: ' + m.text()); });
page.on('pageerror', e => problems.push('pageerror: ' + e.message));
page.on('requestfailed', r => problems.push('requestfailed: ' + r.url()));
await page.route('https://cdn.jsdelivr.net/npm/three@0.170.0/**', async route => {
    const rel = new URL(route.request().url()).pathname.replace('/npm/three@0.170.0/', '');
    route.fulfill({ body: await readFile(join(threeDir, rel)), contentType: 'text/javascript' });
});

async function waitDone(label) {
    try {
        await page.waitForFunction(() => document.querySelector('#headline')?.dataset.state === 'done', null, { timeout: 60000 });
    } catch (e) {
        await page.screenshot({ path: join(shotDir, 'timeout.png') });
        console.error(`timeout waiting for ${label}\n` + log.slice(-30).join('\n') + '\n' + problems.join('\n'));
        process.exit(1);
    }
}

await page.goto(`http://localhost:${port}/`);
await waitDone('boolean (initial)');
for (const tab of ['boolean', 'repair', 'remesh']) {
    if (tab !== 'boolean') {
        await page.click(`#tab-${tab}`);
        await waitDone(tab);
    }
    // one more operation through the Run button
    await page.click('#run');
    await page.waitForTimeout(100);
    await waitDone(tab + ' (run)');
    await page.waitForTimeout(400);
    const headline = await page.textContent('#headline');
    const cls = await page.getAttribute('#headline', 'class');
    console.log(`${tab}: ${headline.trim()} [${cls}]`);
    await page.screenshot({ path: join(shotDir, `${tab}.png`) });
}
if (casesFile) {
    const { writeFile } = await import('node:fs/promises');
    const cases = JSON.parse(await readFile(casesFile, 'utf8')).filter(c => new RegExp(caseFilter || '').test(c.name));
    const lines = [];
    for (const c of cases) {
        const line = await page.evaluate(([d, j]) => DotNet.invokeMethod('G3Demos', 'RunCase', d, j), [c.demo, JSON.stringify(c.p)]);
        lines.push(`${c.name} ${line}`);
        console.log(`${c.name} ${line.slice(0, 160)}`);
    }
    await writeFile(caseOut, lines.join('\n') + '\n');
}
await browser.close();
server.close();
if (problems.length) {
    console.error('FAILED:\n' + problems.join('\n'));
    process.exit(1);
}
console.log('OK: no console errors');
