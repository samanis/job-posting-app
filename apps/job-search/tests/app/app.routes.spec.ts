import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Title } from '@angular/platform-browser';
import { appConfig } from '../../src/app/app.config';

describe('Search routes', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [...appConfig.providers, provideHttpClientTesting()] }));
  it('redirects root to the listing with a route title', async () => {
    const harness = await RouterTestingHarness.create('/');
    expect(TestBed.inject(Router).url).toBe('/jobs');
    expect(TestBed.inject(Title).getTitle()).toBe('Find jobs');
    expect(harness.routeNativeElement?.textContent).toContain('No job data has been loaded.');
  });
  it('opens the listing directly', async () => {
    const harness = await RouterTestingHarness.create('/jobs');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Find jobs');
  });
  it('opens lazy details directly without invented data', async () => {
    const harness = await RouterTestingHarness.create('/jobs/example-id');
    expect(TestBed.inject(Title).getTitle()).toBe('Job details');
    expect(harness.routeNativeElement?.textContent).toContain('Loading job details');
    expect(harness.routeNativeElement?.querySelector('a')?.getAttribute('href')).toBe('/jobs');
  });
  it('offers a return link on unknown routes', async () => {
    const harness = await RouterTestingHarness.create('/missing');
    expect(TestBed.inject(Title).getTitle()).toBe('Page not found');
    expect(harness.routeNativeElement?.querySelector('a')?.getAttribute('href')).toBe('/jobs');
    await harness.navigateByUrl('/jobs');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Find jobs');
  });
});
