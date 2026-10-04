import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QUERY_CLOCK } from '../../../../src/app/core/api/job-query-api';
import { QUERY_CACHE_ENTRIES, QUERY_CACHE_SCOPE, QUERY_CACHE_TTL_MS, QueryReads } from '../../../../src/app/features/job-search/query-reads';
import { defaultQuery } from '../../../../src/app/features/job-search/search-query';
const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z', description: 'Details' };
const page = { items: [job], nextCursor: null };
describe('Shared read cache and ownership', () => {
  let reads: QueryReads; let http: HttpTestingController; let now: number;
  beforeEach(() => {
    now = 1000;
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: QUERY_CLOCK, useValue: () => now }] });
    reads = TestBed.inject(QueryReads); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  const listRequest = () => http.expectOne(r => r.url === '/api/jobs');
  it('provides configurable bounded defaults and a scoped cache identity', () => {
    expect(TestBed.inject(QUERY_CACHE_TTL_MS)).toBe(30000);
    expect(TestBed.inject(QUERY_CACHE_ENTRIES)).toBe(50);
    expect(TestBed.inject(QUERY_CACHE_SCOPE)).toBe('/api/jobs');
  });
  it('deduplicates simultaneous reads, replays the pending result and releases on completion', () => {
    const one = vi.fn(), two = vi.fn();
    const source = reads.list(); source.subscribe(one); source.subscribe(two);
    const req = listRequest(); http.expectNone(r => r.url === '/api/jobs'); req.flush(page);
    expect(one).toHaveBeenCalledTimes(1); expect(two).toHaveBeenCalledTimes(1);
    source.subscribe(two); http.expectNone(r => r.url === '/api/jobs'); expect(two).toHaveBeenCalledTimes(2);
    expect(reads.cachedList(defaultQuery)?.fresh).toBe(true);
  });
  it('keeps a request while any consumer remains, then aborts and releases the final cancellation', () => {
    const one = reads.list().subscribe(); const two = reads.list().subscribe(); const req = listRequest();
    one.unsubscribe(); expect(req.cancelled).toBe(false);
    two.unsubscribe(); expect(req.cancelled).toBe(true); expect(reads.cachedList(defaultQuery)).toBeUndefined();
    reads.list().subscribe(); listRequest().flush(page);
  });
  it('expires exactly at TTL, leaves stale snapshots available and never polls', () => {
    reads.list().subscribe(); listRequest().flush(page);
    now += 29999; reads.list().subscribe(); http.expectNone(r => r.url === '/api/jobs');
    expect(reads.cachedList(defaultQuery)?.fresh).toBe(true);
    now++; expect(reads.cachedList(defaultQuery)?.fresh).toBe(false);
    http.expectNone(r => r.url === '/api/jobs');
    reads.list().subscribe(); listRequest().flush({ items: [], nextCursor: null });
    expect(reads.cachedList(defaultQuery)?.data.items).toEqual([]);
  });
  it('refresh bypasses fresh cache and joins an existing request', () => {
    reads.list().subscribe(); listRequest().flush(page);
    const one = vi.fn(), two = vi.fn(); reads.list(defaultQuery, true).subscribe(one);
    const req = listRequest(); reads.list(defaultQuery, true).subscribe(two); reads.list().subscribe(two);
    http.expectNone(r => r.url === '/api/jobs'); req.flush(page);
    expect(one).toHaveBeenCalledTimes(1); expect(two).toHaveBeenCalledTimes(2);
  });
  it('normalizes text keys and isolates all effective query parameters', () => {
    reads.list({ ...defaultQuery, q: ' Test ' }).subscribe(); listRequest().flush(page);
    reads.list({ ...defaultQuery, q: 'Test' }).subscribe(); http.expectNone(r => r.url === '/api/jobs');
    for (const query of [{ ...defaultQuery, q: 'test' }, { ...defaultQuery, department: 'Test' }, { ...defaultQuery, location: 'Test' }, { ...defaultQuery, sort: 'closing-soon' as const }, { ...defaultQuery, limit: 1 }, { ...defaultQuery, cursor: 'opaque' }]) {
      reads.list(query).subscribe(); listRequest().flush(page);
    }
  });
  it('never caches errors or oversized page data and releases failed flights', () => {
    reads.list().subscribe(); listRequest().flush({}, { status: 500, statusText: 'Failure' });
    expect(reads.cachedList(defaultQuery)).toBeUndefined(); reads.list().subscribe(); listRequest().flush(page);
    const query = { ...defaultQuery, limit: 1 };
    reads.list(query).subscribe(); listRequest().flush({ items: [job, { ...job, id: 'job-2' }], nextCursor: null });
    expect(reads.cachedList(query)).toBeUndefined();
  });
  it('shares list/detail capacity, evicts LRU and invalidates unavailable jobs and containing pages', () => {
    TestBed.resetTestingModule(); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: QUERY_CACHE_ENTRIES, useValue: 3 }] });
    reads = TestBed.inject(QueryReads); http = TestBed.inject(HttpTestingController);
    reads.detail('job-1').subscribe(); http.expectOne('/api/jobs/job-1').flush(job);
    reads.list().subscribe(); listRequest().flush(page);
    const other = { ...defaultQuery, q: 'other' }; reads.list(other).subscribe(); listRequest().flush({ items: [], nextCursor: null });
    expect(reads.cachedDetail('job-1')?.fresh).toBe(true);
    reads.list({ ...defaultQuery, q: 'extra' }).subscribe(); listRequest().flush({ items: [], nextCursor: null });
    expect(reads.cachedList(defaultQuery)).toBeUndefined();
    reads.list().subscribe(); listRequest().flush(page);
    reads.detail('job-1', true).subscribe(); http.expectOne('/api/jobs/job-1').flush({}, { status: 410, statusText: 'Gone' });
    expect(reads.cachedDetail('job-1')).toBeUndefined(); expect(reads.cachedList(defaultQuery)).toBeUndefined();
    expect(reads.cachedList({ ...defaultQuery, q: 'extra' })).toBeDefined();
  });
  it('keeps list cache on ordinary detail failures and caches successful detail', () => {
    reads.list().subscribe(); listRequest().flush(page);
    reads.detail('job-1').subscribe(); http.expectOne('/api/jobs/job-1').flush({}, { status: 500, statusText: 'Failure' });
    expect(reads.cachedList(defaultQuery)).toBeDefined(); expect(reads.cachedDetail('job-1')).toBeUndefined();
    reads.detail('job-1').subscribe(); http.expectOne('/api/jobs/job-1').flush(job);
    reads.detail('job-1').subscribe(); http.expectNone('/api/jobs/job-1');
  });
  it.each([NaN, -1, 999999])('bounds cache configuration %s', value => {
    TestBed.resetTestingModule(); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: QUERY_CACHE_ENTRIES, useValue: value }, { provide: QUERY_CACHE_TTL_MS, useValue: value }, { provide: QUERY_CLOCK, useValue: () => now }] });
    reads = TestBed.inject(QueryReads); http = TestBed.inject(HttpTestingController);
    reads.list().subscribe(); listRequest().flush(page);
    expect(reads.cachedList(defaultQuery)?.fresh).toBe(value !== -1);
  });
  it('releases safely when a consumer starts another request during delivery', () => {
    let second = false;
    reads.list().subscribe(() => reads.list(defaultQuery, true).subscribe(() => second = true));
    listRequest().flush(page); listRequest().flush(page); expect(second).toBe(true);
  });
  it('does not extend original freshness after failed refresh or cache partial data', () => {
    reads.list().subscribe(); listRequest().flush(page);
    now += 29999;
    reads.list(defaultQuery, true).subscribe(); listRequest().flush({}, { status: 500, statusText: 'Failure' });
    expect(reads.cachedList(defaultQuery)?.fresh).toBe(true);
    now++; expect(reads.cachedList(defaultQuery)?.fresh).toBe(false);
    reads.list(defaultQuery, true).subscribe(); listRequest().flush({ items: [job], nextCursor: '' });
    expect(reads.cachedList(defaultQuery)?.fresh).toBe(false);
    expect(reads.cachedList(defaultQuery)?.data).toMatchObject({ nextCursor: null });
  });
  it('uses one combined 50-entry default budget for summaries and details', () => {
    reads.detail('job-1').subscribe(); http.expectOne('/api/jobs/job-1').flush(job);
    for (let i = 0; i < 49; i++) {
      reads.list({ ...defaultQuery, q: String(i) }).subscribe(); listRequest().flush(page);
    }
    reads.list({ ...defaultQuery, q: 'last' }).subscribe(); listRequest().flush(page);
    expect(reads.cachedDetail('job-1')).toBeUndefined();
    expect(reads.cachedList({ ...defaultQuery, q: '0' })).toBeDefined();
  });
});
