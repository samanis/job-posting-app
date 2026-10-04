export interface CreateJobRequest {
  readonly title: string;
  readonly department: string;
  readonly location: string;
  readonly description: string;
  readonly salaryMin: number;
  readonly salaryMax: number;
  readonly closingDate: string;
}

export interface SavedJob extends CreateJobRequest {
  readonly id: string;
  readonly createdAt: string;
}

export type JobField = keyof CreateJobRequest;
export interface ValidationProblemDetails {
  readonly errors: Readonly<Record<string, readonly string[]>>;
}

export type PostingOutcome =
  | { readonly kind: 'saved'; readonly record: SavedJob; readonly status: number }
  | { readonly kind: 'validation'; readonly fieldErrors: Partial<Record<JobField, string[]>>; readonly formErrors: string[]; readonly status: number }
  | { readonly kind: 'pending'; readonly reason: 'accepted' | 'in-progress'; readonly message: string }
  | { readonly kind: 'unknown'; readonly reason: 'invalid-success' | 'network' | 'timeout' | 'server' | 'unexpected'; readonly message: string }
  | { readonly kind: 'conflict'; readonly reason: 'key-mismatch' | 'general'; readonly message: string }
  | { readonly kind: 'throttled'; readonly retryAt: number | null; readonly message: string }
  | { readonly kind: 'rejected'; readonly status: number; readonly message: string };
