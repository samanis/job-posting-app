import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { API_CLOCK } from '../../../core/api/job-posting-api';
import { CreateJobRequest, PostingOutcome } from '../../../core/api/job-posting-contract';
import { decodeAttempt, freezeAttempt, isPayload, payloadFingerprint, PostingAttempt } from './posting-attempt';

export interface AttemptStorage {
  read(): string | null;
  write(value: string): void;
  remove(): void;
}
export const ATTEMPT_STORAGE_KEY = 'job-posting.attempt.v1';
export const ATTEMPT_STORAGE = new InjectionToken<AttemptStorage>('Attempt storage', { providedIn: 'root', factory: () => ({
  read: () => sessionStorage.getItem(ATTEMPT_STORAGE_KEY),
  write: value => sessionStorage.setItem(ATTEMPT_STORAGE_KEY, value),
  remove: () => sessionStorage.removeItem(ATTEMPT_STORAGE_KEY),
}) });
export const ATTEMPT_UUID = new InjectionToken<() => string>('Attempt UUID', { providedIn: 'root', factory: () => () => crypto.randomUUID() });

@Injectable({ providedIn: 'root' })
export class PostingAttemptStore {
  private readonly storage = inject(ATTEMPT_STORAGE);
  private readonly uuid = inject(ATTEMPT_UUID);
  private readonly now = inject(API_CLOCK);
  private readonly current = signal<PostingAttempt | null>(null);
  private readonly problem = signal<string | null>(null);
  readonly attempt = this.current.asReadonly();
  readonly recoveryProblem = this.problem.asReadonly();
  readonly editingLocked = computed(() => this.problem() !== null || (this.current() !== null && this.current()!.status !== 'rejected'));

  constructor() { this.recover(); }

  /** Returns dispatch permission only after the snapshot is durably stored. */
  begin(payload: CreateJobRequest): PostingAttempt | null {
    if (this.editingLocked() || !isPayload(payload)) return null;
    try {
      const key = this.uuid();
      if (!/^[-a-zA-Z0-9]{1,128}$/.test(key)) throw new Error('Invalid UUID');
      const previous = this.current();
      if (previous !== null && previous.key === key) throw new Error('Reused UUID');
      return this.persist({ version: 1, key, payload, status: 'in-flight', retryAt: null, savedRecord: null });
    } catch {
      this.problem.set('A unique submission identity could not be created. Resolve recovery before posting.');
      return null;
    }
  }

  /** No draft argument: retries can only dispatch the original snapshot. */
  retry(): PostingAttempt | null {
    const attempt = this.current();
    if (this.problem() !== null || attempt === null || !['unknown', 'pending', 'throttled'].includes(attempt.status)) return null;
    if (attempt.retryAt !== null && this.now() < attempt.retryAt) return null;
    return this.persist({ ...attempt, status: 'in-flight', retryAt: null });
  }

  matches(payload: CreateJobRequest): boolean {
    const attempt = this.current();
    return attempt !== null && payloadFingerprint(attempt.payload) === payloadFingerprint(payload);
  }

  /** Ignore stale responses. The workflow must pass the dispatched key. */
  settle(key: string, outcome: PostingOutcome): boolean {
    const attempt = this.current();
    if (attempt === null || attempt.key !== key || attempt.status !== 'in-flight') return false;
    const status = outcome.kind === 'validation' ? 'rejected' : outcome.kind;
    const next: PostingAttempt = { ...attempt, status, retryAt: outcome.kind === 'throttled' ? outcome.retryAt : null, savedRecord: outcome.kind === 'saved' ? outcome.record : null };
    if (outcome.kind === 'saved') {
      // Preserve confirmed success in memory even if persistence fails.
      this.current.set(freezeAttempt(next));
    }
    return this.persist(next) !== null;
  }

  postAnother(): boolean {
    if (this.current()?.status !== 'saved') return false;
    return this.clear();
  }

  /** Caller must obtain explicit confirmation AFTER reconciling prior server outcome. */
  resolveRecovery(priorOutcomeReconciled: boolean): boolean {
    if (!priorOutcomeReconciled || (this.problem() === null && this.current()?.status !== 'conflict')) return false;
    return this.clear();
  }

  private recover(): void {
    try {
      const raw = this.storage.read();
      if (raw === null) return;
      const stored = decodeAttempt(raw);
      const attempt = stored.status === 'in-flight' ? freezeAttempt({ ...stored, status: 'unknown' }) : stored;
      this.current.set(attempt);
    } catch {
      this.problem.set('The previous submission cannot be recovered. Reconcile its server outcome before starting another job.');
    }
  }

  private persist(value: PostingAttempt): PostingAttempt | null {
    const attempt = freezeAttempt(value);
    try {
      this.storage.write(JSON.stringify(attempt));
      this.current.set(attempt);
      return attempt;
    } catch {
      this.problem.set('Submission recovery storage is unavailable. Posting is blocked; reconcile any prior server outcome before resetting.');
      return null;
    }
  }

  private clear(): boolean {
    try {
      this.storage.remove();
      this.current.set(null);
      this.problem.set(null);
      return true;
    } catch {
      this.problem.set('The previous submission could not be cleared. New posting remains blocked.');
      return false;
    }
  }
}
