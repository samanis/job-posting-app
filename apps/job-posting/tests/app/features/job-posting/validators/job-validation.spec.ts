import { TestBed } from '@angular/core/testing';
import { closingDateError, fieldError, LOCAL_CLOCK, localDate, normalizeDraft, salaryError, validationMessage } from '../../../../../src/app/features/job-posting/validators/job-validation';
import { EMPTY_DRAFT } from '../../../../../src/app/features/job-posting/models/job-draft';

const valid = { title: ' Engineer ', department: ' Tech ', location: ' Toronto ', description: ' Build ', salaryMin: '0', salaryMax: '10.25', closingDate: '2027-01-01' };

describe('Job validation', () => {
  it('supplies a fallback for native parse errors', () => {
    expect(validationMessage({})).toBe('Enter a valid value.');
    expect(validationMessage({ message: 'Required' })).toBe('Required');
  });
  it.each(['', ' ', 'NaN', 'Infinity', '-1', '1.234', '1e3', '.5', '9'.repeat(400)])('rejects salary %j', value => expect(salaryError(value)).not.toBeNull());
  it.each(['0', '1', '100.25', ' 1.20 '])('accepts salary %j', value => expect(salaryError(value)).toBeNull());
  it.each(['', 'bad', '2027-02-29', '9999-99-99', '2026-10-04', '2026-10-03'])('rejects closing date %s', value => expect(closingDateError(value, '2026-10-04')).not.toBeNull());
  it.each([['2026-10-05', '2026-10-04'], ['2028-02-29', '2028-02-28'], ['2027-01-01', '2026-12-31']])('accepts future date %s after %s', (value, today) => expect(closingDateError(value, today)).toBeNull());
  it('uses local calendar components rather than UTC dates', () => {
    expect(localDate(new Date(2026, 0, 2, 23, 59))).toBe('2026-01-02');
    const date = new Date('2026-01-02T02:00:00Z');
    vi.spyOn(date, 'getFullYear').mockReturnValue(2026);
    vi.spyOn(date, 'getMonth').mockReturnValue(0);
    vi.spyOn(date, 'getDate').mockReturnValue(1);
    expect(localDate(date)).toBe('2026-01-01');
  });
  it('provides a real clock by default', () => {
    const now = TestBed.inject(LOCAL_CLOCK)();
    expect(now).toBeInstanceOf(Date);
    expect(Number.isFinite(now.getTime())).toBe(true);
  });
  it('validates whitespace and valid text', () => {
    expect(fieldError('title', EMPTY_DRAFT, '2026-10-04')).not.toBeNull();
    expect(fieldError('title', { ...valid, title: ' ' }, '2026-10-04')).not.toBeNull();
    expect(fieldError('description', valid, '2026-10-04')).toBeNull();
  });
  it('validates dates through the field schema', () => expect(fieldError('closingDate', valid, '2026-10-04')).toBeNull());
  it('validates salaries before their relationship', () => {
    expect(fieldError('salaryMin', EMPTY_DRAFT, '2026-10-04')).not.toBeNull();
    expect(fieldError('salaryMax', { ...valid, salaryMin: '' }, '2026-10-04')).toBeNull();
    expect(fieldError('salaryMin', { ...valid, salaryMax: '' }, '2026-10-04')).toBeNull();
    expect(fieldError('salaryMin', valid, '2026-10-04')).toBeNull();
    for (const salaryMax of ['0', '1']) {
      const draft = { ...valid, salaryMin: '1', salaryMax };
      expect(fieldError('salaryMin', draft, '2026-10-04')).toContain('less than');
      expect(fieldError('salaryMax', draft, '2026-10-04')).toContain('less than');
    }
  });
  it('normalizes a valid draft without changing display values', () => {
    expect(normalizeDraft(valid)).toEqual({ title: 'Engineer', department: 'Tech', location: 'Toronto', description: 'Build', salaryMin: 0, salaryMax: 10.25, closingDate: '2027-01-01' });
    expect(valid.title).toBe(' Engineer ');
  });
});
