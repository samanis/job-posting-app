import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { appConfig } from '../../../../src/app/app.config';
import { JobDetailPage } from '../../../../src/app/features/job-search/job-detail-page';
const job = { id: 'job-1', title: 'API title', department: 'Platform', location: 'Toronto', salaryMin: 1000.25, salaryMax: 2000.5, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z', description: '<b>Plain text</b>\nSecond line' };
describe('Full job details', () => {
  let harness: RouterTestingHarness; let http: HttpTestingController; let component: JobDetailPage;
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); harness = await RouterTestingHarness.create();
    component = await harness.navigateByUrl('/jobs/job-1?q=Engineer&cursor=deep', JobDetailPage);
  });
  afterEach(() => http.verify());
  async function render() { await harness.fixture.whenStable(); harness.detectChanges(); }
  const request = () => http.expectOne('/api/jobs/job-1');
  it('shows authoritative full data, dates, title, safe multiline description and navigation focus', async () => {
    expect(component.dateLabel()).toBe(''); expect(harness.routeNativeElement!.querySelector('h1')).toBe(document.activeElement);
    request().flush(job); await render();
    const element = harness.routeNativeElement!;
    expect(element.querySelector('h1')?.textContent).toBe('API title'); expect(TestBed.inject(Title).getTitle()).toBe('API title | Job board');
    for (const text of ['Platform', 'Toronto', '1,000.25', '2,000.5', 'January 1, 2027', 'Posted timestamp (UTC)', 'job-1']) expect(element.textContent).toContain(text);
    expect(element.querySelector('.description')?.textContent).toBe(job.description); expect(element.querySelector('b')).toBeNull();
    expect(element.querySelector('a')!.getAttribute('href')).toBe('/jobs?q=Engineer&cursor=deep');
    expect(element.querySelectorAll('time')[0].getAttribute('datetime')).toBe('2027-01-01');
  });
  it('refreshes without stealing focus and labels stale failures', async () => {
    request().flush(job); await render();
    const button = harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!; button.focus(); button.click();
    const refresh = request(); await render(); expect(document.activeElement).toBe(button); expect(harness.routeNativeElement!.textContent).toContain('Refreshing details');
    refresh.flush({}, { status: 500, statusText: 'Failure' }); await render(); expect(harness.routeNativeElement!.textContent).toContain('out of date');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!.click(); request().flush(job); await render();
    expect(component.workflow.state().kind).toBe('ready');
  });
  it.each([404, 410])('shows unavailable %s without old data or retry action', async status => {
    request().flush({}, { status, statusText: 'Unavailable' }); await render();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain('no longer available');
    expect(harness.routeNativeElement!.querySelector('article')).toBeNull(); expect(harness.routeNativeElement!.querySelector('button')).toBeNull();
  });
  it.each([202, 204, 500])('offers retry for response %s without claiming an empty job', async status => {
    request().flush(null, { status, statusText: 'Response' }); await render();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')).not.toBeNull();
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!.click(); request().flush(job); await render();
    expect(component.workflow.data()?.id).toBe('job-1');
  });
  it('cancels a prior detail and focuses the new heading on route-id changes', async () => {
    const old = request(); component = await harness.navigateByUrl('/jobs/job-2', JobDetailPage); expect(old.cancelled).toBe(true);
    http.expectOne('/api/jobs/job-2').flush({ ...job, id: 'job-2', title: 'Next title' }); await render();
    expect(component.workflow.data()?.id).toBe('job-2'); expect(document.activeElement).toBe(harness.routeNativeElement!.querySelector('h1'));
  });
  it('treats a missing route identifier defensively without requesting invented data', async () => {
    request().flush(job); TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [JobDetailPage], providers: [...appConfig.providers, provideHttpClientTesting(), { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParamMap: of(convertToParamMap({ returnTo: 'https://evil.example' })) } }] });
    http = TestBed.inject(HttpTestingController); const fixture = TestBed.createComponent(JobDetailPage); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe('/jobs'); expect(fixture.componentInstance.workflow.state().kind).toBe('error'); http.expectNone(r => r.url.startsWith('/api/jobs'));
  });
});
