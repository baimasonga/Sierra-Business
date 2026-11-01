'use strict';
// Drives the installed Rokela Business Admin window for scripts/accept-windows-install.ps1, through the WebView2 remote-debugging port
// that script enables for its own launch only. Steps:
//   setup    first launch: create the owner account using the keyboard only
//   signin   sign in with the keyboard (after a restart or upgrade, this proves the account and data were kept)
//   inspect  sign in, plus accessibility (axe-core, WCAG 2.1 A/AA), visible keyboard focus and display-scaling checks
// The owner email and password come from ACCEPT_EMAIL and ACCEPT_PASSWORD and are never written out.
// Needs playwright-core and axe-core on NODE_PATH.
const { chromium } = require('playwright-core');
const fs = require('fs');
const path = require('path');

const args = {};
for (let i = 2; i < process.argv.length; i += 2) args[process.argv[i].replace(/^--/, '')] = process.argv[i + 1];
const { step, port, out, label } = args;
const email = process.env.ACCEPT_EMAIL;
const password = process.env.ACCEPT_PASSWORD;
const result = { step, label, checks: [], accessibility: {}, focus: {}, scaling: [] };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function check(name, passed, detail) {
  result.checks.push({ name, passed: !!passed, detail });
  console.log(`${passed ? 'PASS' : 'FAIL'}: ${name} - ${detail}`);
  if (!passed) throw new Error(`${name}: ${detail}`);
}

async function connect() {
  const deadline = Date.now() + 90_000;
  let last;
  while (Date.now() < deadline) {
    try {
      return await chromium.connectOverCDP(`http://127.0.0.1:${port}`, { timeout: 10_000 });
    } catch (error) {
      last = error;
      await sleep(1000);
    }
  }
  throw new Error(`Could not reach the application window: ${last && last.message}`);
}

async function appPage(browser) {
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    for (const context of browser.contexts())
      for (const page of context.pages()) if (/^https?:/.test(page.url())) return page;
    await sleep(500);
  }
  throw new Error('The application page did not appear.');
}

async function shot(page, name) {
  await page.screenshot({ path: path.join(out, `${label}-${name}.png`) });
}

async function audit(page, name) {
  await page.addScriptTag({ path: require.resolve('axe-core/axe.min.js') });
  const { violations } = await page.evaluate(() =>
    window.axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] }, resultTypes: ['violations'] }),
  );
  result.accessibility[name] = violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    help: v.help,
    count: v.nodes.length,
    examples: v.nodes.slice(0, 3).map((n) => n.target.join(' ')),
  }));
  console.log(`Accessibility (${name}): ${violations.length ? violations.map((v) => `${v.id} [${v.impact}] x${v.nodes.length}`).join(', ') : 'no violations'}`);
}

// Describe the focused element and whether the focus is visible (an outline or a focus shadow).
const focused = (page) =>
  page.evaluate(() => {
    const el = document.activeElement;
    if (!el || el === document.body) return null;
    const style = getComputedStyle(el);
    const outline = style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
    const shadow = style.boxShadow && style.boxShadow !== 'none';
    const name = (el.getAttribute('aria-label') || el.labels?.[0]?.textContent || el.textContent || el.getAttribute('type') || '').trim().slice(0, 60);
    return { tag: el.tagName.toLowerCase(), type: el.getAttribute('type'), name, visible: !!(outline || shadow) };
  });

async function tabTo(page, selector, stops) {
  for (let i = 1; i <= 25; i++) {
    await page.keyboard.press('Tab');
    const info = await focused(page);
    if (info) stops.push(info);
    if (await page.evaluate((s) => document.activeElement?.matches(s), selector)) return i;
  }
  return 0;
}

