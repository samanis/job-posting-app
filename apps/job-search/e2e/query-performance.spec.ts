import { expect, test } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import { json, summary } from './fixtures.js';

test('100000-job conceptual dataset renders only requested pages and records Chromium evidence', async ({ page, context }) => {
  const requests: URL[] = [], payloadBytes: number[] = [];
  await page.route('**/api/jobs**', async route => {
    const url = new URL(route.request().url()); requests.push(url);
    const start = url.searchParams.has('cursor') ? 20 : 0;
    const limit = Number(url.searchParams.get('limit'));
    const items = Array.from({ length: limit }, (_, i) => summary(`job-${start + i}`));
    expect(items.every(item => !('description' in item))).toBe(true);
    const body = { items, nextCursor: start === 0 ? 'page-2' : null };
    payloadBytes.push(Buffer.byteLength(JSON.stringify(body))); await json(route, body);
  });
  const session = await context.newCDPSession(page);
  const events: { name?: string; dur?: number; ph?: string; [key: string]: unknown }[] = [];
  session.on('Tracing.dataCollected', data => events.push(...data.value));
  await session.send('Performance.enable');
  await session.send('Tracing.start', { categories: 'devtools.timeline,blink.user_timing', transferMode: 'ReportEvents' });
  try {
    await page.goto('/jobs'); await expect(page.locator('.job-card')).toHaveCount(20);
    expect(requests).toHaveLength(1); expect(requests[0].pathname).toBe('/api/jobs');
    await page.getByRole('button', { name: 'Next', exact: true }).click(); await expect(page.locator('[data-job-id="job-20"]')).toBeVisible();
    await expect(page.locator('.job-card')).toHaveCount(20); expect(requests).toHaveLength(2);
    expect(requests.every(url => url.pathname === '/api/jobs')).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const metrics = await session.send('Performance.getMetrics');
    const finished = new Promise<void>(resolve => session.once('Tracing.tracingComplete', () => resolve()));
    await session.send('Tracing.end'); await finished;
    const evidence = { conceptualDatasetSize: 100000, renderedCards: 20, listReads: requests.length, detailReads: 0, payloadBytes,
      metrics: metrics.metrics.filter(metric => ['LayoutCount', 'RecalcStyleCount', 'ScriptDuration', 'LayoutDuration', 'TaskDuration', 'JSHeapUsedSize'].includes(metric.name)),
      traceEventCount: events.length,
      timeline: ['Layout', 'UpdateLayoutTree', 'FunctionCall', 'EvaluateScript'].map(name => ({ name, count: events.filter(event => event.name === name).length, durationMicroseconds: events.filter(event => event.name === name && event.ph === 'X').reduce((sum, event) => sum + (event.dur ?? 0), 0) })),
      limits: 'Local development server plus mocked API; not API latency, real dataset storage, server throughput or a universal timing budget.' };
    const tracePath = test.info().outputPath('chromium-performance-trace.json');
    const evidencePath = test.info().outputPath('query-performance.json');
    await writeFile(tracePath, JSON.stringify({ traceEvents: events })); await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
    await test.info().attach('performance-evidence', { path: evidencePath, contentType: 'application/json' });
    await test.info().attach('chromium-timeline', { path: tracePath, contentType: 'application/json' });
  } finally { await session.detach(); }
});
