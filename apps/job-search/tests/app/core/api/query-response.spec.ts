import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { classifyFailure, classifyResponse, parseRetryAfter } from '../../../../src/app/core/api/query-response';
const now = Date.UTC(2026, 9, 4, 15);
describe('Query outcomes', () => {
  it.each([null, '', '-1', '1.2', 'tomorrow', '9'.repeat(400), 'Sun, 30 Feb 2026 15:00:00 GMT', 'Mon, 04 Oct 2026 15:00:00 GMT', 'Sun, 04 Oct 2026 99:00:00 GMT'])('rejects unusable Retry-After %s', value => expect(parseRetryAfter(value, now)).toBeNull());
  it('bounds numeric and date delays and permits elapsed/zero delays', () => {
    expect(parseRetryAfter(' 2 ', now)).toBe(now + 2000);
    expect(parseRetryAfter('0', now)).toBe(now);
    expect(parseRetryAfter('999999999', now)).toBe(now + 86400000);
    expect(parseRetryAfter(new Date(now + 3000).toUTCString(), now)).toBe(now + 3000);
    expect(parseRetryAfter(new Date(now - 3000).toUTCString(), now)).toBe(now);
    expect(parseRetryAfter(new Date(now + 2 * 86400000).toUTCString(), now)).toBe(now + 86400000);
  });
  it('accepts only validated 200 and distinguishes accepted from empty', () => {
    const validate = (value: unknown): value is string => typeof value === 'string';
    expect(classifyResponse(new HttpResponse({ status: 200, body: 'ok' }), validate, v => v).kind).toBe('ready');
    expect(classifyResponse(new HttpResponse({ status: 200, body: null }), validate, v => v).kind).toBe('protocol-error');
    for (const status of [201, 204, 206]) expect(classifyResponse(new HttpResponse({ status, body: 'ok' }), validate, v => v).kind).toBe('protocol-error');
    expect(classifyResponse(new HttpResponse({ status: 202 }), validate, v => v).kind).toBe('not-ready');
  });
  it.each([[0, false, 'network-error'], [200, false, 'protocol-error'], [299, false, 'protocol-error'], [400, false, 'invalid-query'], [422, false, 'invalid-query'], [404, true, 'unavailable'], [410, true, 'unavailable'], [404, false, 'client-error'], [410, false, 'client-error'], [409, true, 'client-error'], [405, false, 'client-error'], [500, false, 'server-error'], [599, false, 'server-error'], [302, false, 'unexpected-error'], [600, false, 'unexpected-error']] as const)('classifies %s detail=%s', (status, detail, kind) => {
    expect(classifyFailure(new HttpErrorResponse({ status, error: '<script>unsafe</script>' }), now, detail).kind).toBe(kind);
  });
  it('classifies cursor expiry only with list scope and exact machine code', () => {
    for (const body of [null, [], {}, { code: 'other' }]) expect(classifyFailure(new HttpErrorResponse({ status: 409, error: body }), now, false).kind).toBe('client-error');
    expect(classifyFailure(new HttpErrorResponse({ status: 409, error: { code: 'cursor_expired' } }), now, false).kind).toBe('cursor-expired');
  });
  it('classifies timeout, unexpected and throttle safely', () => {
    expect(classifyFailure(new TimeoutError(), now, false).kind).toBe('timeout');
    expect(classifyFailure(new Error('private stack'), now, false).kind).toBe('unexpected-error');
    expect(classifyFailure(new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '2' }) }), now, false)).toMatchObject({ kind: 'throttled', retryAt: now + 2000 });
    expect(classifyFailure(new HttpErrorResponse({ status: 429 }), now, false)).toMatchObject({ kind: 'throttled', retryAt: null });
  });
});
