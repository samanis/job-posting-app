import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from '../../../../src/app/app.config';
import { JobListPage } from '../../../../src/app/features/job-search/job-list-page';
import { defaultQuery } from '../../../../src/app/features/job-search/search-query';

describe('URL-driven search controls', () => {
  let harness: RouterTestingHarness;
  let page: JobListPage;
  let router: Router;
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] });
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl('/jobs', JobListPage); router = TestBed.inject(Router);
  });
  afterEach(() => vi.useRealTimers());
  async function settled() { await harness.fixture.whenStable(); harness.detectChanges(); }
  function input(id: string, text: string, type = 'input') {
    const element = harness.routeNativeElement!.querySelector<HTMLInputElement>(`#${id}`)!;
    element.value = text; element.dispatchEvent(new Event(type, { bubbles: true }));
  }
  it('debounces once, replaces history and commits only through the URL', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); const nav = vi.spyOn(router, 'navigate');
    input('keyword', 'C'); input('keyword', ' C++ & /? ');
    expect(page.committed()).toEqual(defaultQuery);
    await vi.advanceTimersByTimeAsync(299); expect(nav).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1); await settled();
    expect(nav).toHaveBeenCalledTimes(1); expect(nav).toHaveBeenCalledWith(['/jobs'], { queryParams: { q: 'C++ & /?' }, replaceUrl: true });
    expect(page.committed().q).toBe('C++ & /?');
    input('keyword', ' C++ & /? '); await vi.advanceTimersByTimeAsync(300); expect(nav).toHaveBeenCalledTimes(1);
  });
  it('submits immediately, clears the debounce and resets a cursor on filter change', async () => {
    page = await harness.navigateByUrl('/jobs?q=old&cursor=deep', JobListPage);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); const nav = vi.spyOn(router, 'navigate');
    input('department', ' Platform '); input('location', ' Toronto ');
    harness.routeNativeElement!.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await settled(); await vi.advanceTimersByTimeAsync(300);
    expect(nav).toHaveBeenCalledTimes(1); expect(nav.mock.calls[0][1]?.replaceUrl).toBe(false);
    expect(page.committed()).toMatchObject({ q: 'old', department: 'Platform', location: 'Toronto', cursor: null });
  });
  it('does not submit partial composition and commits the final text once', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); const nav = vi.spyOn(router, 'navigate');
    input('keyword', 'partial'); input('keyword', 'partial', 'compositionstart'); input('keyword', 'part');
    page.submit(new Event('submit')); await vi.advanceTimersByTimeAsync(500); expect(nav).not.toHaveBeenCalled();
    input('keyword', '完成', 'compositionend'); input('keyword', '完成');
    await vi.advanceTimersByTimeAsync(300); await settled(); expect(nav).toHaveBeenCalledTimes(1); expect(page.committed().q).toBe('完成');
  });
  it('wires composition for department and location without partial commits', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); const nav = vi.spyOn(router, 'navigate');
    input('department', 'a', 'compositionstart'); input('location', 'b', 'compositionstart');
    input('department', '完成', 'compositionend'); await vi.advanceTimersByTimeAsync(300); expect(nav).not.toHaveBeenCalled();
    input('location', 'Toronto', 'compositionend'); await vi.advanceTimersByTimeAsync(300); await settled();
    expect(page.committed().department).toBe('完成'); expect(page.committed().location).toBe('Toronto');
  });
  it('shows invalid draft feedback and does not navigate until corrected', async () => {
    const nav = vi.spyOn(router, 'navigate');
    page.edit('q', 'a'.repeat(201)); page.submit(new Event('submit')); harness.detectChanges();
    expect(nav).not.toHaveBeenCalled(); expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain('Invalid q');
    page.edit('q', 'fixed'); page.submit(new Event('submit')); await settled(); expect(page.errors()).toEqual([]);
  });
  it('clears once including paginated default criteria and cancels pending edits', async () => {
    page = await harness.navigateByUrl('/jobs?cursor=deep', JobListPage);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); const nav = vi.spyOn(router, 'navigate');
    page.edit('q', 'pending'); page.compositionStart('q');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button[type=button]')!.click();
    await settled(); await vi.advanceTimersByTimeAsync(500); expect(nav).toHaveBeenCalledTimes(1);
    expect(router.url).toBe('/jobs'); expect(page.committed()).toEqual(defaultQuery);
    page.clear(); expect(nav).toHaveBeenCalledTimes(1);
  });
  it('sort and limit change immediately and reset pagination', async () => {
    page = await harness.navigateByUrl('/jobs?cursor=deep', JobListPage);
    const sort = harness.routeNativeElement!.querySelector<HTMLSelectElement>('#sort')!;
    sort.value = 'closing-soon'; sort.dispatchEvent(new Event('change')); await settled();
    expect(page.committed()).toMatchObject({ sort: 'closing-soon', cursor: null });
    const limit = harness.routeNativeElement!.querySelector<HTMLSelectElement>('#limit')!;
    limit.value = '50'; limit.dispatchEvent(new Event('change')); await settled();
    expect(page.committed().limit).toBe(50);
  });
  it('restores controls on route-history changes without echo navigation', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); page.edit('q', 'pending'); const nav = vi.spyOn(router, 'navigate');
    await harness.navigateByUrl('/jobs?q=restored&limit=7', JobListPage); await vi.advanceTimersByTimeAsync(300);
    expect(nav).not.toHaveBeenCalled(); expect(page.draft().q).toBe('restored');
    expect(harness.routeNativeElement!.querySelector<HTMLSelectElement>('#limit')!.value).toBe('7');
    await harness.navigateByUrl('/jobs?q=before', JobListPage); expect(page.committed().q).toBe('before');
    await harness.navigateByUrl('/jobs?q=restored&limit=7', JobListPage); expect(page.draft().q).toBe('restored');
  });
  it('displays repaired URL feedback with bounded committed values', async () => {
    page = await harness.navigateByUrl('/jobs?sort=bad&cursor=deep', JobListPage);
    expect(page.committed()).toEqual(defaultQuery); expect(page.errors().length).toBe(1);
    expect(harness.routeNativeElement!.querySelector('[role=alert]')).not.toBeNull();
  });
  it('navigates known cursors and guards missing/repeated next pages', async () => {
    const nav = vi.spyOn(router, 'navigate'); page.next(); page.previous(); page.first(); expect(nav).not.toHaveBeenCalled();
    page.nextCursor.set('next'); page.next(); await settled();
    expect(page.committed().cursor).toBe('next'); expect(page.history.canPrevious()).toBe(true); expect(page.nextCursor()).toBeNull();
    page.nextCursor.set('next'); page.next(); expect(nav).toHaveBeenCalledTimes(1);
    page.previous(); await settled(); expect(router.url).toBe('/jobs');
    await harness.navigateByUrl('/jobs?cursor=unknown', JobListPage); expect(page.history.canPrevious()).toBe(false);
    page.first(); await settled(); expect(page.committed().cursor).toBeNull();
  });
  it('preserves canonical listing criteria through detail and safe return links', async () => {
    page = await harness.navigateByUrl('/jobs?q=C%2B%2B&cursor=deep&location=Toronto', JobListPage);
    const url = page.detailUrl('job/1'); expect(url).toContain('job%2F1');
    await harness.navigateByUrl(url);
    const link = harness.routeNativeElement!.querySelector<HTMLAnchorElement>('a')!;
    expect(link.getAttribute('href')).toContain('/jobs?q=C%2B%2B'); expect(link.getAttribute('href')).toContain('cursor=deep');
    await harness.navigateByUrl(link.getAttribute('href')!); expect(router.url).toContain('cursor=deep');
    await harness.navigateByUrl('/jobs/job-1?returnTo=https://evil.example');
    expect(harness.routeNativeElement!.querySelector('a')!.getAttribute('href')).toBe('/jobs');
  });
  it('cancels pending debounce on component destruction', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }); page.edit('q', 'abandoned'); const nav = vi.spyOn(router, 'navigate');
    await harness.navigateByUrl('/missing'); await vi.advanceTimersByTimeAsync(300); expect(nav).not.toHaveBeenCalled();
  });
});
