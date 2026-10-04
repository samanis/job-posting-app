import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { map } from 'rxjs';
import { ActivatedRoute, convertToParamMap, Router, RouterLink } from '@angular/router';
import { CursorHistory } from './cursor-history';
import { defaultQuery, parseQuery, queryKey, queryParams, SearchDraft } from './search-query';
import { ListQueryWorkflow } from './list-query-workflow';
import { closingDateLabel } from './job-presentation';
import { ListReturnFocus } from './list-return-focus';

@Component({ selector: 'app-job-list-page', providers: [ListQueryWorkflow], imports: [RouterLink, DecimalPipe], templateUrl: './job-list-page.html', styleUrl: './job-list-page.css', changeDetection: ChangeDetectionStrategy.OnPush })
export class JobListPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly history = inject(CursorHistory);
  readonly workflow = inject(ListQueryWorkflow);
  readonly committed = signal(defaultQuery);
  readonly draft = signal<SearchDraft>(defaultQuery);
  readonly errors = signal<readonly string[]>([]);
  readonly nextCursor = signal<string | null>(null);
  private readonly returnFocus = inject(ListReturnFocus);
  readonly cards = computed(() => this.workflow.data()?.items.map(job => ({ ...job, dateLabel: closingDateLabel(job.closingDate), url: this.router.parseUrl(this.detailUrl(job.id)) })) ?? []);
  readonly announcement = computed(() => {
    const state = this.workflow.state();
    if (state.kind === 'empty') return 'No jobs match these filters.';
    if (state.kind !== 'ready') return '';
    const count = state.data.items.length;
    return `${count} ${count === 1 ? 'job' : 'jobs'} on this results page.`;
  });
  private timer: ReturnType<typeof setTimeout> | undefined;
  private readonly composing = new Set<string>();
  constructor() {
    const queries = this.route.queryParamMap.pipe(map(params => {
      this.cancelTimer();
      this.composing.clear();
      const parsed = parseQuery(params);
      this.errors.set(parsed.errors);
      if (queryKey(parsed.query) !== queryKey(this.committed())) {
        this.committed.set(parsed.query);
        this.nextCursor.set(null);
      }
      this.draft.set(parsed.query);
      this.history.visit(parsed.query);
      return parsed.query;
    }));
    this.workflow.connect(queries);
    effect(() => this.nextCursor.set(this.workflow.data()?.nextCursor ?? null));
    const element = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => {
      const id = this.returnFocus.take(queryKey(this.committed()));
      if (id === undefined) return;
      const link = Array.from(element.nativeElement.querySelectorAll<HTMLAnchorElement>('[data-job-id]')).find(link => link.dataset['jobId'] === id);
      (link ?? element.nativeElement.querySelector<HTMLElement>('h1')!).focus();
    });
    inject(DestroyRef).onDestroy(() => this.cancelTimer());
  }
  edit(field: 'q' | 'department' | 'location', value: string): void {
    this.draft.update(draft => ({ ...draft, [field]: value }));
    this.cancelTimer();
    if (this.composing.size === 0) this.timer = setTimeout(() => this.commit(true), 300);
  }
  compositionStart(field: string): void { this.composing.add(field); this.cancelTimer(); }
  compositionEnd(field: 'q' | 'department' | 'location', value: string): void {
    this.composing.delete(field); this.edit(field, value);
  }
  submit(event: Event): void { event.preventDefault(); this.commit(false); }
  select(field: 'sort' | 'limit', value: string): void {
    this.draft.update(draft => ({ ...draft, [field]: field === 'limit' ? Number(value) : value }));
    this.commit(false);
  }
  clear(): void {
    this.cancelTimer(); this.composing.clear(); this.draft.set(defaultQuery); this.errors.set([]);
    if (this.router.url !== '/jobs') {
      void this.router.navigate(['/jobs'], { queryParams: {} });
    }
  }
  private cancelTimer(): void { clearTimeout(this.timer); this.timer = undefined; }
  private commit(replaceUrl: boolean): void {
    this.cancelTimer();
    if (this.composing.size > 0) return;
    const draft = this.draft();
    const parsed = parseQuery(convertToParamMap({ q: draft.q, department: draft.department, location: draft.location, sort: draft.sort, limit: String(draft.limit) }));
    this.errors.set(parsed.errors);
    if (parsed.errors.length > 0) return;
    // Submitting unchanged criteria retains its page; changed criteria starts at the first page.
    const current = this.committed();
    const sameCriteria = queryKey({ ...current, cursor: null }) === queryKey(parsed.query);
    if (sameCriteria) return;
    void this.router.navigate(['/jobs'], { queryParams: queryParams(parsed.query), replaceUrl });
  }
  next(): void {
    const cursor = this.nextCursor();
    if (this.history.prepareNext(cursor)) this.navigateCursor(cursor);
  }
  previous(): void {
    const cursor = this.history.previous();
    if (cursor !== undefined) this.navigateCursor(cursor);
  }
  first(): void { if (this.committed().cursor !== null) this.navigateCursor(null); }
  private navigateCursor(cursor: string | null): void {
    this.cancelTimer();
    void this.router.navigate(['/jobs'], { queryParams: queryParams({ ...this.committed(), cursor }) });
  }
  detailUrl(id: string): string {
    return this.router.serializeUrl(this.router.createUrlTree(['/jobs', id], { queryParams: queryParams(this.committed()) }));
  }
  rememberReturn(id: string): void { this.returnFocus.remember(queryKey(this.committed()), id); }
}
