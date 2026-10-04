import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { QUERY_CLOCK } from '../../../../src/app/core/api/job-query-api';
import { ListQueryWorkflow } from '../../../../src/app/features/job-search/list-query-workflow';
import { defaultQuery, SearchQuery } from '../../../../src/app/features/job-search/search-query';
const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z' };
const page = { items: [job], nextCursor: 'next' };
describe('Owned list query pipeline', () => {
  let workflow: ListQueryWorkflow; let http: HttpTestingController; let queries: Subject<SearchQuery>; let now: number;
  beforeEach(() => {
    now = 1000;
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ListQueryWorkflow, { provide: QUERY_CLOCK, useValue: () => now }] });
    workflow = TestBed.inject(ListQueryWorkflow); http = TestBed.inject(HttpTestingController); queries = new Subject();
    workflow.connect(queries);
  });
  afterEach(() => http.verify());
  const request = () => http.expectOne(r => r.url === '/api/jobs');
  it('owns one connection, deduplicates URL emissions and cancels obsolete requests', () => {
    workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
    workflow.connect(queries); queries.next(defaultQuery); const old = request();
    queries.next({ ...defaultQuery }); http.expectNone(r => r.url === '/api/jobs');
    expect(workflow.busy()).toBe(true); workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
    queries.next({ ...defaultQuery, q: 'new' }); const latest = request();
    expect(old.cancelled).toBe(true); expect(() => old.flush(page)).toThrow();
    latest.flush(page); expect(workflow.state().kind).toBe('ready'); expect(workflow.data()?.items[0].id).toBe('job-1');
  });
  it('handles successful empty pages and fresh revisits without HTTP', () => {
    queries.next(defaultQuery); request().flush({ items: [], nextCursor: null }); expect(workflow.state().kind).toBe('empty');
    queries.next({ ...defaultQuery, q: 'new' }); request().flush(page);
    queries.next(defaultQuery); http.expectNone(r => r.url === '/api/jobs'); expect(workflow.state().kind).toBe('empty');
  });
  it('refreshes stale or explicit same-query data, labels failure and never shows another query data', () => {
    queries.next(defaultQuery); request().flush(page);
    workflow.refresh(); const refresh = request(); expect(workflow.state().kind).toBe('refreshing'); expect(workflow.data()).toBeDefined();
    refresh.flush({}, { status: 500, statusText: 'Failure' }); expect(workflow.state().kind).toBe('stale-error');
    workflow.refresh(); request().flush(page);
    queries.next({ ...defaultQuery, q: 'different' }); expect(workflow.data()).toBeUndefined(); request().flush({ items: [], nextCursor: null });
    now += 30000; queries.next(defaultQuery); expect(workflow.state().kind).toBe('refreshing'); request().flush(page); expect(workflow.state().kind).toBe('ready');
  });
  it.each([[202, 'not-ready'], [500, 'error'], [409, 'error']] as const)('handles initial %s without silently changing URL criteria', (status, kind) => {
    queries.next({ ...defaultQuery, cursor: 'deep' });
    request().flush({ code: 'cursor_expired' }, { status, statusText: 'Result' }); expect(workflow.state().kind).toBe(kind);
    workflow.refresh(); const retry = request(); expect(retry.request.params.get('cursor')).toBe('deep'); retry.flush(page);
  });
  it('respects a valid throttle deadline exactly, permits explicit retry with absent delay', () => {
    queries.next(defaultQuery); request().flush({}, { status: 429, statusText: 'Slow down', headers: { 'Retry-After': '2' } });
    expect(workflow.state().kind).toBe('throttled'); workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
    now = 2999; workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
    now = 3000; workflow.refresh(); request().flush({}, { status: 429, statusText: 'Slow down' });
    workflow.refresh(); request().flush(page); expect(workflow.state().kind).toBe('ready');
  });
  it('respects throttling during a stale failure and retries other failures', () => {
    queries.next(defaultQuery); request().flush(page); workflow.refresh();
    request().flush({}, { status: 429, statusText: 'Slow down', headers: { 'Retry-After': '1' } });
    expect(workflow.state().kind).toBe('stale-error'); workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
    now += 1000; workflow.refresh(); request().flush({}, { status: 202, statusText: 'Accepted' });
    expect(workflow.state().kind).toBe('stale-error'); workflow.refresh(); request().flush(page);
  });
  it('tears down active requests and ignores subsequent route/refresh commands', () => {
    queries.next(defaultQuery); const req = request(); TestBed.resetTestingModule(); expect(req.cancelled).toBe(true);
    queries.next({ ...defaultQuery, q: 'later' }); workflow.refresh(); http.expectNone(r => r.url === '/api/jobs');
  });
});
