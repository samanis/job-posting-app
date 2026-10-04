import { InjectionToken } from '@angular/core';
import { CreateJobRequest, JobField } from '../../../core/api/job-posting-contract';
import { JobDraft } from '../models/job-draft';

export const LOCAL_CLOCK = new InjectionToken<() => Date>('Local calendar clock', { providedIn: 'root', factory: () => () => new Date() });

export function localDate(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function salaryError(value: string): string | null {
  if (value.trim() === '') return 'Salary is required.';
  if (!/^\d+(?:\.\d{1,2})?$/.test(value.trim()) || !Number.isFinite(Number(value))) return 'Enter a nonnegative salary with at most two decimal places.';
  return null;
}

export function closingDateError(value: string, today: string): string | null {
  if (value === '') return 'Closing date is required.';
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return 'Enter a valid closing date.';
  const date = new Date(value + 'T00:00:00Z');
  if (!Number.isFinite(date.getTime()) || date.toISOString().slice(0, 10) !== value) return 'Enter a valid closing date.';
  return value > today ? null : 'Closing date must be later than today.';
}

export function fieldError(field: JobField, draft: JobDraft, today: string): string | null {
  if (field === 'closingDate') return closingDateError(draft[field], today);
  if (field === 'salaryMin' || field === 'salaryMax') {
    const error = salaryError(draft[field]);
    if (error !== null) return error;
    if (salaryError(draft.salaryMin) === null && salaryError(draft.salaryMax) === null && Number(draft.salaryMin) >= Number(draft.salaryMax)) return 'Minimum salary must be less than maximum salary.';
    return null;
  }
  return draft[field].trim() === '' ? 'This field is required.' : null;
}

/** Call only after the complete draft has passed validation. */
export function normalizeDraft(draft: JobDraft): CreateJobRequest {
  return { title: draft.title.trim(), department: draft.department.trim(), location: draft.location.trim(), description: draft.description.trim(), salaryMin: Number(draft.salaryMin), salaryMax: Number(draft.salaryMax), closingDate: draft.closingDate };
}

export function validationMessage(error: { readonly message?: string }): string {
  return error.message ?? "Enter a valid value.";
}
