const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  await p.goto('file://' + path.resolve(__dirname, 'screen.html'));
  await p.waitForTimeout(500);
  await p.screenshot({ path: process.argv[2] || 'out.png' });
  await b.close();
})();
