import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { JobField, PostingOutcome, SavedJob } from './job-posting-contract';

const fields: readonly JobField[] = ['title', 'department', 'location', 'description', 'salaryMin', 'salaryMax', 'closingDate'];
const unknownMessage = 'The save could not be confirmed. Keep this submission for a safe retry.';

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function validDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(value + 'T00:00:00Z');
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}

function isSalary(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}

export function isSavedJob(value: unknown): value is SavedJob {
  if (!isObject(value)) return false;
  for (const field of ['id', 'title', 'department', 'location', 'description']) {
    const text = value[field];
    if (typeof text !== 'string' || text.trim().length === 0) return false;
  }
  if (!isSalary(value['salaryMin']) || !isSalary(value['salaryMax']) || value['salaryMin'] >= value['salaryMax']) return false;
  if (!validDate(value['closingDate'])) return false;
  const createdAt = value['createdAt'];
  return typeof createdAt === 'string'
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$/.test(createdAt)
    && validDate(createdAt.slice(0, 10)) && Number.isFinite(Date.parse(createdAt));
}

/** Absolute deadline in epoch milliseconds; null means no usable server delay. */
export function parseRetryAfter(value: string | null, now: number): number | null {
  if (value === null) return null;
  const trimmed = value.trim();
  if (/^\d+$/.test(trimmed)) {
    const deadline = now + Number(trimmed) * 1000;
    return Number.isSafeInteger(deadline) ? deadline : null;
  }
  // Only IMF-fixdate is accepted; Date.parse alone also accepts invalid numeric delays.
  if (!/^(Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4} \d{2}:\d{2}:\d{2} GMT$/.test(trimmed)) return null;
  const deadline = Date.parse(trimmed);
  if (!Number.isFinite(deadline) || new Date(deadline).toUTCString() !== trimmed) return null;
  return Math.max(now, deadline);
}

function validation(body: unknown, status: number): PostingOutcome {
  const fieldErrors: Partial<Record<JobField, string[]>> = {};
  const formErrors: string[] = [];
  if (isObject(body) && isObject(body['errors'])) {
    for (const [name, messages] of Object.entries(body['errors'])) {
      if (!Array.isArray(messages)) continue;
      const text = messages.filter((message: unknown): message is string => typeof message === 'string' && message.trim().length > 0);
      if (text.length === 0) continue;
      const field = fields.find(candidate => candidate.toLowerCase() === name.toLowerCase());
      if (field === undefined) formErrors.push(...text);
      else fieldErrors[field] = [...(fieldErrors[field] ?? []), ...text];
    }
  }
  if (Object.keys(fieldErrors).length === 0 && formErrors.length === 0) {
    return { kind: 'rejected', status, message: 'The request was rejected. Check the job details and try again.' };
  }
  return { kind: 'validation', status, fieldErrors, formErrors };
}

export function classifyResponse(response: HttpResponse<unknown>): PostingOutcome {
  if (response.status === 202) return { kind: 'pending', reason: 'accepted', message: 'The submission was accepted, but saving is not yet confirmed.' };
  if (isSavedJob(response.body)) return { kind: 'saved', record: response.body, status: response.status };
  return { kind: 'unknown', reason: 'invalid-success', message: unknownMessage };
}

export function classifyFailure(error: unknown, now: number): PostingOutcome {
  if (error instanceof TimeoutError) return { kind: 'unknown', reason: 'timeout', message: unknownMessage };
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unknown', reason: 'unexpected', message: unknownMessage };
  const { status } = error;
  if (status === 0) return { kind: 'unknown', reason: 'network', message: unknownMessage };
  // HttpClient JSON parsing can fail even with a successful HTTP status.
  if (status >= 200 && status < 300) return { kind: 'unknown', reason: 'invalid-success', message: unknownMessage };
  if (status === 400 || status === 422) return validation(error.error, status);
  if (status === 409) {
    const code = isObject(error.error) ? error.error['code'] : undefined;
    if (code === 'idempotency_in_progress') return { kind: 'pending', reason: 'in-progress', message: 'This submission is still being processed. Retry the same submission later.' };
    return { kind: 'conflict', reason: code === 'idempotency_key_conflict' ? 'key-mismatch' : 'general', message: 'This submission conflicts with an existing request. Do not create a replacement until the conflict is resolved.' };
  }
  if (status === 429) return throttled(error.headers, now);
  if (status >= 400 && status < 500) return { kind: 'rejected', status, message: 'The request could not be accepted. Check the details before trying again.' };
  if (status >= 500 && status < 600) return { kind: 'unknown', reason: 'server', message: unknownMessage };
  return { kind: 'unknown', reason: 'unexpected', message: unknownMessage };
}

function throttled(headers: HttpHeaders, now: number): PostingOutcome {
  return { kind: 'throttled', retryAt: parseRetryAfter(headers.get('Retry-After'), now), message: 'Too many requests. Wait before retrying this same submission.' };
}