async function signInWithKeyboard(page, firstRun) {
  const heading = (await page.locator('.login-panel h1').innerText()).trim();
  check('Expected screen', heading === (firstRun ? 'Set up your business.' : 'Welcome back.'), heading);
  const stops = [];
  const presses = await tabTo(page, 'input[type=email]', stops);
  check('Email field reachable with Tab', presses > 0, `${presses} presses`);
  await page.keyboard.type(email);
  await page.keyboard.press('Tab');
  check('Tab moves to Password', await page.evaluate(() => document.activeElement?.matches('input[type=password]')), 'Password field focused');
  await page.keyboard.type(password);
  if (firstRun) {
    await page.keyboard.press('Tab');
    check('Tab moves to Confirm password', await page.evaluate(() => document.activeElement?.matches('input[autocomplete=new-password]')), 'Confirm field focused');
    await page.keyboard.type(password);
  }
  result.focus.login = stops.concat((await focused(page)) || []);
  await page.keyboard.press('Enter');
  await page.waitForSelector('#business-navigation', { timeout: 60_000 });
  check(firstRun ? 'Owner account created and signed in' : 'Signed in', await page.getByRole('button', { name: 'Sign out' }).isVisible(), 'Workspace with Sign out');
}

async function inspectWorkspace(page) {
  await shot(page, 'workspace');
  await audit(page, 'workspace');
  const stops = [];
  await page.evaluate(() => document.activeElement?.blur());
  for (let i = 0; i < 20; i++) {
    await page.keyboard.press('Tab');
    const info = await focused(page);
    if (info) stops.push(info);
  }
  result.focus.workspace = stops;
  const hidden = stops.filter((s) => !s.visible);
  console.log(`Focus: ${stops.length} workspace tab stops, ${hidden.length} without a visible focus indicator`);

  // Windows display scaling shrinks the space the window has, in CSS pixels. Emulate common laptop and desktop settings.
  const cdp = await page.context().newCDPSession(page);
  const settings = [
    { name: '100% (1280 x 850 window)', width: 1280, height: 850, scale: 1 },
    { name: '125% on a 1366 x 768 laptop', width: 1093, height: 560, scale: 1.25 },
    { name: '150% on a 1920 x 1080 screen', width: 1280, height: 670, scale: 1.5 },
    { name: '200% on a 1920 x 1080 screen', width: 960, height: 500, scale: 2 },
  ];
  for (const s of settings) {
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: s.width, height: s.height, deviceScaleFactor: s.scale, mobile: false });
    await sleep(700);
    const layout = await page.evaluate(() => {
      const nav = document.querySelector('#business-navigation')?.getBoundingClientRect();
      const heading = document.querySelector('main h1, h1')?.getBoundingClientRect();
      const visible = (r) => !!r && r.width > 0 && r.height > 0 && r.left >= 0 && r.right <= window.innerWidth + 1;
      return {
        horizontalScroll: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        navigationVisible: visible(nav),
        headingVisible: visible(heading),
      };
    });
    result.scaling.push({ ...s, ...layout });
    await page.screenshot({ path: path.join(out, `${label}-scale-${Math.round(s.scale * 100)}.png`) });
    console.log(`Scaling ${s.name}: ${JSON.stringify(layout)}`);
  }
  await cdp.send('Emulation.clearDeviceMetricsOverride');
  // Every setting is measured and photographed first, so a failure still leaves the whole picture in the report.
  for (const s of result.scaling)
    check(`Layout at ${s.name}`, !s.horizontalScroll && s.navigationVisible && s.headingVisible,
      `horizontal scroll ${s.horizontalScroll}, navigation visible ${s.navigationVisible}, heading visible ${s.headingVisible}`);
}

(async () => {
  let exitCode = 0;
  try {
    if (!email || !password) throw new Error('ACCEPT_EMAIL and ACCEPT_PASSWORD are required.');
    fs.mkdirSync(out, { recursive: true });
    const browser = await connect();
    const page = await appPage(browser);
    await page.waitForSelector('.login-panel form, #business-navigation', { timeout: 120_000 });
    await shot(page, 'start');
    if (step === 'inspect') await audit(page, 'sign-in');
    if (step === 'setup') await audit(page, 'owner-setup');
    await signInWithKeyboard(page, step === 'setup');
    if (step === 'inspect') await inspectWorkspace(page);
    else await shot(page, 'workspace');
  } catch (error) {
    exitCode = 1;
    result.error = error.message;
    console.error(`Stopped: ${error.message}`);
  } finally {
    fs.writeFileSync(path.join(out, `${label}-${step}.json`), JSON.stringify(result, null, 2));
  }
  process.exit(exitCode);
})();
