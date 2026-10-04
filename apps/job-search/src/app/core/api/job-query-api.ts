import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { inject, Injectable, InjectionToken } from '@angular/core';
import { catchError, defer, map, Observable, of, timeout } from 'rxjs';
import { JobDetail, JobPage, JobQuery, QueryOutcome } from './query-contract';
import { classifyFailure, classifyResponse } from './query-response';
import { detailSnapshot, isJobDetail, isJobPage, pageSnapshot, validCursor, validText } from './query-validation';

export const QUERY_CLOCK = new InjectionToken<() => number>('QUERY_CLOCK', { providedIn: 'root', factory: () => Date.now });
export const QUERY_TIMEOUT_MS = new InjectionToken<number>('QUERY_TIMEOUT_MS', { providedIn: 'root', factory: () => 10000 });

@Injectable({ providedIn: 'root' })
export class JobQueryApi {
  private readonly http = inject(HttpClient);
  private readonly clock = inject(QUERY_CLOCK);
  private readonly timeoutMs = inject(QUERY_TIMEOUT_MS);

  list(query: JobQuery = {}): Observable<QueryOutcome<JobPage>> {
    return defer(() => {
      const limit = query.limit ?? 20;
      const sort = query.sort ?? 'newest';
      if (!Number.isInteger(limit) || limit < 1 || limit > 50 || !['newest', 'closing-soon'].includes(sort)) return this.invalid();
      let params = new HttpParams().set('limit', limit).set('sort', sort);
      for (const field of ['q', 'department', 'location'] as const) {
        const value = query[field];
        if (value === undefined) continue;
        if (typeof value !== 'string' || value.trim().length > (field === 'q' ? 200 : 100)) return this.invalid();
        if (value.trim()) params = params.set(field, value.trim());
      }
      if (query.cursor !== undefined) {
        if (!validCursor(query.cursor)) return this.invalid();
        params = params.set('cursor', query.cursor);
      }
      return this.read(this.http.get<unknown>('/api/jobs', { params, observe: 'response' }),
        (body): body is JobPage => isJobPage(body, limit), pageSnapshot, false);
    });
  }
  detail(id: string): Observable<QueryOutcome<JobDetail>> {
    return defer(() => {
      if (!validText(id, 1000) || /[\u0000-\u001f\u007f]/.test(id) || id === '.' || id === '..' || /[\ud800-\udfff]/u.test(id)) return this.invalid();
      return this.read(this.http.get<unknown>('/api/jobs/' + encodeURIComponent(id), { observe: 'response' }),
        (body): body is JobDetail => isJobDetail(body) && body.id === id, detailSnapshot, true);
    });
  }
  private invalid(): Observable<never | { readonly kind: 'invalid-query'; readonly message: string }> {
    return of({ kind: 'invalid-query', message: 'Check the search parameters before trying again.' });
  }
  private read<T>(request: Observable<HttpResponse<unknown>>, validate: (body: unknown) => body is T,
    snapshot: (data: T) => T, detail: boolean): Observable<QueryOutcome<T>> {
    const duration = Number.isFinite(this.timeoutMs) ? Math.min(60000, Math.max(1, this.timeoutMs)) : 10000;
    return request.pipe(timeout(duration), map(response => classifyResponse(response, validate, snapshot)),
      catchError((error: unknown) => of(classifyFailure(error, this.clock(), detail))));
  }
}
