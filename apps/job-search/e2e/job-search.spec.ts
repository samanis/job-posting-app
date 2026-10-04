import { expect, test } from '@playwright/test';
import { job, json, mockJobs, summary } from './fixtures.js';

test.beforeEach(async ({ page }, info) => {
  if (!info.title.startsWith('debounces')) await page.clock.setFixedTime(new Date('2026-10-04T15:00:00Z'));
});

test('lists summaries, opens authoritative details and returns to cached card focus', async ({ page }) => {
  const requests = await mockJobs(page); await page.goto('/jobs?q=Platform');
  await expect(page.locator('.job-card')).toHaveCount(1); expect(requests).toHaveLength(1);
  await page.screenshot({ path: test.info().outputPath('results.png'), fullPage: true });
  await page.getByRole('link', { name: job.title }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(job.title);
  await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
  await expect(page.locator('.description')).toHaveText(job.description); await expect(page.locator('b')).toHaveCount(0);
  await expect(page.getByText('January 1, 2027')).toBeVisible(); await expect(page).toHaveTitle(`${job.title} | Job board`);
  await page.screenshot({ path: test.info().outputPath('details.png'), fullPage: true });
  await page.getByRole('link', { name: 'Back to jobs' }).click();
  await expect(page).toHaveURL(/\/jobs\?q=Platform$/); await expect(page.getByRole('link', { name: job.title })).toBeFocused();
  expect(requests).toHaveLength(2);
});

test('debounces rapid edits once and Enter/Clear commit immediately without a second query', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-04T15:00:00Z') });
  const requests = await mockJobs(page); await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  await page.clock.pauseAt(new Date('2026-10-04T15:00:10Z'));
  await page.getByLabel('Keywords').fill('P'); await page.getByLabel('Keywords').fill('Platform');
  await page.clock.runFor(299); expect(requests).toHaveLength(1);
  await page.clock.runFor(20); await expect(page).toHaveURL(/q=Platform/); await expect.poll(() => requests.length).toBe(2);
  await page.getByLabel('Keywords').fill('Next'); await page.getByLabel('Keywords').press('Enter');
  await page.clock.runFor(20); await expect(page).toHaveURL(/q=Next/); await expect.poll(() => requests.length).toBe(3);
  await page.clock.runFor(400); expect(requests).toHaveLength(3);
  await page.getByRole('button', { name: 'Clear filters' }).click(); await page.clock.runFor(20);
  await expect(page).toHaveURL(/\/jobs$/); await expect(page.getByLabel('Keywords')).toHaveValue('');
  expect(requests).toHaveLength(3); // Empty criteria is a fresh cached revisit.
});

test('ignores IME partial text and commits the finished composition', async ({ page }) => {
  const requests = await mockJobs(page); await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  const input = page.getByLabel('Keywords'); await input.dispatchEvent('compositionstart'); await input.fill('partial'); await input.press('Enter');
  expect(requests).toHaveLength(1); await expect(page).toHaveURL(/\/jobs$/);
  await input.fill('完成'); await input.dispatchEvent('compositionend');
  await expect(page).toHaveURL(/q=/); await expect.poll(() => requests.length).toBe(2); expect(requests[1].searchParams.get('q')).toBe('完成');
});

test('delayed obsolete query cannot replace the latest matches', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => release = resolve);
  let arrived!: () => void; const started = new Promise<void>(resolve => arrived = resolve);
  await page.route('**/api/jobs**', async route => {
    const q = new URL(route.request().url()).searchParams.get('q');
    if (q === 'old') { arrived(); await gate; await json(route, { items: [{ ...summary(), title: 'Old matches' }], nextCursor: null }).catch(() => undefined); }
    else await json(route, { items: [{ ...summary(), title: q === 'new' ? 'Latest matches' : job.title }], nextCursor: null });
  });
  await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  await page.getByLabel('Keywords').fill('old'); await page.getByLabel('Keywords').press('Enter'); await started;
  await page.getByLabel('Keywords').fill('new'); await page.getByLabel('Keywords').press('Enter');
  await expect(page.getByRole('link', { name: 'Latest matches' })).toBeVisible(); release();
  await expect(page.getByRole('link', { name: 'Old matches' })).toHaveCount(0);
});

test('paginates, restores browser Back/Forward and resets cursor on sort change', async ({ page }) => {
  const requests: URL[] = [];
  await page.route('**/api/jobs**', async route => {
    const url = new URL(route.request().url()); requests.push(url);
    await json(route, { items: [{ ...summary(), title: url.searchParams.has('cursor') ? 'Second page' : 'First page' }], nextCursor: url.searchParams.has('cursor') ? null : 'next' });
  });
  await page.goto('/jobs'); await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Second page' })).toBeVisible();
  await page.goBack(); await expect(page.getByRole('link', { name: 'First page' })).toBeVisible();
  await page.goForward(); await expect(page.getByRole('link', { name: 'Second page' })).toBeVisible(); expect(requests).toHaveLength(2);
  await page.getByLabel('Sort by').selectOption('closing-soon'); await expect(page).toHaveURL(/sort=closing-soon/);
  await expect(page.getByRole('link', { name: 'First page' })).toBeVisible(); expect(requests[2].searchParams.has('cursor')).toBe(false);
  await page.getByLabel('Jobs per page').selectOption('50'); await expect.poll(() => requests.length).toBe(4); expect(requests[3].searchParams.get('limit')).toBe('50');
});

