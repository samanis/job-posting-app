import { HttpClient } from '@angular/common/http';
import { inject, Injectable, InjectionToken } from '@angular/core';
import { catchError, map, Observable, of, timeout } from 'rxjs';
import { CreateJobRequest, PostingOutcome } from './job-posting-contract';
import { classifyFailure, classifyResponse } from './posting-response';

export const POSTING_TIMEOUT_MS = new InjectionToken<number>('Posting request timeout', { providedIn: 'root', factory: () => 15000 });
export const API_CLOCK = new InjectionToken<() => number>('API clock', { providedIn: 'root', factory: () => () => Date.now() });

@Injectable({ providedIn: 'root' })
export class JobPostingApi {
  private readonly http = inject(HttpClient);
  private readonly now = inject(API_CLOCK);
  private readonly configuredTimeout = inject(POSTING_TIMEOUT_MS);

  /** One POST per subscription. The workflow owns attempt locking and manual retries. */
  post(payload: CreateJobRequest, key: string): Observable<PostingOutcome> {
    const duration = Number.isFinite(this.configuredTimeout)
      ? Math.min(60000, Math.max(1000, this.configuredTimeout)) : 15000;
    return this.http.post<unknown>('/api/jobs', payload, {
      observe: 'response', headers: { 'Idempotency-Key': key },
    }).pipe(
      timeout(duration),
      map(classifyResponse),
      catchError((error: unknown) => of(classifyFailure(error, this.now()))),
    );
  }
}
