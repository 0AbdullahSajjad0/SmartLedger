import { Injectable, inject, signal } from '@angular/core';
import { HealthApi } from './health-api';

export type ServerState = 'checking' | 'waking' | 'ready' | 'unreachable';

export const WAKING_AFTER_MS = 1_500;

export const GIVE_UP_AFTER_MS = 90_000;

export const RETRY_EVERY_MS = 3_000;

@Injectable({ providedIn: 'root' })
export class ServerStatus {
  private readonly health = inject(HealthApi);

  private readonly _state = signal<ServerState>('checking');
  private readonly _startedAt = signal(Date.now());

  readonly state = this._state.asReadonly();
  readonly startedAt = this._startedAt.asReadonly();

  private running = false;
  private wakingTimer?: ReturnType<typeof setTimeout>;

  wake(): void {
    if (this.running || this._state() === 'ready') {
      return;
    }
    this.running = true;
    this._state.set('checking');
    this._startedAt.set(Date.now());

    clearTimeout(this.wakingTimer);
    this.wakingTimer = setTimeout(() => {
      if (this._state() === 'checking') {
        this._state.set('waking');
      }
    }, WAKING_AFTER_MS);

    this.attempt();
  }

  retry(): void {
    this.running = false;
    this.wake();
  }

  private attempt(): void {
    this.health.ready().subscribe({
      next: (healthy) => (healthy ? this.done('ready') : this.again()),
      error: () => this.again(),
    });
  }

  private again(): void {
    if (Date.now() - this._startedAt() >= GIVE_UP_AFTER_MS) {
      this.done('unreachable');
      return;
    }
    if (this._state() === 'checking') {
      this._state.set('waking');
    }
    setTimeout(() => this.attempt(), RETRY_EVERY_MS);
  }

  private done(state: 'ready' | 'unreachable'): void {
    clearTimeout(this.wakingTimer);
    this.running = false;
    this._state.set(state);
  }
}
