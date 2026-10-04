import { Page, Route } from '@playwright/test';

// Intercepted test fixtures only. No real backend or cross-app propagation is exercised.
export const job = { id: 'job-1', title: 'Platform Engineer', department: 'Engineering', location: 'Toronto', salaryMin: 1000.25, salaryMax: 2000.5, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z', description: '<b>Plain text</b>\nSecond line' };
export function summary(id = job.id) { const { description: _, ...data } = job; return { ...data, id }; }
export async function json(route: Route, body: unknown, status = 200, headers: Record<string, string> = {}) {
  await route.fulfill({ status, contentType: 'application/json', headers, body: status === 204 ? '' : JSON.stringify(body) });
}
export async function mockJobs(page: Page) {
  const requests: URL[] = [];
  await page.route('**/api/jobs**', async route => {
    const url = new URL(route.request().url()); requests.push(url);
    if (url.pathname !== '/api/jobs') await json(route, job);
    else await json(route, { items: [summary()], nextCursor: null });
  });
  return requests;
}
