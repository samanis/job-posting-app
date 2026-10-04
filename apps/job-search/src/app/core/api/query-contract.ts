export interface JobSummary {
  readonly id: string;
  readonly title: string;
  readonly department: string;
  readonly location: string;
  readonly salaryMin: number;
  readonly salaryMax: number;
  readonly closingDate: string;
  readonly createdAt: string;
}
export interface JobDetail extends JobSummary { readonly description: string; }
export interface JobPage { readonly items: readonly JobSummary[]; readonly nextCursor: string | null; }
export type JobSort = 'newest' | 'closing-soon';
export interface JobQuery {
  readonly q?: string;
  readonly department?: string;
  readonly location?: string;
  readonly cursor?: string;
  readonly limit?: number;
  readonly sort?: JobSort;
}
export type QueryFailure =
  | { readonly kind: 'not-ready'; readonly message: string }
  | { readonly kind: 'protocol-error'; readonly message: string }
  | { readonly kind: 'invalid-query'; readonly message: string }
  | { readonly kind: 'unavailable'; readonly message: string }
  | { readonly kind: 'cursor-expired'; readonly message: string }
  | { readonly kind: 'throttled'; readonly retryAt: number | null; readonly message: string }
  | { readonly kind: 'client-error'; readonly status: number; readonly message: string }
  | { readonly kind: 'server-error' | 'network-error' | 'timeout' | 'unexpected-error'; readonly message: string };
export type QueryOutcome<T> = { readonly kind: 'ready'; readonly data: T } | QueryFailure;