test('refresh discovers a new mocked job while cached revisit needs no read', async ({ page }) => {
  let calls = 0;
  await page.route('**/api/jobs**', route => { calls++; return json(route, { items: calls > 1 ? [summary(), { ...summary('job-2'), title: 'New mocked job' }] : [summary()], nextCursor: null }); });
  await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  await page.getByRole('button', { name: 'Refresh results' }).click(); await expect(page.locator('.job-card')).toHaveCount(2); expect(calls).toBe(2);
});

test('direct detail links are safe and unavailable records are explained', async ({ page }) => {
  await page.route('**/api/jobs**', route => json(route, {}, 404));
  await page.goto('/jobs/removed?returnTo=https://evil.example');
  await expect(page.getByRole('alert')).toHaveText('This job is no longer available.');
  await expect(page.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/jobs');
  await expect(page.locator('article')).toHaveCount(0);
});

test('empty results differ from failures and fit the viewport', async ({ page }) => {
  await page.route('**/api/jobs**', route => json(route, { items: [], nextCursor: null })); await page.goto('/jobs');
  await expect(page.getByRole('status')).toHaveText('No jobs match these filters.'); await expect(page.getByRole('alert')).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath('empty.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const response of ['202', 'malformed', '400', '422', '500', 'network'] as const) {
  test(`handles ${response} without empty-success claims and recovers explicitly`, async ({ page }) => {
    let calls = 0;
    await page.route('**/api/jobs**', async route => {
      calls++;
      if (calls > 1) return json(route, { items: [summary()], nextCursor: null });
      if (response === 'network') return route.abort('failed');
      return json(route, response === 'malformed' ? { bad: 'body' } : {}, response === 'malformed' ? 200 : Number(response));
    });
    await page.goto('/jobs'); await expect(page.getByRole('alert')).toBeVisible(); await expect(page.locator('.job-card')).toHaveCount(0);
    await expect(page.getByRole('status')).not.toHaveText('No jobs match these filters.'); expect(calls).toBe(1);
    if (response === '500') await page.screenshot({ path: test.info().outputPath('error.png'), fullPage: true });
    await page.getByRole('button', { name: 'Retry', exact: true }).click(); await expect(page.locator('.job-card')).toHaveCount(1); expect(calls).toBe(2);
  });
}

test('429 blocks early retry and allows retry at the server deadline', async ({ page }) => {
  let calls = 0;
  await page.route('**/api/jobs**', route => { calls++; return calls === 1 ? json(route, {}, 429, { 'Retry-After': '2' }) : json(route, { items: [summary()], nextCursor: null }); });
  await page.goto('/jobs'); await expect(page.getByRole('alert')).toContainText('Too many searches');
  await page.getByRole('button', { name: 'Retry', exact: true }).click(); expect(calls).toBe(1);
  await page.clock.setFixedTime(new Date('2026-10-04T15:00:02Z')); await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.locator('.job-card')).toHaveCount(1); expect(calls).toBe(2);
});

test('keyboard-only search, detail and return navigation preserves useful focus', async ({ page }) => {
  await mockJobs(page); await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  await page.keyboard.press('Tab'); await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  await page.keyboard.press('Enter'); await expect(page.getByRole('main')).toBeFocused();
  await page.keyboard.press('Tab'); await expect(page.getByLabel('Keywords')).toBeFocused();
  await page.keyboard.type('Platform'); await page.keyboard.press('Enter'); await expect(page).toHaveURL(/q=Platform/);
  for (let i = 0; i < 7; i++) await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: job.title })).toBeFocused(); await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
  // Back-to-jobs precedes the focused heading in document order.
  await page.keyboard.press('Shift+Tab'); await expect(page.getByRole('link', { name: 'Back to jobs' })).toBeFocused(); await page.keyboard.press('Enter');
  await expect(page.getByRole('link', { name: job.title })).toBeFocused();
});

test('long job text wraps in list/detail without horizontal overflow', async ({ page }) => {
  const longJob = { ...job, title: 'LongTitle'.repeat(60), location: 'LongLocation'.repeat(50), description: '<b>Plain text</b>\n' + 'Description'.repeat(200) };
  await page.route('**/api/jobs**', route => json(route, new URL(route.request().url()).pathname === '/api/jobs' ? { items: [longJob], nextCursor: null } : longJob));
  await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.locator('[data-job-id]').click(); await expect(page.locator('.description')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('long-details.png'), fullPage: true });
});
