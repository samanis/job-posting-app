import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { API_CLOCK, JobPostingApi, POSTING_TIMEOUT_MS } from '../../../../src/app/core/api/job-posting-api';
import { CreateJobRequest } from '../../../../src/app/core/api/job-posting-contract';

const payload: CreateJobRequest = Object.freeze({ title: 'Engineer', department: 'Engineering', location: 'Toronto', description: 'Build software', salaryMin: 10, salaryMax: 20, closingDate: '2027-02-28' });
const saved = { ...payload, id: 'job-1', createdAt: '2026-10-04T15:00:00Z' };

describe('JobPostingApi', () => {
  let http: HttpTestingController;
  let api: JobPostingApi;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); vi.useRealTimers(); });
  function start() {
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(JobPostingApi);
    const result = firstValueFrom(api.post(payload, 'attempt-123'));
    const request = http.expectOne({ method: 'POST', url: '/api/jobs' });
    expect(request.request.body).toEqual(payload);
    expect(request.request.headers.get('Idempotency-Key')).toBe('attempt-123');
    return { result, request };
  }
  it.each([200, 201, 203])('returns the actual saved record on %s', async status => {
    const { result, request } = start();
    request.flush({ ...saved, title: 'Server title' }, { status, statusText: 'Success' });
    expect(await result).toEqual({ kind: 'saved', status, record: { ...saved, title: 'Server title' } });
    expect(payload.title).toBe('Engineer');
  });
  it.each([202, 204])('does not confirm completion on %s', async status => {
    const { result, request } = start(); request.flush(null, { status, statusText: 'Success' });
    expect((await result).kind).toBe(status === 202 ? 'pending' : 'unknown');
  });
  it('handles malformed success', async () => {
    const { result, request } = start(); request.flush({});
    expect(await result).toMatchObject({ kind: 'unknown', reason: 'invalid-success' });
  });
  it.each([400, 422, 409, 429, 404, 500, 503])('handles %s without an automatic second request', async status => {
    TestBed.overrideProvider(API_CLOCK, { useValue: () => 10000 });
    const { result, request } = start();
    request.flush({ errors: { Title: ['Required'] } }, { status, statusText: 'Failure', headers: { 'Retry-After': '2' } });
    const outcome = await result;
    expect(outcome.kind).toBe(({ 400: 'validation', 422: 'validation', 409: 'conflict', 429: 'throttled', 404: 'rejected', 500: 'unknown', 503: 'unknown' } as Record<number, string>)[status]);
    if (outcome.kind === 'throttled') expect(outcome.retryAt).toBe(12000);
    http.expectNone('/api/jobs');
  });
  it('handles network errors using the default clock', async () => {
    vi.spyOn(Date, 'now').mockReturnValue(10000);
    const { result, request } = start(); request.error(new ProgressEvent('error'));
    expect(await result).toMatchObject({ kind: 'unknown', reason: 'network' });
    vi.restoreAllMocks();
  });
  it.each([[NaN, 15000], [-1, 1000], [100000, 60000], [1200, 1200]])('bounds timeout %s to %s', async (configured, duration) => {
    vi.useFakeTimers();
    TestBed.overrideProvider(POSTING_TIMEOUT_MS, { useValue: configured });
    const { result, request } = start();
    await vi.advanceTimersByTimeAsync(duration - 1);
    expect(request.cancelled).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(await result).toMatchObject({ kind: 'unknown', reason: 'timeout' });
    expect(request.cancelled).toBe(true);
    http.expectNone('/api/jobs');
  });
});
