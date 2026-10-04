import { Injectable } from '@angular/core';
@Injectable({ providedIn: 'root' })
export class ListReturnFocus {
  private pending: { key: string; id: string } | undefined;
  remember(key: string, id: string): void { this.pending = { key, id }; }
  take(key: string): string | undefined {
    const pending = this.pending; this.pending = undefined;
    return pending?.key === key ? pending.id : undefined;
  }
}
