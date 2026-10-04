import { JobField } from '../../../core/api/job-posting-contract';

export type JobDraft = Record<JobField, string>;
export const JOB_FIELDS: readonly JobField[] = ['title', 'department', 'location', 'description', 'salaryMin', 'salaryMax', 'closingDate'];
export const EMPTY_DRAFT: JobDraft = { title: '', department: '', location: '', description: '', salaryMin: '', salaryMax: '', closingDate: '' };
