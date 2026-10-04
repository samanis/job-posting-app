import { inject, Injectable, InjectionToken } from '@angular/core';
import { defer, finalize, Observable, of, ReplaySubject, share, tap } from 'rxjs';
import { JobDetail, JobPage, QueryOutcome } from '../../core/api/query-contract';
import { JobQueryApi, QUERY_CLOCK } from '../../core/api/job-query-api';
import { defaultQuery, SearchQuery } from './search-query';

export const QUERY_CACHE_TTL_MS = new InjectionToken<number>('QUERY_CACHE_TTL_MS', { providedIn: 'root', factory: () => 30000 });
export const QUERY_CACHE_ENTRIES = new InjectionToken<number>('QUERY_CACHE_ENTRIES', { providedIn: 'root', factory: () => 50 });
export const QUERY_CACHE_SCOPE = new InjectionToken<string>('QUERY_CACHE_SCOPE', { providedIn: 'root', factory: () => '/api/jobs' });
type Data = JobPage | JobDetail;
interface Entry { readonly data: Data; readonly expires: number; }
export interface CachedRead<T> { readonly data: T; readonly fresh: boolean; }

/** Root memory only. Both resource kinds share one LRU and one in-flight registry. */
@Injectable({ providedIn: 'root' })
export class QueryReads {
  private readonly api = inject(JobQueryApi);
  private readonly clock = inject(QUERY_CLOCK);
  private readonly scope = inject(QUERY_CACHE_SCOPE);
  private readonly ttl = this.bound(inject(QUERY_CACHE_TTL_MS), 30000, 0, 300000);
  private readonly capacity = Math.floor(this.bound(inject(QUERY_CACHE_ENTRIES), 50, 1, 50));
  private readonly cache = new Map<string, Entry>();
  private readonly flights = new Map<string, Observable<QueryOutcome<Data>>>();
  private bound(value: number, fallback: number, min: number, max: number): number {
    return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
  }
  private listKey(query: SearchQuery): string {
    return JSON.stringify([this.scope, 'list', query.q.trim(), query.department.trim(), query.location.trim(), query.sort, query.limit, query.cursor]);
  }
  private detailKey(id: string): string { return JSON.stringify([this.scope, 'detail', id]); }
  cachedList(query: SearchQuery): CachedRead<JobPage> | undefined { return this.cached<JobPage>(this.listKey(query)); }
  cachedDetail(id: string): CachedRead<JobDetail> | undefined { return this.cached<JobDetail>(this.detailKey(id)); }
  private cached<T extends Data>(key: string): CachedRead<T> | undefined {
    const entry = this.cache.get(key);
    if (entry === undefined) return undefined;
    this.cache.delete(key); this.cache.set(key, entry);
    return { data: entry.data as T, fresh: this.clock() < entry.expires };
  }
  list(query: SearchQuery = defaultQuery, refresh = false): Observable<QueryOutcome<JobPage>> {
    const snapshot = Object.freeze({ ...query, q: query.q.trim(), department: query.department.trim(), location: query.location.trim() });
    return this.read(this.listKey(snapshot), () => this.api.list({ ...snapshot, cursor: snapshot.cursor ?? undefined }), refresh);
  }
  detail(id: string, refresh = false): Observable<QueryOutcome<JobDetail>> {
    return this.read(this.detailKey(id), () => this.api.detail(id), refresh, id);
  }
  private read<T extends Data>(key: string, load: () => Observable<QueryOutcome<T>>, refresh: boolean, id?: string): Observable<QueryOutcome<T>> {
    return defer(() => {
      const running = this.flights.get(key);
      // Explicit refresh joins an existing request, bypassing stored data but never duplicates work.
      if (running !== undefined) return running as Observable<QueryOutcome<T>>;
      const cached = this.cached<T>(key);
      if (!refresh && cached?.fresh) return of({ kind: 'ready' as const, data: cached.data });
      const release = () => { if (this.flights.get(key) === shared) this.flights.delete(key); };
      const shared = defer(load).pipe(tap(outcome => {
        if (outcome.kind === 'ready') {
          this.cache.delete(key);
          this.cache.set(key, { data: outcome.data, expires: this.clock() + this.ttl });
          if (this.cache.size > this.capacity) this.cache.delete(this.cache.keys().next().value!);
        } else if (outcome.kind === 'unavailable' && id !== undefined) this.invalidateJob(id);
        release();
      }), finalize(release), share({ connector: () => new ReplaySubject<QueryOutcome<T>>(1), resetOnComplete: true, resetOnError: true, resetOnRefCountZero: true }));
      this.flights.set(key, shared as Observable<QueryOutcome<Data>>);
      return shared;
    });
  }
  private invalidateJob(id: string): void {
    this.cache.delete(this.detailKey(id));
    for (const [key, entry] of this.cache) {
      if ('items' in entry.data && entry.data.items.some(job => job.id === id)) this.cache.delete(key);
    }
  }
}
