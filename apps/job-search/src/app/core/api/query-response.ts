import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { QueryFailure, QueryOutcome } from './query-contract';
import { isObject } from './query-validation';

const maxDelay = 24 * 60 * 60 * 1000;
export function parseRetryAfter(value: string | null, now: number): number | null {
  if (value === null) return null;
  const text = value.trim();
  if (/^\d+$/.test(text)) {
    const seconds = Number(text);
    if (!Number.isFinite(seconds)) return null;
    return now + Math.min(seconds * 1000, maxDelay);
  }
  if (!/^(Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4} \d{2}:\d{2}:\d{2} GMT$/.test(text)) return null;
  const time = Date.parse(text);
  if (!Number.isFinite(time) || new Date(time).toUTCString() !== text) return null;
  return now + Math.min(Math.max(0, time - now), maxDelay);
}
export function protocolError(): QueryFailure {
  return { kind: 'protocol-error', message: 'The job service returned an unsupported response. Try again later.' };
}
export function classifyResponse<T>(response: HttpResponse<unknown>, validate: (body: unknown) => body is T,
  snapshot: (data: T) => T): QueryOutcome<T> {
  if (response.status === 202) return { kind: 'not-ready', message: 'Job data is not ready yet. Try again later.' };
  if (response.status !== 200 || !validate(response.body)) return protocolError();
  return { kind: 'ready', data: snapshot(response.body) };
}
export function classifyFailure(error: unknown, now: number, detail: boolean): QueryFailure {
  if (error instanceof TimeoutError) return { kind: 'timeout', message: 'The job service took too long to respond. Try again.' };
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unexpected-error', message: 'Jobs could not be loaded. Try again.' };
  const status = error.status;
  if (status === 0) return { kind: 'network-error', message: 'The job service could not be reached. Check your connection and try again.' };
  if (status >= 200 && status < 300) return protocolError();
  if (status === 400 || status === 422) return { kind: 'invalid-query', message: 'The search criteria were rejected. Check your filters and try again.' };
  if (detail && (status === 404 || status === 410)) return { kind: 'unavailable', message: 'This job is no longer available.' };
  if (!detail && status === 409 && isObject(error.error) && error.error['code'] === 'cursor_expired') {
    return { kind: 'cursor-expired', message: 'This results page has expired. Return to the first page.' };
  }
  if (status === 429) return { kind: 'throttled', retryAt: parseRetryAfter(error.headers.get('Retry-After'), now), message: 'Too many searches. Wait before trying again.' };
  if (status >= 400 && status < 500) return { kind: 'client-error', status, message: 'The job request could not be accepted.' };
  if (status >= 500 && status < 600) return { kind: 'server-error', message: 'The job service is temporarily unavailable. Try again later.' };
  return { kind: 'unexpected-error', message: 'Jobs could not be loaded. Try again.' };
}
