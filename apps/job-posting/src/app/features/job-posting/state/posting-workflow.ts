import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, interval, Subscription, take } from 'rxjs';
import { API_CLOCK, JobPostingApi } from '../../../core/api/job-posting-api';
import { CreateJobRequest, PostingOutcome } from '../../../core/api/job-posting-contract';
import { PostingAttempt } from './posting-attempt';
import { PostingAttemptStore } from './posting-attempt-store';

export type WorkflowState = 'editing' | 'submitting' | 'saved' | 'rejected' | 'pending' | 'unknown' | 'throttled' | 'conflict' | 'recovery-blocked';
const messages: Record<WorkflowState, string> = {
  editing: '', submitting: 'Saving your job?', saved: 'Your job was saved.',
  rejected: 'The submission was rejected. Check the details before posting again.',
  pending: 'The submission is still being processed. Keep its original details for a later retry.',
  unknown: 'Saving is unconfirmed. Retry only this same submission.',
  throttled: 'Too many requests. Retry this same submission when the delay ends.',
  conflict: 'The submission conflicts with an existing request. Reconcile the prior outcome before resetting.',
  'recovery-blocked': 'Submission recovery is blocked.',
};

/** Scoped to the page so navigation tears down its requests and timers. */
@Injectable()
export class PostingWorkflow {
  readonly store = inject(PostingAttemptStore);
  private readonly api = inject(JobPostingApi);
  private readonly now = inject(API_CLOCK);
  private readonly destroyRef = inject(DestroyRef);
  private readonly result = signal<PostingOutcome | null>(null);
  private readonly clockNow = signal(this.now());
  private countdown?: Subscription;
  readonly outcome = this.result.asReadonly();
  readonly editingLocked = this.store.editingLocked;
  readonly savedRecord = computed(() => this.store.attempt()?.savedRecord ?? null);
  readonly state = computed<WorkflowState>(() => {
    const attempt = this.store.attempt();
    if (attempt?.status === 'saved') return 'saved';
    if (this.store.recoveryProblem() !== null) return 'recovery-blocked';
    if (attempt === null) return 'editing';
    return attempt.status === 'in-flight' ? 'submitting' : attempt.status;
  });
  readonly remainingSeconds = computed(() => {
    const deadline = this.store.attempt()?.retryAt;
    return deadline == null ? 0 : Math.max(0, Math.ceil((deadline - this.clockNow()) / 1000));
  });
  readonly canRetry = computed(() => ['unknown', 'pending', 'throttled'].includes(this.state()) && this.remainingSeconds() === 0);
  readonly message = computed(() => {
    const problem = this.store.recoveryProblem();
    if (problem !== null) return problem;
    const result = this.result();
    if (result?.kind === 'conflict' && result.reason === 'key-mismatch') return 'This key was used with different job details. Reconcile the prior submission before resetting.';
    if (result !== null && result.kind !== 'saved' && result.kind !== 'validation') return result.message;
    return messages[this.state()];
  });

  constructor() { this.updateCountdown(); }

  submit(payload: CreateJobRequest): boolean {
    const attempt = this.store.begin(payload);
    if (attempt === null) return false;
    this.dispatch(attempt);
    return true;
  }

  retry(): boolean {
    this.clockNow.set(this.now());
    if (!this.canRetry()) return false;
    const attempt = this.store.retry();
    if (attempt === null) return false;
    this.dispatch(attempt);
    return true;
  }

  postAnother(): boolean {
    if (!this.store.postAnother()) return false;
    this.result.set(null);
    return true;
  }

  resolveRecovery(confirmed: boolean): boolean {
    if (!this.store.resolveRecovery(confirmed)) return false;
    this.result.set(null);
    this.updateCountdown();
    return true;
  }

  private dispatch(attempt: PostingAttempt): void {
    this.result.set(null);
    this.updateCountdown();
    this.api.post(attempt.payload, attempt.key).pipe(
      take(1), takeUntilDestroyed(this.destroyRef),
      finalize(() => {
        if (this.store.attempt()?.status === 'in-flight') {
          this.store.settle(attempt.key, { kind: 'unknown', reason: 'unexpected', message: 'The request ended without confirming a save. Keep the original submission for retry.' });
        }
      }),
    ).subscribe(outcome => {
      this.result.set(outcome);
      this.store.settle(attempt.key, outcome);
      this.updateCountdown();
    });
  }

  private updateCountdown(): void {
    this.countdown?.unsubscribe();
    this.clockNow.set(this.now());
    if (this.remainingSeconds() === 0) return;
    this.countdown = interval(250).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.clockNow.set(this.now());
      if (this.remainingSeconds() === 0) this.countdown?.unsubscribe();
    });
  }
}
