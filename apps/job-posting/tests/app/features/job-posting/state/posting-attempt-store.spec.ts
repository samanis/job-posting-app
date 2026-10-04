import { TestBed } from '@angular/core/testing';
import { API_CLOCK } from '../../../../../src/app/core/api/job-posting-api';
import { PostingOutcome } from '../../../../../src/app/core/api/job-posting-contract';
import { ATTEMPT_STORAGE, ATTEMPT_STORAGE_KEY, ATTEMPT_UUID, AttemptStorage, PostingAttemptStore } from '../../../../../src/app/features/job-posting/state/posting-attempt-store';

const payload = { title: 'Engineer', department: 'Tech', location: 'Toronto', description: 'Build', salaryMin: 10, salaryMax: 20, closingDate: '2027-01-01' };
const saved = { ...payload, id: 'id-1', createdAt: '2026-10-04T15:00:00Z' };
const unknown: PostingOutcome = { kind: 'unknown', reason: 'network', message: 'Unconfirmed' };
const rejected: PostingOutcome = { kind: 'rejected', status: 400, message: 'Rejected' };

class MemoryStorage implements AttemptStorage {
  value: string | null = null;
  read = vi.fn(() => this.value);
  write = vi.fn((value: string) => { this.value = value; });
  remove = vi.fn(() => { this.value = null; });
}

