import { JobDetail, JobPage, JobSummary } from './query-contract';

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
export function validDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false;
  const date = new Date(value + 'T00:00:00Z');
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}
function validTimestamp(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,3})?(?:Z|[+-](?:[01]\d|2[0-3]):[0-5]\d)$/.test(value)) return false;
  return validDate(value.slice(0, 10)) && Number.isFinite(Date.parse(value));
}
export function validText(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= max;
}
export function validCursor(value: unknown): value is string {
  return validText(value, 2048) && !/[\u0000-\u0020\u007f]/.test(value);
}
function salary(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= Number.MAX_SAFE_INTEGER / 100
    && Math.abs(value * 100 - Math.round(value * 100)) < 1e-6;
}
export function isJobSummary(value: unknown): value is JobSummary {
  if (!isObject(value)) return false;
  for (const field of ['id', 'title', 'department', 'location']) {
    if (!validText(value[field], 1000)) return false;
  }
  return salary(value['salaryMin']) && salary(value['salaryMax']) && value['salaryMin'] < value['salaryMax']
    && validDate(value['closingDate']) && validTimestamp(value['createdAt']);
}
export function isJobDetail(value: unknown): value is JobDetail {
  return isJobSummary(value) && validText((value as unknown as Record<string, unknown>)['description'], 100000);
}
export function isJobPage(value: unknown, limit: number): value is JobPage {
  if (!isObject(value) || !Array.isArray(value['items']) || value['items'].length > limit) return false;
  const items: unknown[] = value['items'];
  if (!items.every(isJobSummary)) return false;
  if (new Set(items.map(item => item.id)).size !== items.length) return false;
  return value['nextCursor'] === null || (items.length > 0 && validCursor(value['nextCursor']));
}

/** Copies only contracted fields; freezes data independently of the response body. */
export function summarySnapshot(job: JobSummary): JobSummary {
  return Object.freeze({ id: job.id, title: job.title, department: job.department, location: job.location,
    salaryMin: job.salaryMin, salaryMax: job.salaryMax, closingDate: job.closingDate, createdAt: job.createdAt });
}
export function detailSnapshot(job: JobDetail): JobDetail {
  return Object.freeze({ ...summarySnapshot(job), description: job.description });
}
export function pageSnapshot(page: JobPage): JobPage {
  return Object.freeze({ items: Object.freeze(page.items.map(summarySnapshot)), nextCursor: page.nextCursor });
}
