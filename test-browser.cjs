'use strict';
const { chromium } = require(process.env.RAIKOU_PLAYWRIGHT || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const root = path.join(__dirname, 'public'), output = path.join(__dirname, 'verification');

(async () => {
  await fs.mkdir(output, { recursive: true });
  const server = http.createServer(async (req, res) => {
    try {
      let url = new URL(req.url, 'http://localhost').pathname;
      if (url === '/raikou') { res.writeHead(308, { Location: '/raikou/' }); res.end(); return; }
      if (url.startsWith('/raikou/')) url = url.slice(7);
      if (url.endsWith('/')) url += 'index.html';
      const file = path.resolve(root, '.' + decodeURIComponent(url));
      if (!file.startsWith(root + path.sep)) { res.writeHead(403); res.end(); return; }
      const data = await fs.readFile(file);
      res.writeHead(200, { 'Content-Type': ({ '.html':'text/html', '.mjs':'text/javascript', '.css':'text/css', '.json':'application/json', '.png':'image/png' })[path.extname(file)] || 'application/octet-stream', 'Cache-Control':'no-cache' }); res.end(data);
    } catch { res.writeHead(404); res.end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1000, height: 760 } });
  const page = await context.newPage(), errors = [], external = [], csp = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('request', request => { if (!request.url().startsWith(base)) external.push(request.url()); });
  page.on('console', message => { if (/Content Security Policy|violates.*directive/.test(message.text())) csp.push(message.text()); });
  const manifest = JSON.parse(await fs.readFile(path.join(root, 'manifest.json')));
  const all = Object.fromEntries(await Promise.all(manifest.bands.map(async band => [band.id, JSON.parse(await fs.readFile(path.join(root, band.url)))])));
  const waitBands = () => page.waitForFunction(() => !document.querySelector('[data-band="Q2"]').disabled);
  const select = async band => { await page.locator(`[data-band="${band}"]`).click(); await page.locator('#route').waitFor({ state:'visible' }); assert.match(await page.locator('#route-title').textContent(), new RegExp('^' + band)); };
  const checkFinish = async (data, node) => {
    assert.equal(await page.locator('#responses').isVisible(), false);
    assert.equal(await page.locator('#origins span').textContent(), node.startingAdvances.join(', '));
    assert.match(await page.locator('[data-kind="finish"]').textContent(), /3484/);
    assert.equal(await page.locator('.step').count(), node.steps.length);
    assert.equal(await page.locator('[data-kind="entry"]').count(), 0);
  };
  try {
    await page.goto(base + '/'); await waitBands();
    assert.equal(await page.locator('[data-band]').count(), 4);
    assert.equal(await page.locator('input:not(#filter)').count(), 0);
    await page.screenshot({ path:path.join(output, 'bands-desktop.png'), fullPage:true });
    for (const band of manifest.bands) {
      if (await page.locator('#route').isVisible()) await page.locator('#band').click();
      await select(band.id);
      assert.equal(await page.locator('#origins').isVisible(), false);
      let node = all[band.id].nodes[0];
      assert.equal(await page.locator('.choice').count(), node.choices.length);
      await page.locator('#filter').fill('this response does not exist');
      assert.equal(await page.locator('#no-match').isVisible(), true);
      await page.locator('#clear-filter').click();
      await page.locator('#filter').fill('ceiling');
      assert.equal(await page.locator('.choice:visible').count(), 1);
      await page.locator('#clear-filter').click();
      await page.locator('#details-toggle').click(); assert.equal(await page.locator('#details').isVisible(), true);
      await page.locator('#details-toggle').click();
      if (band.id === 'Q2') {
        await page.screenshot({ path:path.join(output, 'responses-desktop.png'), fullPage:true });
        const geometry = await page.locator('.choice').first().evaluate(button => {
          const a=button.getBoundingClientRect(), b=button.querySelector('.emote').getBoundingClientRect();
          return { height:b.height, offset:Math.abs((a.top+a.bottom-b.top-b.bottom)/2) };
        });
        assert.equal(geometry.height, 24); assert(geometry.offset < 5, JSON.stringify(geometry));
      }
      while (!node.terminal) {
        await page.locator('.choice').first().click(); node = all[band.id].nodes[node.choices[0].next];
      }
      await checkFinish(all[band.id], node);
      if (band.id === 'Q2') await page.screenshot({ path:path.join(output, 'finish-desktop.png'), fullPage:true });
      await page.locator('#back').click(); assert.equal(await page.locator('#origins').isVisible(), false);
      await page.locator('#restart').click(); assert.match(await page.locator('#summary').textContent(), /214 starts · 0 observations/);
      assert.equal(await page.locator('#back').isDisabled(), true);
    }
    // A real low-counter finish with BONK, selected through its observed responses.
    await page.locator('#band').click(); await select('Q3');
    function pathTo(data, id, origin) {
      const node = data.nodes[id];
      if (node.terminal) return node.startingAdvances.includes(origin) ? [] : null;
      for (let i=0;i<node.choices.length;i++) { const tail=pathTo(data,node.choices[i].next,origin); if(tail) return [i,...tail]; }
      return null;
    }
    for (const i of pathTo(all.Q3, 0, 3291)) await page.locator('.choice').nth(i).click();
    assert.match(await page.locator('#origins').textContent(), /3291/);
    assert.match(await page.locator('[data-kind="finish"]').textContent(), /BONK ↑; ← Raikou/);
    await page.screenshot({ path:path.join(output, 'bonk-finish.png'), fullPage:true });
    // Same files deployed beneath a path, including redirect and relative images.
    await page.goto(base + '/raikou'); await waitBands(); assert(page.url().endsWith('/raikou/'));
    await page.setViewportSize({ width:375, height:812 });
    await page.screenshot({ path:path.join(output, 'bands-mobile.png'), fullPage:true });
    await select('Q2');
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    await page.waitForFunction(() => [...document.images].every(image => image.complete && image.naturalWidth > 0));
    await page.screenshot({ path:path.join(output, 'responses-mobile.png'), fullPage:true });
    await page.locator('#route-title').focus(); await page.keyboard.press('1');
    assert.match(await page.locator('#summary').textContent(), /1 observation$/);
    await page.keyboard.press('Alt+ArrowLeft'); assert.match(await page.locator('#summary').textContent(), /0 observations/);
    await page.keyboard.press('Control+f'); assert.equal(await page.locator('#filter').evaluate(el => el === document.activeElement), true);
    await page.locator('#filter').fill('1'); assert.match(await page.locator('#summary').textContent(), /0 observations/);
    await page.locator('#clear-filter').click();
    // Explicit failure and retry, without discarding or silently substituting bands.
    await page.route('**/manifest.json', route => route.fulfill({ status:503, body:'Unavailable' }));
    await page.reload(); await page.locator('#retry').waitFor({ state:'visible' });
    assert.equal(await page.locator('[data-band="Q2"]').isDisabled(), true);
    await page.unroute('**/manifest.json'); await page.locator('#retry').click(); await waitBands();
    await page.route('**/data/q2.*.json', route => route.fulfill({ status:503, body:'Unavailable' }));
    await page.locator('[data-band="Q2"]').click(); await page.locator('#retry').waitFor({ state:'visible' });
    assert.equal(await page.locator('#route').isVisible(), false);
    await page.screenshot({ path:path.join(output,'retry-mobile.png'), fullPage:true });
    await page.unroute('**/data/q2.*.json'); await page.locator('#retry').click(); await page.locator('#route').waitFor({ state:'visible' });
    // A delayed Q2 response cannot replace a later Q4 selection.
    await page.reload(); await waitBands();
    let release, started;
    const delay = new Promise(resolve => release=resolve), pending = new Promise(resolve => started=resolve);
    await page.route('**/data/q2.*.json', async route => { started(); await delay; try { await route.continue(); } catch {} });
    await page.locator('[data-band="Q2"]').click(); await pending;
    await select('Q4'); release(); await page.unroute('**/data/q2.*.json');
    assert.match(await page.locator('#route-title').textContent(), /^Q4/);
    assert.deepEqual(errors, []); assert.deepEqual(external, []); assert.deepEqual(csp, []);
    const report={passed:true,bands:4,rootAndSubpath:true,mobile:true,staticEmotes:true,backRestartFilter:true,bonk:true,failureRetry:true,staleLoad:true,pageErrors:errors,externalRequests:external,cspErrors:csp};
    await fs.writeFile(path.join(output,'browser-tests.json'),JSON.stringify(report,null,2)); console.log(JSON.stringify(report));
  } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode=1; });
