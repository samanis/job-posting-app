import { TestBed } from '@angular/core/testing';
import { SavedJobConfirmation } from '../../../../../src/app/features/job-posting/components/saved-job-confirmation';

const record = { id: 'saved-42', title: 'Server engineer', department: 'Platform', location: 'Toronto', description: '<script>alert(1)</script>\nSecond line', salaryMin: 1000.5, salaryMax: 2500, closingDate: '2027-01-01', createdAt: '2026-10-04T15:00:00Z' };

describe('Saved job confirmation', () => {
  it('shows every authoritative field with safe text and meaningful date/number formatting', async () => {
    const fixture = TestBed.createComponent(SavedJobConfirmation);
    fixture.componentRef.setInput('record', record); await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    for (const value of ['saved-42', 'Server engineer', 'Platform', 'Toronto', '1,000.5', '2,500', 'January 1, 2027', 'Oct 4, 2026, 3:00:00 PM UTC', '<script>alert(1)</script>', 'Second line']) expect(root.textContent?.replace(/\s+/g, ' ')).toContain(value);
    expect(root.querySelector('script')).toBeNull();
    expect(root.textContent).not.toContain('$');
    expect(root.querySelector('time')?.getAttribute('datetime')).toBe('2027-01-01');
    expect(root.querySelector('h2')?.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement?.id).toBe('confirmation-heading');
    const emit = vi.spyOn(fixture.componentInstance.postAnother, 'emit');
    root.querySelector<HTMLButtonElement>('button')!.click(); expect(emit).toHaveBeenCalledOnce();
  });
  it('retains the calendar day and reacts to new signal input', async () => {
    const fixture = TestBed.createComponent(SavedJobConfirmation);
    fixture.componentRef.setInput('record', { ...record, closingDate: '2028-02-29' }); await fixture.whenStable();
    expect(fixture.componentInstance.closingDateLabel()).toBe('February 29, 2028');
    fixture.componentRef.setInput('record', record); await fixture.whenStable();
    expect(fixture.componentInstance.closingDateLabel()).toBe('January 1, 2027');
  });
});
