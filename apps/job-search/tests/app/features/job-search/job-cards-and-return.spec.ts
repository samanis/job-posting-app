import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from '../../../../src/app/app.config';
import { JobListPage } from '../../../../src/app/features/job-search/job-list-page';
import { ListReturnFocus } from '../../../../src/app/features/job-search/list-return-focus';
import { defaultQuery, queryKey } from '../../../../src/app/features/job-search/search-query';
import { closingDateLabel } from '../../../../src/app/features/job-search/job-presentation';
const job = { id: 'job-1', title: '<b>Title</b>', department: 'Platform', location: 'Toronto', salaryMin: 1000.25, salaryMax: 2000.5, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z' };
describe('Job cards and return focus', () => {
  let harness: RouterTestingHarness; let http: HttpTestingController; let component: JobListPage;
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); harness = await RouterTestingHarness.create(); component = await harness.navigateByUrl('/jobs', JobListPage);
  });
  afterEach(() => http.verify());
  const request = () => http.expectOne(r => r.url === '/api/jobs');
  async function render() { await harness.fixture.whenStable(); harness.detectChanges(); }
  it('renders bounded summaries and stable ids without detail fetches', async () => {
    request().flush({ items: [job], nextCursor: null }); await render();
    const element = harness.routeNativeElement!, link = element.querySelector('[data-job-id]');
    expect(link?.textContent).toBe(job.title); expect(element.querySelector('b')).toBeNull();
    for (const text of ['Platform', 'Toronto', '1,000.25', '2,000.5', 'January 1, 2027']) expect(element.textContent).toContain(text);
    expect(element.querySelector('.description')).toBeNull(); http.expectNone(r => r.url.includes('/api/jobs/'));
    component.workflow.refresh(); request().flush({ items: [{ ...job, title: 'Updated' }], nextCursor: null }); await render();
    expect(element.querySelector('[data-job-id]')).toBe(link); expect(link?.textContent).toBe('Updated');
    expect(closingDateLabel('2024-02-29')).toBe('February 29, 2024');
  });
  it('restores focus to the selected card after returning from details', async () => {
    request().flush({ items: [{ ...job, id: 'other' }, job], nextCursor: null }); await render();
    const link = harness.routeNativeElement!.querySelector<HTMLAnchorElement>('[data-job-id="job-1"]')!;
    link.click(); await render(); http.expectOne('/api/jobs/job-1').flush({ ...job, description: 'Details' }); await render();
    const back = harness.routeNativeElement!.querySelector<HTMLAnchorElement>('a')!; back.click(); await render();
    http.expectNone(r => r.url === '/api/jobs'); expect(document.activeElement).toBe(harness.routeNativeElement!.querySelector('[data-job-id="job-1"]'));
  });
  it('falls back to the list heading when a remembered card is absent', async () => {
    request().flush({ items: [], nextCursor: null }); await render(); component.rememberReturn('absent');
    await harness.navigateByUrl('/missing'); await harness.navigateByUrl('/jobs');
    expect(document.activeElement).toBe(harness.routeNativeElement!.querySelector('h1'));
  });
  it('consumes return intent once and ignores different query identities', () => {
    request().flush({ items: [], nextCursor: null }); const focus = TestBed.inject(ListReturnFocus);
    expect(focus.take('different')).toBeUndefined(); focus.remember(queryKey(defaultQuery), 'job-1');
    expect(focus.take('different')).toBeUndefined(); expect(focus.take(queryKey(defaultQuery))).toBeUndefined();
  });
  it('activates a query-bearing card without encoding criteria into the detail path', async () => {
    request().flush({ items: [], nextCursor: null });
    await harness.navigateByUrl('/jobs?q=Platform&cursor=deep'); request().flush({ items: [job], nextCursor: null }); await render();
    harness.routeNativeElement!.querySelector<HTMLAnchorElement>('[data-job-id]')!.click(); await render();
    http.expectOne('/api/jobs/job-1').flush({ ...job, description: 'Details' }); await render();
    expect(harness.routeNativeElement!.querySelector('a')!.getAttribute('href')).toBe('/jobs?q=Platform&cursor=deep');
    harness.routeNativeElement!.querySelector<HTMLAnchorElement>('a')!.click(); await render();
    expect(document.activeElement).toBe(harness.routeNativeElement!.querySelector('[data-job-id]'));
  });
});
