import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { DatePipe, DecimalPipe } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, output } from '@angular/core';
import { SavedJob } from '../../../core/api/job-posting-contract';

@Component({
  selector: 'app-saved-job-confirmation',
  imports: [DatePipe, DecimalPipe, MatButtonModule, MatCardModule],
  templateUrl: './saved-job-confirmation.html',
  styleUrl: './saved-job-confirmation.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SavedJobConfirmation {
  readonly record = input.required<SavedJob>();
  readonly postAnother = output<void>();
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly closingDateLabel = computed(() => new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: 'long', day: 'numeric' }).format(new Date(this.record().closingDate + 'T12:00:00')));

  constructor() {
    afterNextRender(() => this.element.nativeElement.querySelector<HTMLHeadingElement>('h2')!.focus());
  }
}
