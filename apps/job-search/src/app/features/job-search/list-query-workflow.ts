import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map, merge, Observable, Subject, switchMap, tap } from 'rxjs';
import { JobPage, QueryFailure } from '../../core/api/query-contract';
import { QUERY_CLOCK } from '../../core/api/job-query-api';
import { QueryReads } from './query-reads';
import { queryKey, SearchQuery } from './search-query';

export type ListReadState =
  | { readonly kind: 'initial-loading'; readonly key: string }
  | { readonly kind: 'ready' | 'empty'; readonly key: string; readonly data: JobPage }
  | { readonly kind: 'refreshing'; readonly key: string; readonly data: JobPage }
  | { readonly kind: 'stale-error'; readonly key: string; readonly data: JobPage; readonly failure: QueryFailure }
  | { readonly kind: 'error' | 'not-ready' | 'throttled'; readonly key: string; readonly failure: QueryFailure };

@Injectable()
export class ListQueryWorkflow {
  private readonly reads = inject(QueryReads);
  private readonly clock = inject(QUERY_CLOCK);
  private readonly destroy = inject(DestroyRef);
  private readonly commands = new Subject<{ query: SearchQuery; refresh: boolean }>();
  private current: SearchQuery | undefined;
  private connected = false;
  readonly state = signal<ListReadState>({ kind: 'initial-loading', key: '' });
  readonly busy = computed(() => this.state().kind === 'initial-loading' || this.state().kind === 'refreshing');
  readonly data = computed(() => { const state = this.state(); return 'data' in state ? state.data : undefined; });
  connect(queries: Observable<SearchQuery>): void {
    if (this.connected) return;
    this.connected = true;
    merge(queries.pipe(distinctUntilChanged((a, b) => queryKey(a) === queryKey(b)),
      tap(query => this.current = query),
      // Keep route state as the sole criteria source; commands only refresh that exact source.
      map(query => ({ query, refresh: false }))), this.commands).pipe(switchMap(({ query, refresh }) => {
        const key = queryKey(query);
        const cached = this.reads.cachedList(query);
        if (cached !== undefined && (refresh || !cached.fresh)) this.state.set({ kind: 'refreshing', key, data: cached.data });
        else this.state.set({ kind: 'initial-loading', key });
        return this.reads.list(query, refresh).pipe(tap(outcome => {
          if (outcome.kind === 'ready') this.state.set({ kind: outcome.data.items.length === 0 ? 'empty' : 'ready', key, data: outcome.data });
          else if (cached !== undefined) this.state.set({ kind: 'stale-error', key, data: cached.data, failure: outcome });
          else this.state.set({ kind: outcome.kind === 'not-ready' ? 'not-ready' : outcome.kind === 'throttled' ? 'throttled' : 'error', key, failure: outcome });
        }));
      }), takeUntilDestroyed(this.destroy)).subscribe();
  }
  refresh(): void {
    if (this.current === undefined || this.busy()) return;
    const state = this.state();
    if ('failure' in state && state.failure.kind === 'throttled' && state.failure.retryAt !== null && this.clock() < state.failure.retryAt) return;
    this.commands.next({ query: this.current, refresh: true });
  }
}
