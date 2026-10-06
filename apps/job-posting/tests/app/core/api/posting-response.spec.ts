import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { classifyFailure, classifyResponse, isSavedJob, parseRetryAfter } from '../../../../src/app/core/api/posting-response';

export const saved = { id: 'job-1', title: 'Engineer', department: 'Engineering', location: 'Toronto', description: 'Build software', salaryMin: 10, salaryMax: 20, closingDate: '2027-02-28', createdAt: '2026-10-04T15:00:00Z' };
const now = Date.parse('2026-10-04T15:00:00Z');
const failure = (status: number, error: unknown = null, headers = new HttpHeaders()) => classifyFailure(new HttpErrorResponse({ status, error, headers }), now);

describe('Saved record validation', () => {
  it('accepts a complete record, including an expired closing date on replay', () => {
    expect(isSavedJob(saved)).toBe(true);
    expect(isSavedJob({ ...saved, closingDate: '2020-02-29', createdAt: '2026-10-04T15:00:00.123+01:00' })).toBe(true);
  });
  it.each([null, [], 'text', 1])('rejects non-record values: %j', value => expect(isSavedJob(value)).toBe(false));
  it.each(['id', 'title', 'department', 'location', 'description'])('requires nonblank text for %s', field => {
    for (const value of [undefined, 42, '', '   ']) expect(isSavedJob({ ...saved, [field]: value })).toBe(false);
  });
  it.each(['salaryMin', 'salaryMax'])('requires finite nonnegative %s', field => {
    for (const value of [undefined, '10', NaN, Infinity, -1]) expect(isSavedJob({ ...saved, [field]: value })).toBe(false);
  });
  it.each([10, 9])('rejects maximum %s at or below minimum', salaryMax => expect(isSavedJob({ ...saved, salaryMax })).toBe(false));
  it.each([null, 'bad', '2027-02-29', '9999-99-99'])('rejects closing date %j', closingDate => expect(isSavedJob({ ...saved, closingDate })).toBe(false));
  it.each([null, 'bad', '2026-02-30T15:00:00Z', '2026-10-04T99:00:00Z'])('rejects timestamp %j', createdAt => expect(isSavedJob({ ...saved, createdAt })).toBe(false));
});

describe('Retry-After', () => {
  it.each([null, '', '-1', '1.5', 'tomorrow', '999999999999999999999', 'Sun, 99 Oct 2026 15:00:00 GMT', 'Mon, 04 Oct 2026 15:00:00 GMT'])('rejects %j', value => expect(parseRetryAfter(value, now)).toBeNull());
  it('handles seconds, zero and whitespace', () => {
    expect(parseRetryAfter(' 12 ', now)).toBe(now + 12000);
    expect(parseRetryAfter('0', now)).toBe(now);
  });
  it('handles future and elapsed dates', () => {
    expect(parseRetryAfter('Sun, 04 Oct 2026 15:01:00 GMT', now)).toBe(now + 60000);
    expect(parseRetryAfter('Sun, 04 Oct 2026 14:59:00 GMT', now)).toBe(now);
  });
});

describe('Response classification', () => {
  it.each([200, 201, 203, 206])('recognizes complete saved response %s', status => expect(classifyResponse(new HttpResponse({ status, body: saved }))).toEqual({ kind: 'saved', status, record: saved }));
  it('recognizes the API committed 202 record with source timestamp precision', () => expect(classifyResponse(new HttpResponse({ status: 202, body: { ...saved, createdAt: '2026-10-04T15:00:00.1234567+00:00' } }))).toMatchObject({ kind: 'saved', status: 202 }));
  it('keeps an incomplete 202 unconfirmed', () => expect(classifyResponse(new HttpResponse({ status: 202, body: {} }))).toMatchObject({ kind: 'pending', reason: 'accepted' }));
  it.each([null, {}, 'bad'])('preserves uncertainty for malformed success %j', body => expect(classifyResponse(new HttpResponse({ status: 200, body }))).toMatchObject({ kind: 'unknown', reason: 'invalid-success' }));
  it('preserves uncertainty for no content', () => expect(classifyResponse(new HttpResponse({ status: 204 }))).toMatchObject({ kind: 'unknown' }));
  it.each([400, 422])('maps field and form validation for %s', status => {
    expect(failure(status, { errors: { Title: ['Required'], title: ['Too short'], SalaryMin: ['Range'], Other: ['General'], ignored: [' ', 7], malformed: 'bad' } })).toEqual({ kind: 'validation', status, fieldErrors: { title: ['Required', 'Too short'], salaryMin: ['Range'] }, formErrors: ['General'] });
  });
  it('retains form-only validation', () => expect(failure(422, { errors: { Other: ['Issue'] } })).toMatchObject({ kind: 'validation', fieldErrors: {}, formErrors: ['Issue'] }));
  it.each([null, 'HTML', [], {}, { errors: [] }, { errors: {} }, { errors: { Title: [null, ''] } }])('falls back for malformed validation %j', body => expect(failure(400, body)).toMatchObject({ kind: 'rejected', status: 400 }));
  it('distinguishes idempotency conflicts', () => {
    expect(failure(409, { code: 'idempotency_in_progress' })).toMatchObject({ kind: 'pending', reason: 'in-progress' });
    expect(failure(409, { code: 'idempotency_key_conflict' })).toMatchObject({ kind: 'conflict', reason: 'key-mismatch' });
    expect(failure(409, {})).toMatchObject({ kind: 'conflict', reason: 'general' });
    expect(failure(409, 'bad')).toMatchObject({ kind: 'conflict', reason: 'general' });
  });
  it('handles throttling with and without delay', () => {
    expect(failure(429, null, new HttpHeaders({ 'Retry-After': '10' }))).toMatchObject({ kind: 'throttled', retryAt: now + 10000 });
    expect(failure(429)).toMatchObject({ kind: 'throttled', retryAt: null });
  });
  it.each([404, 405, 408, 413, 499])('handles generic client status %s', status => expect(failure(status)).toMatchObject({ kind: 'rejected', status }));
  it.each([500, 503, 599])('preserves uncertainty on server status %s', status => expect(failure(status)).toMatchObject({ kind: 'unknown', reason: 'server' }));
  it('handles network, timeout, unexpected errors and JSON parse failures', () => {
    expect(failure(0)).toMatchObject({ reason: 'network' });
    expect(failure(200)).toMatchObject({ reason: 'invalid-success' });
    expect(failure(302)).toMatchObject({ reason: 'unexpected' });
    expect(failure(600)).toMatchObject({ reason: 'unexpected' });
    expect(classifyFailure(new Error('private diagnostic'), now)).toMatchObject({ reason: 'unexpected' });
    expect(classifyFailure(new TimeoutError(), now)).toMatchObject({ reason: 'timeout' });
  });
});

