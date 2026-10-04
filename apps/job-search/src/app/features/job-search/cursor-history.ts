import { Injectable, signal } from '@angular/core';
import { criteriaKey, SearchQuery } from './search-query';
import { validCursor } from '../../core/api/query-validation';

/** One bounded criteria history survives list/detail navigation, never refresh. */
@Injectable({ providedIn: 'root' })
export class CursorHistory {
  private key = '';
  private entries: (string | null)[] = [];
  private index = 0;
  readonly canPrevious = signal(false);
  visit(query: SearchQuery): void {
    const key = criteriaKey(query);
    if (key !== this.key) { this.key = key; this.entries = []; }
    const found = this.entries.indexOf(query.cursor);
    if (found < 0) { this.entries = [query.cursor]; this.index = 0; }
    else this.index = found;
    this.canPrevious.set(this.index > 0);
  }
  prepareNext(cursor: string | null): boolean {
    if (cursor === null || !validCursor(cursor) || this.entries.slice(0, this.index + 1).includes(cursor)) return false;
    this.entries = this.entries.slice(0, this.index + 1);
    this.entries.push(cursor);
    if (this.entries.length > 50) { this.entries.shift(); this.index--; }
    return true;
  }
  previous(): string | null | undefined { return this.index > 0 ? this.entries[this.index - 1] : undefined; }
}
