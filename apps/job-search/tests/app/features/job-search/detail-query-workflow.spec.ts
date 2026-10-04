import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { QUERY_CLOCK } from '../../../../src/app/core/api/job-query-api';
import { DetailQueryWorkflow } from '../../../../src/app/features/job-search/detail-query-workflow';
const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z', description: 'Details' };
describe('Detail read workflow', () => {
  let workflow: DetailQueryWorkflow; let http: HttpTestingController; let ids: Subject<string>; let now: number;
  beforeEach(() => {
    now = 1000;
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), DetailQueryWorkflow, { provide: QUERY_CLOCK, useValue: () => now }] });
    workflow = TestBed.inject(DetailQueryWorkflow); http = TestBed.inject(HttpTestingController); ids = new Subject(); workflow.connect(ids);
  });
  afterEach(() => http.verify());
  const request = (id = 'job-1') => http.expectOne('/api/jobs/' + id);
  it('has one connection, cancels old identities and tears down requests', () => {
    workflow.refresh(); workflow.connect(ids); ids.next('job-1'); const old = request();
    workflow.refresh(); ids.next('job-1'); http.expectNone('/api/jobs/job-1');
    ids.next('job-2'); expect(old.cancelled).toBe(true); expect(workflow.data()).toBeUndefined();
    request('job-2').flush({ ...job, id: 'job-2' }); expect(workflow.state().kind).toBe('ready');
    ids.next('job-3'); const pending = request('job-3'); TestBed.resetTestingModule(); expect(pending.cancelled).toBe(true);
    ids.next('job-4'); workflow.refresh(); http.expectNone('/api/jobs/job-4');
  });
  it('uses fresh cache, revalidates exactly expired data and preserves stale data on errors', () => {
    ids.next('job-1'); request().flush(job); ids.next('job-2'); request('job-2').flush({ ...job, id: 'job-2' });
    ids.next('job-1'); http.expectNone('/api/jobs/job-1'); expect(workflow.state().kind).toBe('ready');
    workflow.refresh(); expect(workflow.state().kind).toBe('refreshing'); request().flush({}, { status: 500, statusText: 'Failure' });
    expect(workflow.state().kind).toBe('stale-error'); expect(workflow.data()?.id).toBe('job-1');
    workflow.refresh(); request().flush(job);
    ids.next('job-2'); now += 30000; ids.next('job-1'); expect(workflow.state().kind).toBe('refreshing'); request().flush(job);
  });
  it.each([404, 410])('clears cached detail on unavailable %s', status => {
    ids.next('job-1'); request().flush(job); workflow.refresh(); request().flush({}, { status, statusText: 'Unavailable' });
    expect(workflow.state().kind).toBe('unavailable'); expect(workflow.data()).toBeUndefined();
  });
  it.each([202, 204, 500])('treats unsupported/failed detail %s as error, not data', status => {
    ids.next('job-1'); request().flush(null, { status, statusText: 'Response' }); expect(workflow.state().kind).toBe('error');
    workflow.refresh(); request().flush(job); expect(workflow.state().kind).toBe('ready');
  });
  it('obeys valid and absent throttle deadlines, including stale errors', () => {
    ids.next('job-1'); request().flush({}, { status: 429, statusText: 'Slow', headers: { 'Retry-After': '1' } });
    workflow.refresh(); http.expectNone('/api/jobs/job-1'); now += 1000;
    workflow.refresh(); request().flush({}, { status: 429, statusText: 'Slow' }); workflow.refresh(); request().flush(job);
    workflow.refresh(); request().flush({}, { status: 429, statusText: 'Slow', headers: { 'Retry-After': '1' } });
    workflow.refresh(); http.expectNone('/api/jobs/job-1'); now += 1000; workflow.refresh(); request().flush(job);
  });
});
