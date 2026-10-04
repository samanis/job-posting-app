import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { JobQueryApi, QUERY_CLOCK, QUERY_TIMEOUT_MS } from '../../../../src/app/core/api/job-query-api';
import { JobQuery, QueryOutcome, JobPage, JobDetail } from '../../../../src/app/core/api/query-contract';
const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10.25, salaryMax: 20.5, closingDate: '2020-02-29', createdAt: '2026-10-04T15:00:00Z', description: 'Plain text' };
describe('Job query transport', () => {
  let api: JobQueryApi;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(JobQueryApi); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); vi.useRealTimers(); });
  it('has usable default clock and timeout providers', () => {
    expect(TestBed.inject(QUERY_CLOCK)()).toBeGreaterThan(0);
    expect(TestBed.inject(QUERY_TIMEOUT_MS)).toBe(10000);
  });
  it('sends one cold GET with defaults, accepting an empty page', () => {
    const source = api.list(); http.expectNone('/api/jobs');
    let result: QueryOutcome<JobPage> | undefined;
    source.subscribe(v => result = v);
    const request = http.expectOne('/api/jobs?limit=20&sort=newest');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], nextCursor: null });
    expect(result).toEqual({ kind: 'ready', data: { items: [], nextCursor: null } });
  });
  it('encodes all optional parameters, trims text and snapshots bounded summaries', () => {
    let result: QueryOutcome<JobPage> | undefined;
    api.list({ q: ' C++ & a/b? ', department: ' Platform ', location: ' Montréal ', cursor: 'opaque+/=?', limit: 1, sort: 'closing-soon' }).subscribe(v => result = v);
    const req = http.expectOne(r => r.url === '/api/jobs');
    expect(req.request.params.keys()).toEqual(['limit', 'sort', 'q', 'department', 'location', 'cursor']);
    expect(req.request.params.get('q')).toBe('C++ & a/b?');
    expect(req.request.urlWithParams).toContain('%2B%2B');
    expect(req.request.params.get('department')).toBe('Platform');
    expect(req.request.params.get('location')).toBe('Montréal');
    expect(req.request.params.get('cursor')).toBe('opaque+/=?');
    req.flush({ items: [job], nextCursor: 'next' });
    if (result?.kind !== 'ready') throw new Error('Expected page');
    expect(result.data.items[0]).not.toHaveProperty('description');
    expect(Object.isFrozen(result.data.items)).toBe(true);
    expect(Object.isFrozen(result.data.items[0])).toBe(true);
  });
  it('omits empty text without changing default query', () => {
    api.list({ q: ' ', department: '', location: '\t' }).subscribe();
    http.expectOne('/api/jobs?limit=20&sort=newest').flush({ items: [], nextCursor: null });
  });
  it.each([{ limit: 0 }, { limit: 51 }, { limit: 1.2 }, { limit: NaN }, { sort: 'bad' }, { q: 'a'.repeat(201) }, { department: 'a'.repeat(101) }, { location: 1 }, { cursor: '' }, { cursor: 'a b' }] as unknown as JobQuery[])('rejects invalid parameters without a request %s', query => {
    let kind: string | undefined; api.list(query).subscribe(v => kind = v.kind);
    expect(kind).toBe('invalid-query'); http.expectNone(r => r.url === '/api/jobs');
  });
  it('encodes detail identifiers as one path segment', () => {
    let result: QueryOutcome<JobDetail> | undefined;
    api.detail('a/b?c#d &😀').subscribe(v => result = v);
    const req = http.expectOne('/api/jobs/a%2Fb%3Fc%23d%20%26%F0%9F%98%80');
    const record = { ...job, id: 'a/b?c#d &😀' };
    expect(req.request.method).toBe('GET'); req.flush(record);
    expect(result).toEqual({ kind: 'ready', data: record });
    if (result?.kind === 'ready') expect(Object.isFrozen(result.data)).toBe(true);
  });
  it.each(['', ' ', '.', '..', '\u0000', '\ud800', 'a'.repeat(1001)])('rejects invalid detail id %s', id => {
    let kind: string | undefined; api.detail(id).subscribe(v => kind = v.kind);
    expect(kind).toBe('invalid-query'); http.expectNone(r => r.url.startsWith('/api/jobs'));
  });
  it('propagates unsupported success and malformed response outcomes', () => {
    for (const [status, body, kind] of [[202, {}, 'not-ready'], [204, null, 'protocol-error'], [200, {}, 'protocol-error']] as const) {
      let outcome: string | undefined; api.list().subscribe(v => outcome = v.kind);
      http.expectOne(r => r.url === '/api/jobs').flush(body, { status, statusText: 'OK' });
      expect(outcome).toBe(kind);
    }
  });
  it('reports unavailable detail and list throttle with injected clock', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: QUERY_CLOCK, useValue: () => 1000 }] });
    api = TestBed.inject(JobQueryApi); http = TestBed.inject(HttpTestingController);
    let result: QueryOutcome<JobDetail> | undefined;
    api.detail('job-1').subscribe(v => result = v);
    http.expectOne('/api/jobs/job-1').flush({}, { status: 404, statusText: 'Not found' });
    expect(result?.kind).toBe('unavailable');
    let page: QueryOutcome<JobPage> | undefined;
    api.list().subscribe(v => page = v);
    http.expectOne(r => r.url === '/api/jobs').flush({}, { status: 429, statusText: 'Throttled', headers: { 'Retry-After': '2' } });
    expect(page).toMatchObject({ kind: 'throttled', retryAt: 3000 });
  });
  it('cancels without emitting an error or another request', () => {
    const next = vi.fn(); const error = vi.fn();
    const sub = api.list().subscribe({ next, error }); const req = http.expectOne(r => r.url === '/api/jobs');
    sub.unsubscribe(); expect(req.cancelled).toBe(true); expect(next).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled();
  });
  it.each([0, 100000, NaN])('bounds configured timeout %s and never retries', async duration => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: QUERY_TIMEOUT_MS, useValue: duration }] });
    api = TestBed.inject(JobQueryApi); http = TestBed.inject(HttpTestingController); vi.useFakeTimers();
    let kind: string | undefined; api.list().subscribe(v => kind = v.kind);
    const req = http.expectOne(r => r.url === '/api/jobs');
    await vi.advanceTimersByTimeAsync(60000);
    expect(kind).toBe('timeout'); expect(req.cancelled).toBe(true); http.expectNone(r => r.url === '/api/jobs');
  });
  it('maps network failure without leaking details', () => {
    let kind: string | undefined; api.detail('job-1').subscribe(v => kind = v.kind);
    http.expectOne('/api/jobs/job-1').error(new ProgressEvent('error'));
    expect(kind).toBe('network-error');
  });
  it('rejects malformed or mismatched detail records', () => {
    for (const body of [{}, job]) {
      let kind: string | undefined; api.detail('different-id').subscribe(v => kind = v.kind);
      http.expectOne('/api/jobs/different-id').flush(body);
      expect(kind).toBe('protocol-error');
    }
  });
  it.each([[400, 'invalid-query'], [422, 'invalid-query'], [409, 'cursor-expired'], [404, 'client-error'], [500, 'server-error']] as const)('maps HTTP %s on listing reads', (status, expected) => {
    let kind: string | undefined; api.list().subscribe(v => kind = v.kind);
    http.expectOne(r => r.url === '/api/jobs').flush({ code: 'cursor_expired' }, { status, statusText: 'Failure' });
    expect(kind).toBe(expected);
    http.expectNone(r => r.url === '/api/jobs');
  });
});
