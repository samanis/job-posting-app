import { expect, Page, test } from '@playwright/test';

const payload = { title: 'Engineer', department: 'Platform', location: 'Toronto', description: 'Build software', salaryMin: 1000, salaryMax: 2000, closingDate: '2027-01-01' };
const saved = { ...payload, id: 'saved-job', title: 'API title', description: '<b>Plain text</b>\nSecond line', createdAt: '2026-10-04T15:00:00Z' };
async function fill(page: Page) {
  for (const [field, value] of Object.entries(payload)) await page.locator(`#${field}`).fill(String(value));
}

test.beforeEach(async ({ page }) => {
  await page.clock.setFixedTime(new Date('2026-10-04T15:00:00Z'));
  await page.goto('/');
});

test('validates required fields, salary bounds and future closing date without POST', async ({ page }) => {
  const requests: string[] = [];
  await page.route('**/api/jobs', route => { requests.push(route.request().url()); return route.abort(); });
  await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.locator('#title')).toBeFocused();
  await expect(page.getByRole('heading', { name: 'Check the job details' })).toBeVisible();
  await fill(page);
  await page.locator('#salaryMax').fill('1000');
  await page.locator('#closingDate').fill('2026-10-04');
  await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.locator('#salaryMin-errors')).toContainText('less than');
  await expect(page.locator('#closingDate-errors')).toContainText('later than today');
  await page.locator('#salaryMin').fill('1.234');
  await expect(page.locator('#salaryMin-errors')).toContainText('two decimal');
  expect(requests).toHaveLength(0);
});

test('shows API record safely, restores saved confirmation on refresh, and starts another job by keyboard', async ({ page }) => {
  await page.route('**/api/jobs', route => route.fulfill({ status: 201, json: saved }));
  await fill(page); await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Job saved' })).toBeFocused();
  await expect(page.locator('app-saved-job-confirmation')).toContainText('API title');
  await expect(page.locator('app-saved-job-confirmation')).toContainText('<b>Plain text</b>');
  await expect(page.locator('app-saved-job-confirmation b')).toHaveCount(0);
  await expect(page.locator('app-saved-job-confirmation')).toContainText('January 1, 2027');
  await page.screenshot({ path: test.info().outputPath('confirmation.png'), fullPage: true });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Job saved' })).toBeVisible();
  const another = page.getByRole('button', { name: 'Post another job' });
  await another.focus(); await page.keyboard.press('Enter');
  await expect(page.locator('#title')).toHaveValue(''); await expect(page.locator('#title')).toBeFocused();
});

test('maps server errors, preserves the draft, and correction uses a fresh key', async ({ page }) => {
  const keys: string[] = [];
  await page.route('**/api/jobs', route => {
    keys.push(route.request().headers()['idempotency-key']);
    return route.fulfill(keys.length === 1 ? { status: 422, json: { errors: { Title: ['Server title error'], Other: ['General error'] } } } : { status: 201, json: saved });
  });
  await fill(page); await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.locator('#title-errors')).toContainText('Server title error');
  await expect(page.locator('.error-summary')).toContainText('General error');
  await expect(page.locator('#description')).toHaveValue('Build software');
  await page.locator('#title').fill('Corrected'); await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Job saved' })).toBeVisible();
  expect(keys).toHaveLength(2); expect(keys[0]).toBeTruthy(); expect(keys[1]).not.toBe(keys[0]);
});

for (const failure of ['server', 'network', 'accepted'] as const) {
  test(`preserves key and payload after ${failure} and refresh without an automatic POST`, async ({ page }) => {
    const requests: { key: string; body: unknown }[] = [];
    await page.route('**/api/jobs', route => {
      requests.push({ key: route.request().headers()['idempotency-key'], body: route.request().postDataJSON() });
      if (requests.length > 1) return route.fulfill({ status: 200, json: saved });
      if (failure === 'network') return route.abort('failed');
      return route.fulfill({ status: failure === 'server' ? 503 : 202, json: {} });
    });
    await fill(page); await page.getByRole('button', { name: 'Post job', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Retry same submission' })).toBeEnabled();
    await expect(page.locator('#title')).toHaveAttribute('readonly', '');
    await page.reload(); await expect(page.getByRole('button', { name: 'Retry same submission' })).toBeEnabled();
    expect(requests).toHaveLength(1);
    await page.getByRole('button', { name: 'Retry same submission' }).click();
    await expect(page.getByRole('heading', { name: 'Job saved' })).toBeVisible();
    expect(requests).toHaveLength(2); expect(requests[1]).toEqual(requests[0]); expect(requests[0].body).toEqual(payload);
  });
}

test('blocks rapid repeated submission while the response is delayed', async ({ page }) => {
  let count = 0;
  let release!: () => void;
  const response = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/jobs', async route => { count++; await response; await route.fulfill({ status: 201, json: saved }); });
  await fill(page); await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Post job', exact: true })).toBeDisabled();
  await page.locator('form').dispatchEvent('submit');
  await expect.poll(() => count).toBe(1); release();
  await expect(page.getByRole('heading', { name: 'Job saved' })).toBeVisible(); expect(count).toBe(1);
});

test('honors throttle delay with controlled browser time', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-04T15:00:00Z') });
  let count = 0;
  await page.route('**/api/jobs', route => route.fulfill(++count === 1 ? { status: 429, headers: { 'Retry-After': '5' }, json: {} } : { status: 201, json: saved }));
  await fill(page); await page.getByRole('button', { name: 'Post job', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Retry same submission' })).toBeDisabled();
  await page.clock.fastForward(6000);
  await expect(page.getByRole('button', { name: 'Retry same submission' })).toBeEnabled();
  await page.getByRole('button', { name: 'Retry same submission' }).click();
  await expect(page.getByRole('heading', { name: 'Job saved' })).toBeVisible();
});

test('form fits the viewport and keyboard submission focuses the first invalid field', async ({ page }) => {
  await expect(page.locator('#title')).toHaveAttribute('aria-describedby', 'title-help title-errors');
  await page.getByRole('button', { name: 'Post job', exact: true }).focus(); await page.keyboard.press('Enter');
  await expect(page.locator('#title')).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('form.png'), fullPage: true });
});
