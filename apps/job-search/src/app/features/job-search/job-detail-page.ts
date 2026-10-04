import { afterNextRender, ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, Injector, runInInjectionContext, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, RouterLink } from '@angular/router';
import { parseQuery, queryParams } from './search-query';
import { distinctUntilChanged, map, tap } from 'rxjs';
import { DetailQueryWorkflow } from './detail-query-workflow';
import { closingDateLabel } from './job-presentation';
@Component({ selector: 'app-job-detail-page', providers: [DetailQueryWorkflow], imports: [RouterLink, DatePipe, DecimalPipe], templateUrl: './job-detail-page.html', styleUrl: './job-detail-page.css', changeDetection: ChangeDetectionStrategy.OnPush })
export class JobDetailPage {
  readonly returnParams = signal<Params>({});
  readonly workflow = inject(DetailQueryWorkflow);
  readonly dateLabel = computed(() => { const data = this.workflow.data(); return data === undefined ? '' : closingDateLabel(data.closingDate); });
  constructor() {
    const route = inject(ActivatedRoute), element = inject<ElementRef<HTMLElement>>(ElementRef), injector = inject(Injector), title = inject(Title);
    route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => this.returnParams.set(queryParams(parseQuery(params).query)));
    this.workflow.connect(route.paramMap.pipe(map(params => params.get('id') ?? ''), distinctUntilChanged(), tap(() => {
      runInInjectionContext(injector, () => afterNextRender(() => element.nativeElement.querySelector<HTMLElement>('h1')!.focus()));
    })));
    effect(() => title.setTitle(this.workflow.data() === undefined ? 'Job details' : `${this.workflow.data()!.title} | Job board`));
  }
}
