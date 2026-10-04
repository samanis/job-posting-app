import { detailSnapshot, isJobDetail, isJobPage, isJobSummary, validCursor, validDate, validText } from '../../../../src/app/core/api/query-validation';

export const job = { id: 'job-1', title: 'Engineer', department: 'Platform', location: 'Toronto', salaryMin: 10.25, salaryMax: 20.5, closingDate: '2020-02-29', createdAt: '2026-10-04T15:00:00Z', description: 'Plain text' };
describe('Query body validation', () => {
  it('accepts real dates including past closing dates', () => {
    expect(isJobDetail(job)).toBe(true);
    expect(validDate('2024-02-29')).toBe(true);
  });
  it.each([null, 1, '', '2026-2-01', '0000-01-01', '2026-02-29', '2026-99-01'])('rejects invalid date %s', value => expect(validDate(value)).toBe(false));
  it.each([null, [], 2])('rejects non-object job %s', value => expect(isJobSummary(value)).toBe(false));
  it.each(['id', 'title', 'department', 'location'])('requires bounded text for %s', field => {
    for (const value of [null, '', ' ', 'a'.repeat(1001)]) expect(isJobSummary({ ...job, [field]: value })).toBe(false);
  });
  it.each([null, '1', NaN, Infinity, -1, 1.001, Number.MAX_SAFE_INTEGER])('rejects salary %s', value => expect(isJobSummary({ ...job, salaryMin: value })).toBe(false));
  it('requires valid max and strict min/max', () => {
    expect(isJobSummary({ ...job, salaryMax: NaN })).toBe(false);
    expect(isJobSummary({ ...job, salaryMax: 10.25 })).toBe(false);
    expect(isJobSummary({ ...job, salaryMin: 0, closingDate: 'bad' })).toBe(false);
  });
  it.each([null, '', '2026-10-04', '2026-02-30T15:00:00Z', '2026-10-04T24:00:00Z', '2026-10-04T15:60:00Z', '2026-10-04T15:00:00+24:00'])('rejects invalid timestamp %s', value => expect(isJobSummary({ ...job, createdAt: value })).toBe(false));
  it('accepts timestamp fractions and offsets', () => expect(isJobSummary({ ...job, createdAt: '2026-10-04T15:00:00.123+05:30' })).toBe(true));
  it('requires bounded description and snapshots only contracted fields', () => {
    expect(isJobDetail({ ...job, description: ' ' })).toBe(false);
    expect(isJobDetail({ ...job, description: 'a'.repeat(100001) })).toBe(false);
    expect(isJobDetail(null)).toBe(false);
    const data = detailSnapshot(job);
    expect(data).toEqual(job);
    expect(Object.isFrozen(data)).toBe(true);
  });
  it('checks cursor type, bounds and controls', () => {
    for (const value of [null, '', ' ', 'a b', '\u007f', 'a'.repeat(2049)]) expect(validCursor(value)).toBe(false);
    expect(validCursor('opaque+/=?')).toBe(true);
    expect(validText('a'.repeat(1000), 1000)).toBe(true);
  });
  it('checks page shape, size, item schema, identity and cursor', () => {
    for (const value of [null, [], {}, { items: null }, { items: [job, job], nextCursor: null }, { items: [{}], nextCursor: null }, { items: [job], nextCursor: '' }, { items: [], nextCursor: 'next' }]) expect(isJobPage(value, 20)).toBe(false);
    expect(isJobPage({ items: [job], nextCursor: null }, 0)).toBe(false);
    expect(isJobPage({ items: [job], nextCursor: 'next' }, 1)).toBe(true);
    expect(isJobPage({ items: [], nextCursor: null }, 20)).toBe(true);
  });
  it('accepts exact bounded text/cursor/page limits and rejects the next item', () => {
    const summary = { ...job, title: 'a'.repeat(1000), salaryMin: 0.1, salaryMax: 0.3, closingDate: '0001-01-01' };
    expect(isJobSummary(summary)).toBe(true);
    expect(validCursor('a'.repeat(2048))).toBe(true);
    const items = Array.from({ length: 50 }, (_, i) => ({ ...summary, id: String(i) }));
    expect(isJobPage({ items, nextCursor: 'next' }, 50)).toBe(true);
    expect(isJobPage({ items: [...items, { ...summary, id: 'extra' }], nextCursor: 'next' }, 50)).toBe(false);
  });
});
