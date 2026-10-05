import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { afterNextRender, ChangeDetectionStrategy, Component, Injector, computed, effect, inject, output, signal } from '@angular/core';
import { form, FormField, metadata, REQUIRED, readonly as readonlyForm, validate } from '@angular/forms/signals';
import { CreateJobRequest, JobField } from '../../../core/api/job-posting-contract';
import { SavedJobConfirmation } from './saved-job-confirmation';
import { PostingWorkflow } from '../state/posting-workflow';
import { EMPTY_DRAFT, JOB_FIELDS, JobDraft } from '../models/job-draft';
import { fieldError, LOCAL_CLOCK, localDate, normalizeDraft, validationMessage } from '../validators/job-validation';

@Component({
  selector: 'app-new-job-page',
  imports: [FormField, SavedJobConfirmation, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  providers: [PostingWorkflow],
  templateUrl: './new-job-page.html',
  styleUrl: './new-job-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewJobPage {
  readonly workflow = inject(PostingWorkflow);
  readonly recoveryConfirmed = signal(false);
  private readonly injector = inject(Injector);
  private readonly clock = inject(LOCAL_CLOCK);
  readonly draft = signal<JobDraft>({ ...EMPTY_DRAFT });
  readonly today = signal(localDate(this.clock()));
  readonly attempted = signal(false);
  readonly validPayload = output<CreateJobRequest>();
  readonly fields = JOB_FIELDS;
  readonly errorMatchers = Object.fromEntries(JOB_FIELDS.map(field => [field, { isErrorState: () => this.messages(field).length > 0 }])) as Record<JobField, { isErrorState: () => boolean }>;
  readonly labels: Record<JobField, string> = { title: 'Job title', department: 'Department', location: 'Location', description: 'Description', salaryMin: 'Salary minimum', salaryMax: 'Salary maximum', closingDate: 'Closing date' };
  private readonly serverFields = signal<Partial<Record<JobField, { value: string; messages: readonly string[] }>>>({});
  readonly serverForm = signal<readonly string[]>([]);
  readonly jobForm = form(this.draft, path => {
    readonlyForm(path, () => this.workflow.editingLocked());
    for (const field of JOB_FIELDS) {
      metadata(path[field], REQUIRED, () => true);
      validate(path[field], () => {
        const message = fieldError(field, this.draft(), this.today());
        return message === null ? null : { kind: 'job-validation', message };
      });
    }
  });
  readonly summary = computed(() => JOB_FIELDS.flatMap(field => this.messages(field).map(message => ({ field, message }))));

  constructor() {
    const payload = this.workflow.store.attempt()?.payload;
    if (payload !== undefined) this.draft.set({ ...payload, salaryMin: String(payload.salaryMin), salaryMax: String(payload.salaryMax) });
    effect(() => {
      const outcome = this.workflow.outcome();
      if (outcome?.kind === 'validation') this.setServerErrors(outcome.fieldErrors, outcome.formErrors);
    });
    effect(() => {
      const draft = this.draft();
      const entries = this.serverFields();
      const next = { ...entries };
      let changed = false;
      for (const field of JOB_FIELDS) {
        const entry = entries[field];
        if (entry !== undefined && entry.value !== draft[field]) { delete next[field]; changed = true; }
      }
      if (changed) this.serverFields.set(next);
    });
  }

  messages(field: JobField): readonly string[] {
    const state = this.jobForm[field]();
    const local = this.attempted() || state.touched() ? state.errors().map(validationMessage) : [];
    const entry = this.serverFields()[field];
    const external = entry !== undefined && entry.value === this.draft()[field] ? entry.messages : [];
    return [...local, ...external];
  }

  setServerErrors(fields: Partial<Record<JobField, readonly string[]>>, formMessages: readonly string[] = []): void {
    const entries: Partial<Record<JobField, { value: string; messages: readonly string[] }>> = {};
    for (const field of JOB_FIELDS) {
      const messages = fields[field];
      if (messages !== undefined) entries[field] = { value: this.draft()[field], messages: [...messages] };
    }
    this.serverFields.set(entries);
    this.serverForm.set([...formMessages]);
  }

  postAnother(): void {
    if (!this.workflow.postAnother()) return;
    this.draft.set({ ...EMPTY_DRAFT });
    this.attempted.set(false);
    this.today.set(localDate(this.clock()));
    this.setServerErrors({});
    afterNextRender(() => this.jobForm.title().focusBoundControl(), { injector: this.injector });
  }

  resolveRecovery(): void {
    if (this.workflow.resolveRecovery(this.recoveryConfirmed())) {
      this.recoveryConfirmed.set(false);
      this.draft.set({ ...EMPTY_DRAFT });
      this.attempted.set(false);
      this.setServerErrors({});
    }
  }

  onSubmit(event: Event): void {
    event.preventDefault();
    if (this.workflow.editingLocked()) return;
    this.today.set(localDate(this.clock()));
    this.attempted.set(true);
    const invalid = JOB_FIELDS.find(field => this.jobForm[field]().invalid());
    if (invalid !== undefined) { this.jobForm[invalid]().focusBoundControl(); return; }
    // Server messages report the previous attempt; local validity controls the next payload.
    const payload = normalizeDraft(this.draft());
    if (this.workflow.submit(payload)) {
      this.setServerErrors({});
      this.validPayload.emit(payload);
    }
  }
}
