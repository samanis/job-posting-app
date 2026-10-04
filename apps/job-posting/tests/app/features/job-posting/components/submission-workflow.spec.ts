import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { API_CLOCK, JobPostingApi, POSTING_TIMEOUT_MS } from '../../../../../src/app/core/api/job-posting-api';
import { NewJobPage } from '../../../../../src/app/features/job-posting/components/new-job-page';
import { ATTEMPT_STORAGE, ATTEMPT_UUID } from '../../../../../src/app/features/job-posting/state/posting-attempt-store';
import { LOCAL_CLOCK } from '../../../../../src/app/features/job-posting/validators/job-validation';

const payload = { title: 'Engineer', department: 'Tech', location: 'Toronto', description: 'Build', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01' };
const saved = { ...payload, title: 'Server title', id: 'id-1', createdAt: '2026-10-04T15:00:00Z' };

describe('Submission workflow integration', () => {
  let fixture: ComponentFixture<NewJobPage>;
  let page: NewJobPage;
  let root: HTMLElement;
  let http: HttpTestingController;
  let raw: string | null;
  let now: number;
  let storage: { read: ReturnType<typeof vi.fn>; write: ReturnType<typeof vi.fn>; remove: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    raw = null; now = 1000; let sequence = 0;
    storage = { read: vi.fn(() => raw), write: vi.fn((value: string) => { raw = value; }), remove: vi.fn(() => { raw = null; }) };
    TestBed.configureTestingModule({ imports: [NewJobPage], providers: [provideHttpClientTesting(),
      { provide: ATTEMPT_STORAGE, useValue: storage }, { provide: ATTEMPT_UUID, useValue: () => `key-${++sequence}` },
      { provide: API_CLOCK, useValue: () => now }, { provide: LOCAL_CLOCK, useValue: () => new Date(2026, 9, 4) },
      { provide: POSTING_TIMEOUT_MS, useValue: 1000 },
    ] });
  });
  afterEach(() => { fixture.destroy(); http.verify(); vi.restoreAllMocks(); vi.useRealTimers(); });
  async function create() {
    fixture = TestBed.createComponent(NewJobPage); page = fixture.componentInstance; root = fixture.nativeElement;
    http = TestBed.inject(HttpTestingController); await fixture.whenStable();
  }
  async function submit() {
    const event = new Event('submit', { cancelable: true, bubbles: true });
    const form = root.querySelector('form');
    if (form === null) page.onSubmit(event); else form.dispatchEvent(event);
    await fixture.whenStable();
  }
  async function ready() {
    await create(); page.draft.set({ ...payload, salaryMin: '10', salaryMax: '20' }); await fixture.whenStable();
    await submit(); return http.expectOne('/api/jobs');
  }
  function recovery(status: string, patch: Record<string, unknown> = {}) {
    raw = JSON.stringify({ version: 1, key: 'recovered-key', payload, status, retryAt: null, savedRecord: null, ...patch });
  }
  it('shows the API record, focuses confirmation and starts a clean logical attempt explicitly', async () => {
    const req = await ready(); req.flush(saved); await fixture.whenStable();
    expect(root.querySelector('form')).toBeNull();
    expect(root.querySelector('app-saved-job-confirmation')?.textContent).toContain('Server title');
    expect(document.activeElement?.id).toBe('confirmation-heading');
    root.querySelector<HTMLButtonElement>('app-saved-job-confirmation button')!.click(); await fixture.whenStable();
    expect(page.workflow.state()).toBe('editing'); expect(page.draft().title).toBe('');
    expect(page.attempted()).toBe(false); expect(document.activeElement?.id).toBe('title');
    page.draft.set({ ...payload, salaryMin: '10', salaryMax: '20' }); await fixture.whenStable(); await submit();
    const next = http.expectOne('/api/jobs'); expect(next.request.headers.get('Idempotency-Key')).toBe('key-2'); next.flush(saved);
  });
  it('does not reset before success or when saved cleanup fails', async () => {
    await create(); page.postAnother(); expect(page.workflow.state()).toBe('editing');
    expect(page.workflow.submit(payload)).toBe(true); http.expectOne('/api/jobs').flush(saved); await fixture.whenStable();
    storage.remove.mockImplementation(() => { throw new Error('Denied'); });
    root.querySelector<HTMLButtonElement>('app-saved-job-confirmation button')!.click(); await fixture.whenStable();
    expect(page.workflow.state()).toBe('saved'); expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('could not be cleared');
  });
  it('does not dispatch invalid form values' , async () => {
    await create(); expect(page.workflow.savedRecord()).toBeNull(); await submit(); http.expectNone('/api/jobs'); expect(page.workflow.state()).toBe('editing');
  });
  it('sends one persisted POST and locks controls during a delayed response', async () => {
    const req = await ready();
    expect(storage.write).toHaveBeenCalledOnce(); expect(req.request.body).toEqual(payload);
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(page.workflow.state()).toBe('submitting'); expect(root.querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    expect(root.querySelector<HTMLInputElement>('#title')?.readOnly).toBe(true);
    expect(root.querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(true);
    await submit(); http.expectNone('/api/jobs');
    req.flush(saved, { status: 201, statusText: 'Created' }); await fixture.whenStable();
    expect(page.workflow.state()).toBe('saved'); expect(page.workflow.savedRecord()).toEqual(saved);
    expect(root.textContent).toContain('Your job was saved');
    expect(page.workflow.postAnother()).toBe(true); expect(page.workflow.state()).toBe('editing');
    expect(page.workflow.postAnother()).toBe(false);
  });
  it.each([400, 422])('shows server field/form errors for %s and correction uses a new key', async status => {
    const req = await ready(); req.flush({ errors: { Title: ['Server title error'], Unknown: ['General error'] } }, { status, statusText: 'Invalid' });
    await fixture.whenStable();
    expect(page.workflow.state()).toBe('rejected'); expect(root.querySelector('#title-errors')?.textContent).toContain('Server title error');
    expect(root.textContent).toContain('General error'); expect(page.draft().title).toBe('Engineer');
    expect(root.querySelector<HTMLInputElement>('#title')?.readOnly).toBe(false);
    page.draft.update(draft => ({ ...draft, title: 'Corrected' })); await fixture.whenStable(); await submit();
    const corrected = http.expectOne('/api/jobs'); expect(corrected.request.headers.get('Idempotency-Key')).toBe('key-2');
    expect(corrected.request.body.title).toBe('Corrected'); corrected.flush(saved);
  });
  it.each([404, 413])('shows generic rejection %s without losing the draft', async status => {
    const req = await ready(); req.flush('private diagnostics', { status, statusText: 'Rejected' }); await fixture.whenStable();
    expect(page.workflow.state()).toBe('rejected'); expect(root.textContent).toContain('could not be accepted');
    expect(root.textContent).not.toContain('private diagnostics'); expect(page.draft().description).toBe('Build');
  });
  it.each([202, 204, 500, 503])('retains and explicitly retries the same request after %s', async status => {
    const req = await ready(); const key = req.request.headers.get('Idempotency-Key');
    req.flush(null, { status, statusText: 'Response' }); await fixture.whenStable();
    expect(page.workflow.state()).toBe(status === 202 ? 'pending' : 'unknown'); expect(page.workflow.canRetry()).toBe(true);
    expect(root.querySelector<HTMLInputElement>('#title')?.readOnly).toBe(true);
    // Even a programmatic draft modification must not change the retry body.
    page.draft.update(draft => ({ ...draft, title: 'Changed' })); await fixture.whenStable(); await submit(); http.expectNone('/api/jobs');
    root.querySelector<HTMLButtonElement>('#retry-submission')!.click(); await fixture.whenStable();
    const retry = http.expectOne('/api/jobs'); expect(retry.request.body).toEqual(payload); expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(saved);
  });
  it('handles malformed success and a network failure without generating new identities', async () => {
    const req = await ready(); req.flush({}); await fixture.whenStable(); expect(page.workflow.state()).toBe('unknown');
    expect(page.workflow.retry()).toBe(true); const retry = http.expectOne('/api/jobs'); retry.error(new ProgressEvent('error'));
    await fixture.whenStable(); expect(page.workflow.state()).toBe('unknown'); expect(page.workflow.store.attempt()?.key).toBe('key-1');
    expect(root.textContent).toContain('could not be confirmed');
  });
  it.each(['idempotency_in_progress', 'idempotency_key_conflict', 'other'])('distinguishes conflict code %s', async code => {
    const req = await ready(); req.flush({ code }, { status: 409, statusText: 'Conflict' }); await fixture.whenStable();
    if (code === 'idempotency_in_progress') {
      expect(page.workflow.state()).toBe('pending'); expect(page.workflow.canRetry()).toBe(true);
      expect(root.textContent).toContain('still being processed');
    } else {
      expect(page.workflow.state()).toBe('conflict'); expect(page.workflow.canRetry()).toBe(false);
      expect(root.textContent).toContain(code === 'idempotency_key_conflict' ? 'different job details' : 'conflicts');
      page.resolveRecovery(); expect(page.workflow.state()).toBe('conflict');
      page.recoveryConfirmed.set(true); await fixture.whenStable();
      root.querySelector<HTMLButtonElement>('#resolve-recovery')!.click(); await fixture.whenStable();
      expect(page.workflow.state()).toBe('editing'); expect(page.recoveryConfirmed()).toBe(false); expect(page.draft().title).toBe('');
    }
  });
  it('counts down throttle delay, refuses early retry, and stops its timer at the deadline', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }); const req = await ready(); req.flush(null, { status: 429, statusText: 'Throttled', headers: { 'Retry-After': '2' } });
    await fixture.whenStable(); expect(page.workflow.remainingSeconds()).toBe(2); expect(page.workflow.retry()).toBe(false);
    expect(root.querySelector<HTMLButtonElement>('#retry-submission')?.disabled).toBe(true);
    now = 2000; await vi.advanceTimersByTimeAsync(250); await fixture.whenStable(); expect(page.workflow.remainingSeconds()).toBe(1);
    now = 3000; await vi.advanceTimersByTimeAsync(250); await fixture.whenStable(); expect(page.workflow.canRetry()).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
    expect(page.workflow.retry()).toBe(true); http.expectOne('/api/jobs').flush(saved);
  });
  it('provides a manual retry fallback for invalid Retry-After', async () => {
    const req = await ready(); req.flush(null, { status: 429, statusText: 'Throttled', headers: { 'Retry-After': 'invalid' } });
    await fixture.whenStable(); expect(page.workflow.canRetry()).toBe(true); expect(page.workflow.remainingSeconds()).toBe(0);
  });
  it('handles request timeout as unknown', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }); const req = await ready(); await vi.advanceTimersByTimeAsync(1000); await fixture.whenStable();
    expect(req.cancelled).toBe(true); expect(page.workflow.state()).toBe('unknown'); expect(page.workflow.canRetry()).toBe(true);
  });
  it('marks interrupted requests unknown and cancels transport on page destruction', async () => {
    const req = await ready(); fixture.destroy(); expect(req.cancelled).toBe(true);
    expect(page.workflow.store.attempt()?.status).toBe('unknown'); expect(JSON.parse(raw!).status).toBe('unknown');
  });
  it('cleans up a throttle countdown on destruction', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }); const intervals = vi.spyOn(globalThis, 'setInterval'); const clears = vi.spyOn(globalThis, 'clearInterval'); const req = await ready(); req.flush(null, { status: 429, statusText: 'Wait', headers: { 'Retry-After': '5' } });
    await fixture.whenStable();
    const index = intervals.mock.calls.findIndex(call => call[1] === 250);
    const countdownHandle = intervals.mock.results[index].value;
    fixture.destroy();
    expect(clears).toHaveBeenCalledWith(countdownHandle);
    const remaining = page.workflow.remainingSeconds(); now = 10000;
    await vi.advanceTimersByTimeAsync(500);
    expect(page.workflow.remainingSeconds()).toBe(remaining);
  });
  it.each(['in-flight', 'pending', 'conflict', 'rejected', 'saved'])('recovers %s without startup POST', async status => {
    recovery(status, { savedRecord: status === 'saved' ? saved : null }); await create();
    http.expectNone('/api/jobs'); expect(page.draft().salaryMin).toBe('10');
    expect(page.workflow.state()).toBe(status === 'in-flight' ? 'unknown' : status);
    expect(page.workflow.message().length).toBeGreaterThan(0);
    if (status === 'saved') expect(page.workflow.savedRecord()).toEqual(saved);
  });
  it('starts a countdown for recovered throttling', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }); recovery('throttled', { retryAt: 2000 }); await create();
    expect(page.workflow.remainingSeconds()).toBe(1); http.expectNone('/api/jobs');
  });
  it('blocks corrupt recovery until explicit reconciliation and preserves failed resets', async () => {
    raw = 'corrupt'; await create(); expect(page.workflow.state()).toBe('recovery-blocked');
    await submit(); http.expectNone('/api/jobs'); expect(page.workflow.retry()).toBe(false);
    expect(root.textContent).toContain('cannot be recovered');
    page.resolveRecovery(); expect(raw).toBe('corrupt');
    storage.remove.mockImplementationOnce(() => { throw new Error('Denied'); });
    page.recoveryConfirmed.set(true); page.resolveRecovery(); expect(page.workflow.state()).toBe('recovery-blocked');
    page.resolveRecovery(); await fixture.whenStable(); expect(page.workflow.state()).toBe('editing');
  });
  it('blocks initial dispatch on persistence failure', async () => {
    storage.write.mockImplementation(() => { throw new Error('Quota'); });
    await create(); page.draft.set({ ...payload, salaryMin: '10', salaryMax: '20' }); await fixture.whenStable(); await submit();
    http.expectNone('/api/jobs'); expect(page.workflow.state()).toBe('recovery-blocked');
  });
  it('retains saved outcome through persistence and cleanup failures', async () => {
    const req = await ready(); storage.write.mockImplementation(() => { throw new Error('Quota'); }); req.flush(saved); await fixture.whenStable();
    expect(page.workflow.state()).toBe('saved'); expect(page.workflow.savedRecord()).toEqual(saved);
    storage.remove.mockImplementation(() => { throw new Error('Denied'); }); expect(page.workflow.postAnother()).toBe(false);
    expect(page.workflow.state()).toBe('saved'); expect(page.workflow.retry()).toBe(false); await submit(); http.expectNone('/api/jobs');
  });
  it('handles retry persistence failure without dispatching', async () => {
    recovery('unknown'); await create(); storage.write.mockImplementation(() => { throw new Error('Quota'); });
    expect(page.workflow.retry()).toBe(false); http.expectNone('/api/jobs'); expect(page.workflow.state()).toBe('recovery-blocked');
  });
  it('treats an empty transport completion as unknown', async () => {
    await create(); vi.spyOn(TestBed.inject(JobPostingApi), 'post').mockReturnValue(EMPTY);
    expect(page.workflow.submit(payload)).toBe(true); expect(page.workflow.state()).toBe('unknown'); http.expectNone('/api/jobs');
  });
});
