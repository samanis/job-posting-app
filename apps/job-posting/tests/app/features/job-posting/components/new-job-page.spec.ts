import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ATTEMPT_STORAGE, ATTEMPT_UUID } from '../../../../../src/app/features/job-posting/state/posting-attempt-store';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NewJobPage } from '../../../../../src/app/features/job-posting/components/new-job-page';
import { LOCAL_CLOCK } from '../../../../../src/app/features/job-posting/validators/job-validation';
import { JobDraft } from '../../../../../src/app/features/job-posting/models/job-draft';

const valid: JobDraft = { title: ' Engineer ', department: ' Engineering ', location: ' Toronto ', description: ' Build software ', salaryMin: '10.25', salaryMax: '20.50', closingDate: '2026-10-05' };

describe('Job Signal Form', () => {
  let fixture: ComponentFixture<NewJobPage>;
  let page: NewJobPage;
  let root: HTMLElement;
  let now: Date;
  beforeEach(async () => {
    now = new Date(2026, 9, 4, 23, 59);
    TestBed.configureTestingModule({ imports: [NewJobPage], providers: [provideHttpClientTesting(), { provide: ATTEMPT_UUID, useValue: () => 'form-test-key' }, { provide: ATTEMPT_STORAGE, useFactory: () => ({ read: () => null, write: vi.fn(), remove: vi.fn() }) }, { provide: LOCAL_CLOCK, useValue: () => now }] });
    fixture = TestBed.createComponent(NewJobPage);
    page = fixture.componentInstance;
    root = fixture.nativeElement;
    await fixture.whenStable();
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  async function submit() {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
  }
  async function input(field: string, value: string) {
    const control = root.querySelector<HTMLInputElement>(`#${field}`)!;
    control.value = value;
    control.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
  }
  it('renders seven labeled controls and help/error associations without initial errors', () => {
    expect(root.querySelectorAll('input, textarea')).toHaveLength(7);
    for (const field of page.fields) {
      expect(root.querySelector(`label[for="${field}"]`)?.textContent).toBe(page.labels[field]);
      expect(root.querySelector(`#${field}`)?.getAttribute('aria-describedby')).toBe(`${field}-help ${field}-errors`);
    }
    expect(root.querySelector('#closingDate')?.getAttribute('type')).toBe('date');
    expect(root.querySelector('#salaryMin')?.getAttribute('inputmode')).toBe('decimal');
    expect(root.querySelector('.error-summary')).toBeNull();
  });
  it('rejects an empty submit and focuses the first invalid field', async () => {
    const emit = vi.spyOn(page.validPayload, 'emit');
    await submit();
    expect(emit).not.toHaveBeenCalled();
    expect(document.activeElement?.id).toBe('title');
    expect(root.querySelector('#title')?.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('.error-summary a')?.getAttribute('href')).toBe('#title');
  });
  it('shows local errors after blur and clears them on correction', async () => {
    const control = root.querySelector<HTMLInputElement>('#title')!;
    control.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(page.messages('title')).toContain('This field is required.');
    await input('title', 'Engineer');
    expect(page.messages('title')).toEqual([]);
  });
  it('emits a normalized valid payload exactly once and starts one HTTP request', async () => {
    page.draft.set({ ...valid });
    await fixture.whenStable();
    const emit = vi.spyOn(page.validPayload, 'emit');
    await submit();
    expect(emit).toHaveBeenCalledExactlyOnceWith({ title: 'Engineer', department: 'Engineering', location: 'Toronto', description: 'Build software', salaryMin: 10.25, salaryMax: 20.5, closingDate: '2026-10-05' });
    const request = TestBed.inject(HttpTestingController).expectOne('/api/jobs');
    request.flush({ ...request.request.body, id: 'saved-job', createdAt: '2026-10-04T15:00:00Z' });
    await fixture.whenStable();
    expect(page.draft().title).toBe(' Engineer ');
    expect(root.querySelector('.error-summary')).toBeNull();
  });
  it('revalidates after midnight even when the draft is unchanged', async () => {
    page.draft.set({ ...valid }); await fixture.whenStable();
    expect(page.jobForm.closingDate().valid()).toBe(true);
    now = new Date(2026, 9, 5, 0, 1);
    const emit = vi.spyOn(page.validPayload, 'emit');
    await submit();
    expect(emit).not.toHaveBeenCalled();
    expect(page.messages('closingDate')).toContain('Closing date must be later than today.');
    expect(document.activeElement?.id).toBe('closingDate');
  });
  it('renders server errors as text and clears only the edited field permanently', async () => {
    page.draft.set({ ...valid }); await fixture.whenStable();
    page.setServerErrors({ title: ['<b>Server error</b>'], department: ['Department error'] }, ['General issue']);
    await fixture.whenStable();
    expect(root.querySelector('#title-errors')?.textContent).toContain('<b>Server error</b>');
    expect(root.querySelector('#title-errors b')).toBeNull();
    expect(root.querySelector('.error-summary')?.textContent).toContain('General issue');
    await input('title', 'Changed');
    expect(page.messages('title')).toEqual([]);
    expect(page.messages('department')).toEqual(['Department error']);
    expect(page.serverForm()).toEqual(['General issue']);
    await input('title', valid.title);
    expect(page.messages('title')).toEqual([]);
    page.setServerErrors({}); await fixture.whenStable();
    expect(page.serverForm()).toEqual([]);
  });
  it('combines local and server messages and handles native errors without messages', async () => {
    page.attempted.set(true);
    page.setServerErrors({ title: ['Server required'] });
    await fixture.whenStable();
    expect(page.messages('title')).toEqual(['This field is required.', 'Server required']);
  });
});

