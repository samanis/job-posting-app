import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from '../../../../src/app/app.config';
import { JobListPage } from '../../../../src/app/features/job-search/job-list-page';
const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z' };
describe('URL-to-HTTP list integration', () => {
  let harness: RouterTestingHarness; let http: HttpTestingController; let component: JobListPage;
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); harness = await RouterTestingHarness.create();
    component = await harness.navigateByUrl('/jobs', JobListPage);
  });
  afterEach(() => { http.verify(); vi.useRealTimers(); });
  const request = () => http.expectOne(r => r.url === '/api/jobs');
  async function render() { await harness.fixture.whenStable(); harness.detectChanges(); }
  it('dispatches once initially and once per settled input, cancels stale query and supplies next cursor', async () => {
    const first = request(); expect(harness.routeNativeElement!.textContent).toContain('Loading jobs');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    component.edit('q', 'partial'); component.edit('q', 'final'); await vi.advanceTimersByTimeAsync(300); await render();
    expect(first.cancelled).toBe(true); const next = request(); expect(next.request.params.get('q')).toBe('final');
    next.flush({ items: [job], nextCursor: 'next' }); await render();
    expect(component.nextCursor()).toBe('next'); expect(harness.routeNativeElement!.textContent).toContain('1 job on this results page');
    component.edit('q', ' final '); await vi.advanceTimersByTimeAsync(300); http.expectNone(r => r.url === '/api/jobs');
    component.next(); await render(); const secondPage = request(); expect(secondPage.request.params.get('cursor')).toBe('next');
    secondPage.flush({ items: [], nextCursor: null }); await render(); expect(harness.routeNativeElement!.textContent).toContain('No jobs match');
  });
  it('labels same-query refresh and stale failure and offers explicit retry', async () => {
    request().flush({ items: [job], nextCursor: null }); await render();
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('section[aria-label="Search results"] button')!.click();
    const refresh = request(); await render(); expect(harness.routeNativeElement!.textContent).toContain('Refreshing jobs');
    refresh.flush({}, { status: 500, statusText: 'Failure' }); await render();
    expect(harness.routeNativeElement!.textContent).toContain('out of date'); expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain('temporarily unavailable');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('section[aria-label="Search results"] button')!.click(); request().flush({ items: [], nextCursor: null }); await render();
    expect(component.workflow.state().kind).toBe('empty');
  });
  it('offers first-page reload for expired cursor without automatic restart', async () => {
    const initial = request(); await harness.navigateByUrl('/jobs?cursor=expired', JobListPage); expect(initial.cancelled).toBe(true);
    request().flush({ code: 'cursor_expired' }, { status: 409, statusText: 'Expired' }); await render();
    http.expectNone(r => r.url === '/api/jobs');
    const button = harness.routeNativeElement!.querySelector<HTMLButtonElement>('section[aria-label="Search results"] button')!;
    expect(button.textContent).toContain('Reload first page'); button.click(); await render();
    const first = request(); expect(first.request.params.has('cursor')).toBe(false); first.flush({ items: [], nextCursor: null });
  });
});
