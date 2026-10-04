import { ParamMap, Params } from '@angular/router';
import { JobSort } from '../../core/api/query-contract';
import { validCursor } from '../../core/api/query-validation';

export interface SearchQuery {
  readonly q: string;
  readonly department: string;
  readonly location: string;
  readonly sort: JobSort;
  readonly limit: number;
  readonly cursor: string | null;
}
export type SearchDraft = Omit<SearchQuery, 'cursor'>;
export const defaultQuery: SearchQuery = Object.freeze({ q: '', department: '', location: '', sort: 'newest', limit: 20, cursor: null });
export function parseQuery(params: ParamMap): { query: SearchQuery; errors: readonly string[] } {
  const errors: string[] = [];
  const text = (field: string, max: number): string => {
    const values = params.getAll(field);
    if (values.length > 1 || (values[0]?.trim().length ?? 0) > max) {
      errors.push(`Invalid ${field} parameter. Use at most ${max} characters and one value.`);
      return '';
    }
    return (values[0] ?? '').trim();
  };
  const q = text('q', 200), department = text('department', 100), location = text('location', 100);
  const sortValue = params.get('sort');
  let sort: JobSort = 'newest';
  if (sortValue !== null) {
    if (params.getAll('sort').length === 1 && (sortValue === 'newest' || sortValue === 'closing-soon')) sort = sortValue;
    else errors.push('Invalid sort parameter. Using newest first.');
  }
  const limitValue = params.get('limit');
  let limit = 20;
  if (limitValue !== null) {
    if (params.getAll('limit').length === 1 && /^\d+$/.test(limitValue) && Number(limitValue) >= 1 && Number(limitValue) <= 50) limit = Number(limitValue);
    else errors.push('Invalid limit parameter. Using 20 jobs per page.');
  }
  let cursor = params.get('cursor');
  if (cursor !== null && (params.getAll('cursor').length !== 1 || !validCursor(cursor))) {
    cursor = null;
    errors.push('Invalid results cursor. Using the first page.');
  }
  // A cursor belongs to the exact criteria, so any repaired criteria invalidates it.
  if (errors.length > 0) cursor = null;
  return { query: Object.freeze({ q, department, location, sort, limit, cursor }), errors: Object.freeze(errors) };
}
export function queryParams(query: SearchQuery): Params {
  const params: Params = {};
  for (const field of ['q', 'department', 'location'] as const) if (query[field]) params[field] = query[field];
  if (query.sort !== 'newest') params['sort'] = query.sort;
  if (query.limit !== 20) params['limit'] = String(query.limit);
  if (query.cursor !== null) params['cursor'] = query.cursor;
  return params;
}
export function queryKey(query: SearchQuery): string { return JSON.stringify(queryParams(query)); }
export function criteriaKey(query: SearchQuery): string { return queryKey({ ...query, cursor: null }); }
