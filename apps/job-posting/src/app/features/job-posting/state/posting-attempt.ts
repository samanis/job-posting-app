import { CreateJobRequest, SavedJob } from '../../../core/api/job-posting-contract';
import { isSavedJob } from '../../../core/api/posting-response';
import { JOB_FIELDS } from '../models/job-draft';
import { closingDateError, salaryError } from '../validators/job-validation';

export type AttemptStatus = 'in-flight' | 'unknown' | 'pending' | 'throttled' | 'conflict' | 'rejected' | 'saved';
export interface PostingAttempt {
  readonly version: 1;
  readonly key: string;
  readonly payload: CreateJobRequest;
  readonly status: AttemptStatus;
  readonly retryAt: number | null;
  readonly savedRecord: SavedJob | null;
}

export function payloadFingerprint(payload: CreateJobRequest): string {
  return JSON.stringify(JOB_FIELDS.map(field => payload[field]));
}

export function isPayload(value: unknown): value is CreateJobRequest {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const data = value as Record<string, unknown>;
  for (const field of ['title', 'department', 'location', 'description']) {
    const text = data[field];
    if (typeof text !== 'string' || text.trim() === '' || text !== text.trim()) return false;
  }
  for (const field of ['salaryMin', 'salaryMax']) {
    const salary = data[field];
    if (typeof salary !== 'number' || salaryError(String(salary)) !== null) return false;
  }
  return (data['salaryMin'] as number) < (data['salaryMax'] as number)
    && typeof data['closingDate'] === 'string' && closingDateError(data['closingDate'], '0000-00-00') === null;
}

export function freezeAttempt(attempt: PostingAttempt): PostingAttempt {
  // Copy only contract fields; ignore extra source properties and property order.
  const p = attempt.payload;
  const payload = Object.freeze({ title: p.title, department: p.department, location: p.location, description: p.description, salaryMin: p.salaryMin, salaryMax: p.salaryMax, closingDate: p.closingDate });
  const savedRecord = attempt.savedRecord === null ? null : Object.freeze({ ...attempt.savedRecord });
  return Object.freeze({ ...attempt, payload, savedRecord });
}

export function decodeAttempt(raw: string): PostingAttempt {
  const data: unknown = JSON.parse(raw);
  if (typeof data !== 'object' || data === null || Array.isArray(data)) throw new Error('Invalid recovery record');
  const value = data as Record<string, unknown>;
  if (value['version'] !== 1 || typeof value['key'] !== 'string' || !/^[-a-zA-Z0-9]{1,128}$/.test(value['key']) || !isPayload(value['payload'])) throw new Error('Invalid recovery identity');
  const statuses: readonly unknown[] = ['in-flight', 'unknown', 'pending', 'throttled', 'conflict', 'rejected', 'saved'];
  if (!statuses.includes(value['status'])) throw new Error('Invalid recovery status');
  const retryAt = value['retryAt'];
  if (retryAt !== null && (typeof retryAt !== 'number' || !Number.isSafeInteger(retryAt) || retryAt < 0)) throw new Error('Invalid retry deadline');
  if (value['status'] === 'saved' ? !isSavedJob(value['savedRecord']) : value['savedRecord'] !== null) throw new Error('Invalid saved record');
  return freezeAttempt({ version: 1, key: value['key'], payload: value['payload'], status: value['status'] as AttemptStatus, retryAt: retryAt as number | null, savedRecord: value['savedRecord'] as SavedJob | null });
}