describe('Posting attempt store', () => {
  let storage: MemoryStorage;
  let uuid: ReturnType<typeof vi.fn<() => string>>;
  let now: number;
  beforeEach(() => {
    storage = new MemoryStorage(); now = 1000;
    let sequence = 0;
    uuid = vi.fn(() => `key-${++sequence}`);
    TestBed.configureTestingModule({ providers: [
      { provide: ATTEMPT_STORAGE, useValue: storage },
      { provide: ATTEMPT_UUID, useValue: uuid },
      { provide: API_CLOCK, useValue: () => now },
    ] });
  });
  const store = () => TestBed.inject(PostingAttemptStore);
  function recovered(status: string, patch: Record<string, unknown> = {}) {
    storage.value = JSON.stringify({ version: 1, key: 'prior-key', payload, status, retryAt: null, savedRecord: null, ...patch });
    return store();
  }
  it('persists before returning dispatch permission and blocks rapid duplicate clicks', () => {
    const s = store(); expect(s.editingLocked()).toBe(false); expect(s.matches(payload)).toBe(false);
    const mutable = { ...payload };
    const attempt = s.begin(mutable)!;
    expect(storage.write).toHaveBeenCalledOnce();
    expect(JSON.parse(storage.value!)).toEqual(attempt);
    mutable.title = 'Changed';
    expect(attempt.payload.title).toBe('Engineer');
    expect(s.editingLocked()).toBe(true);
    expect(s.matches(payload)).toBe(true);
    expect(s.matches(mutable)).toBe(false);
    expect(s.begin(payload)).toBeNull();
    expect(s.retry()).toBeNull(); expect(uuid).toHaveBeenCalledOnce();
  });
  it('rejects invalid payloads without generating identities', () => {
    expect(store().begin({ ...payload, title: '' })).toBeNull(); expect(uuid).not.toHaveBeenCalled();
  });
  it.each(['unknown', 'pending', 'throttled'])('retries %s with the exact original key and payload', status => {
    const s = recovered(status);
    expect(s.editingLocked()).toBe(true);
    expect(s.begin({ ...payload, title: 'Changed' })).toBeNull();
    const result = s.retry()!;
    expect(result.key).toBe('prior-key'); expect(result.payload).toEqual(payload);
    expect(result.status).toBe('in-flight'); expect(uuid).not.toHaveBeenCalled();
  });
  it.each(['network', 'timeout', 'server', 'invalid-success', 'unexpected'] as const)('never replaces the key after unknown outcome %s', reason => {
    const s = store(); const a = s.begin(payload)!;
    s.settle(a.key, { kind: 'unknown', reason, message: 'Unconfirmed' });
    expect(s.begin({ ...payload, title: 'Changed' })).toBeNull();
    expect(s.retry()?.key).toBe(a.key);
    expect(uuid).toHaveBeenCalledOnce();
  });
  it('honors a throttle deadline before allowing explicit retry', () => {
    const s = recovered('throttled', { retryAt: 2000 });
    expect(s.retry()).toBeNull(); now = 2000; expect(s.retry()).not.toBeNull();
  });
  it('recovers in-flight as unknown without dispatching or clearing storage', () => {
    const s = recovered('in-flight'); expect(s.attempt()?.status).toBe('unknown');
    expect(storage.write).not.toHaveBeenCalled(); expect(storage.remove).not.toHaveBeenCalled();
    expect(uuid).not.toHaveBeenCalled();
  });
  it.each(['conflict', 'rejected', 'saved'])('does not retry %s', status => {
    const s = recovered(status, { savedRecord: status === 'saved' ? saved : null });
    expect(s.retry()).toBeNull();
  });
  it('does not retry without an attempt', () => expect(store().retry()).toBeNull());
  it.each([unknown, { kind: 'pending', reason: 'accepted', message: 'Pending' }, { kind: 'conflict', reason: 'key-mismatch', message: 'Conflict' }, { kind: 'throttled', retryAt: 2000, message: 'Wait' }, rejected, { kind: 'validation', status: 422, fieldErrors: {}, formErrors: ['Error'] }, { kind: 'saved', status: 201, record: saved }] satisfies PostingOutcome[])('settles outcome %j', outcome => {
    const s = store(); const a = s.begin(payload)!;
    expect(s.settle(a.key, outcome)).toBe(true);
    expect(s.attempt()?.status).toBe(outcome.kind === 'validation' ? 'rejected' : outcome.kind);
    expect(s.attempt()?.key).toBe(a.key);
    expect(s.attempt()?.payload).toEqual(payload);
  });
  it('ignores absent, mismatched and repeated response settlements', () => {
    const s = store(); expect(s.settle('absent', unknown)).toBe(false);
    const a = s.begin(payload)!; expect(s.settle('other', unknown)).toBe(false);
    expect(s.settle(a.key, unknown)).toBe(true); expect(s.settle(a.key, rejected)).toBe(false);
  });
  it('creates a fresh identity after definite rejection even for identical data', () => {
    const s = store(); const first = s.begin(payload)!; s.settle(first.key, rejected);
    expect(s.editingLocked()).toBe(false);
    expect(s.begin(payload)?.key).not.toBe(first.key);
  });
  it('requires explicit post-another after success', () => {
    const s = store(); expect(s.postAnother()).toBe(false);
    const a = s.begin(payload)!; s.settle(a.key, { kind: 'saved', record: saved, status: 201 });
    expect(s.begin(payload)).toBeNull(); expect(s.postAnother()).toBe(true);
    expect(storage.value).toBeNull(); expect(s.attempt()).toBeNull();
    expect(s.begin(payload)?.key).not.toBe(a.key);
  });
  it('keeps confirmed success when cleanup or saved persistence fails', () => {
    const s = store(); const a = s.begin(payload)!;
    storage.write.mockImplementation(() => { throw new Error('Quota'); });
    expect(s.settle(a.key, { kind: 'saved', record: saved, status: 201 })).toBe(false);
    expect(s.attempt()?.status).toBe('saved'); expect(s.attempt()?.savedRecord).toEqual(saved);
    storage.remove.mockImplementation(() => { throw new Error('Denied'); });
    expect(s.postAnother()).toBe(false); expect(s.attempt()?.status).toBe('saved');
    expect(s.begin(payload)).toBeNull();
  });
  it('blocks dispatch when the snapshot cannot be persisted', () => {
    storage.write.mockImplementation(() => { throw new Error('Quota'); });
    const s = store(); expect(s.begin(payload)).toBeNull(); expect(s.recoveryProblem()).toContain('storage');
    expect(s.retry()).toBeNull(); expect(s.editingLocked()).toBe(true);
  });
  it.each(['bad', '{}', '{"version":99}', 'null'])('blocks malformed recovery %s without deleting it', raw => {
    storage.value = raw; const s = store();
    expect(s.recoveryProblem()).not.toBeNull(); expect(s.begin(payload)).toBeNull();
    expect(storage.value).toBe(raw); expect(storage.remove).not.toHaveBeenCalled();
    expect(s.resolveRecovery(false)).toBe(false);
    expect(s.resolveRecovery(true)).toBe(true); expect(s.editingLocked()).toBe(false);
  });
  it('handles read failures and retries no dispatch until recovery is resolved', () => {
    storage.read.mockImplementation(() => { throw new Error('Denied'); });
    const s = store(); expect(s.recoveryProblem()).not.toBeNull(); expect(s.retry()).toBeNull();
    storage.remove.mockImplementation(() => { throw new Error('Denied'); });
    expect(s.resolveRecovery(true)).toBe(false); expect(s.editingLocked()).toBe(true);
  });
  it.each(['throw', 'invalid', 'reused'])('blocks UUID failure %s', mode => {
    const s = mode === 'reused' ? recovered('rejected') : store();
    uuid.mockImplementation(() => { if (mode === 'throw') throw new Error('Unavailable'); return mode === 'invalid' ? '' : 'prior-key'; });
    expect(s.begin(payload)).toBeNull(); expect(s.recoveryProblem()).toContain('identity');
  });
  it('allows conflict resolution only through explicit prior-outcome reconciliation', () => {
    const s = recovered('conflict'); expect(s.resolveRecovery(false)).toBe(false);
    expect(s.resolveRecovery(true)).toBe(true); expect(s.begin(payload)).not.toBeNull();
  });
  it('does not allow reset of unresolved or idle attempts without a recovery problem', () => {
    const s = store(); expect(s.resolveRecovery(true)).toBe(false);
    const a = s.begin(payload)!; s.settle(a.key, unknown);
    expect(s.resolveRecovery(true)).toBe(false);
  });
});

describe('Browser storage and UUID adapters', () => {
  afterEach(() => { sessionStorage.removeItem(ATTEMPT_STORAGE_KEY); vi.restoreAllMocks(); });
  it('uses the versioned session key and browser UUID by default', () => {
    const storage = TestBed.inject(ATTEMPT_STORAGE);
    expect(storage.read()).toBeNull(); storage.write('example'); expect(storage.read()).toBe('example');
    storage.remove(); expect(storage.read()).toBeNull();
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa');
    expect(TestBed.inject(ATTEMPT_UUID)()).toBe('aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa');
  });
});
