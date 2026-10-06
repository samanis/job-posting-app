// Explicitly creates one job against the running local APIs via both Angular clients.
const { chromium } = require('@playwright/test');
(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const title = `Browser integration check ${Date.now()}`;
    await page.goto('http://127.0.0.1:4200/jobs/new');
    for (const [id, value] of Object.entries({ title, department: 'Engineering', location: 'Toronto', description: 'Created through the real Angular form and projected through RabbitMQ.', salaryMin: '100000', salaryMax: '150000', closingDate: '2028-12-31' })) {
      await page.locator(`#${id}`).fill(value);
    }
    const response = page.waitForResponse(r => r.url().includes('/api/jobs') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Post job', exact: true }).click();
    const posted = await response;
    const body = await posted.json();
    if (posted.status() !== 202) throw new Error(`POST failed: ${posted.status()}`);
    await page.locator('.submission-status[data-state="saved"]').waitFor();
    console.log(`Angular POST and saved confirmation PASS: ${body.id}`);
    await page.goto(`http://127.0.0.1:4201/jobs?q=${encodeURIComponent(title)}`);
    const link = page.getByRole('link', { name: title, exact: true });
    await link.waitFor();
    await link.click();
    await page.getByRole('heading', { level: 1, name: title }).waitFor();
    if (errors.length) throw new Error(errors.join('; '));
    console.log('Angular search list/detail PASS; no browser page errors. Test job retained.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
