import { decodeAttempt, freezeAttempt, isPayload, payloadFingerprint, PostingAttempt } from '../../../../../src/app/features/job-posting/state/posting-attempt';

const payload = { title: 'Engineer', department: 'Tech', location: 'Toronto', description: 'Build', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01' };
const attempt: PostingAttempt = { version: 1, key: 'key-1', payload, status: 'unknown', retryAt: null, savedRecord: null };
const saved = { ...payload, id: 'id-1', createdAt: '2026-10-04T15:00:00Z' };

describe('Attempt persistence format', () => {
  it('compares by contract field order regardless of input property order', () => {
    expect(payloadFingerprint(payload)).toBe(payloadFingerprint({ closingDate: payload.closingDate, salaryMax: 20, salaryMin: 10, description: 'Build', location: 'Toronto', department: 'Tech', title: 'Engineer' }));
    expect(payloadFingerprint(payload)).not.toBe(payloadFingerprint({ ...payload, salaryMax: 30 }));
  });
  it('copies and freezes snapshots and saved records', () => {
    const copy = freezeAttempt({ ...attempt, status: 'saved', savedRecord: saved });
    expect(Object.isFrozen(copy)).toBe(true);
    expect(Object.isFrozen(copy.payload)).toBe(true);
    expect(Object.isFrozen(copy.savedRecord)).toBe(true);
    expect(copy.payload).not.toBe(payload);
    expect(copy.savedRecord).not.toBe(saved);
    expect(freezeAttempt(attempt).savedRecord).toBeNull();
  });
  it.each([null, [], 'text'])('rejects non-payload %j', value => expect(isPayload(value)).toBe(false));
  it.each(['title', 'department', 'location', 'description'])('rejects invalid normalized text %s', field => {
    for (const value of [null, '', ' ', ' text ']) expect(isPayload({ ...payload, [field]: value })).toBe(false);
  });
  it.each(['salaryMin', 'salaryMax'])('rejects invalid salary %s', field => {
    for (const value of ['10', -1, NaN, Infinity, 1.234]) expect(isPayload({ ...payload, [field]: value })).toBe(false);
  });
  it('validates bounds and date format without rejecting old retry payloads', () => {
    expect(isPayload(payload)).toBe(true);
    expect(isPayload({ ...payload, closingDate: '2020-02-29' })).toBe(true);
    expect(isPayload({ ...payload, salaryMin: 20 })).toBe(false);
    expect(isPayload({ ...payload, closingDate: 1 })).toBe(false);
    expect(isPayload({ ...payload, closingDate: '2027-02-29' })).toBe(false);
  });
  it.each(['in-flight', 'unknown', 'pending', 'throttled', 'conflict', 'rejected'])('decodes %s without a saved record', status => {
    expect(decodeAttempt(JSON.stringify({ ...attempt, status, retryAt: 10000 }))).toMatchObject({ status, retryAt: 10000 });
  });
  it('decodes confirmed saved state', () => expect(decodeAttempt(JSON.stringify({ ...attempt, status: 'saved', savedRecord: saved }))).toMatchObject({ savedRecord: saved }));
  it.each(['bad', 'null', '[]', '"text"'])('rejects malformed top-level %s', raw => expect(() => decodeAttempt(raw)).toThrow());
  it.each([{ version: 2 }, { key: null }, { key: '' }, { key: 'bad key' }, { payload: {} }, { status: 'bad' }, { retryAt: '1' }, { retryAt: 1.5 }, { retryAt: -1 }, { retryAt: 1e20 }, { retryAt: undefined }, { savedRecord: {} }, { status: 'saved', savedRecord: null }])('rejects malformed fields %j', patch => expect(() => decodeAttempt(JSON.stringify({ ...attempt, ...patch }))).toThrow());
});
