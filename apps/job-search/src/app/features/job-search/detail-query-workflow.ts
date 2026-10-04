import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map, merge, Observable, Subject, switchMap, tap } from 'rxjs';
import { JobDetail, QueryFailure } from '../../core/api/query-contract';
import { QUERY_CLOCK } from '../../core/api/job-query-api';
import { QueryReads } from './query-reads';

export type DetailState =
  | { readonly kind: 'loading'; readonly id: string }
  | { readonly kind: 'ready' | 'refreshing'; readonly id: string; readonly data: JobDetail }
  | { readonly kind: 'stale-error'; readonly id: string; readonly data: JobDetail; readonly failure: QueryFailure }
  | { readonly kind: 'error' | 'unavailable'; readonly id: string; readonly failure: QueryFailure };
@Injectable()
export class DetailQueryWorkflow {
  private readonly reads = inject(QueryReads);
  private readonly clock = inject(QUERY_CLOCK);
  private readonly destroy = inject(DestroyRef);
  private readonly commands = new Subject<{ id: string; refresh: boolean }>();
  private current: string | undefined;
  private connected = false;
  readonly state = signal<DetailState>({ kind: 'loading', id: '' });
  readonly busy = computed(() => this.state().kind === 'loading' || this.state().kind === 'refreshing');
  readonly data = computed(() => { const state = this.state(); return 'data' in state ? state.data : undefined; });
  connect(ids: Observable<string>): void {
    if (this.connected) return;
    this.connected = true;
    merge(ids.pipe(distinctUntilChanged(), tap(id => this.current = id), map(id => ({ id, refresh: false }))), this.commands)
      .pipe(switchMap(({ id, refresh }) => {
        const cached = this.reads.cachedDetail(id);
        if (cached !== undefined && (refresh || !cached.fresh)) this.state.set({ kind: 'refreshing', id, data: cached.data });
        else this.state.set({ kind: 'loading', id });
        return this.reads.detail(id, refresh).pipe(tap(outcome => {
          if (outcome.kind === 'ready') this.state.set({ kind: 'ready', id, data: outcome.data });
          else if (outcome.kind === 'unavailable') this.state.set({ kind: 'unavailable', id, failure: outcome });
          else if (cached !== undefined) this.state.set({ kind: 'stale-error', id, data: cached.data, failure: outcome });
          else this.state.set({ kind: 'error', id, failure: outcome });
        }));
      }), takeUntilDestroyed(this.destroy)).subscribe();
  }
  refresh(): void {
    if (this.current === undefined || this.busy()) return;
    const state = this.state();
    if ('failure' in state && state.failure.kind === 'throttled' && state.failure.retryAt !== null && this.clock() < state.failure.retryAt) return;
    this.commands.next({ id: this.current, refresh: true });
  }
}
