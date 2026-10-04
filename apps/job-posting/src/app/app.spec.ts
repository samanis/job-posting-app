import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('Application shell', () => {
  it('provides navigation and a keyboard skip target', async () => {
    TestBed.configureTestingModule({ imports: [App], providers: [provideRouter(routes)] });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('header a')?.getAttribute('href')).toBe('/jobs/new');
    expect(element.querySelector('.skip-link')?.getAttribute('href')).toBe('#main-content');
    expect(element.querySelector('main')?.id).toBe('main-content');
    expect(element.querySelector('main')?.getAttribute('tabindex')).toBe('-1');
  });
});
