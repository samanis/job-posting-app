import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../src/app/app.routes';
import { appConfig } from '../../src/app/app.config';

describe('Application routes', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter(routes), ...appConfig.providers] }));

  it('redirects the root to the lazy job posting page', async () => {
    const harness = await RouterTestingHarness.create('/');
    expect(TestBed.inject(Router).url).toBe('/jobs/new');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Post a job');
    expect(harness.routeNativeElement?.textContent).toContain('Create a new opening');
  });

  it('opens the job posting page directly', async () => {
    const harness = await RouterTestingHarness.create('/jobs/new');
    expect(harness.routeNativeElement?.querySelector('section')?.getAttribute('aria-labelledby')).toBe('new-job-heading');
  });

  it('offers a return link for an unknown route', async () => {
    const harness = await RouterTestingHarness.create('/missing');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Page not found');
    expect(harness.routeNativeElement?.querySelector('a')?.getAttribute('href')).toBe('/jobs/new');
    await harness.navigateByUrl('/jobs/new');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Post a job');
  });
});
