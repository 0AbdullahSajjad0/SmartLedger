import { Injectable, inject, signal } from '@angular/core';
import { HealthApi } from './health-api';

/**
 * - checking: asked, no answer yet (usually a few hundred milliseconds).
 * - waking: no answer for a moment, so the server is starting up from idle.
 * - ready: the API and its database answer.
 * - unreachable: still nothing after the longest a cold start takes; offer a retry.
 */
export type ServerState = 'checking' | 'waking' | 'ready' | 'unreachable';

/** Answers slower than this mean the server is starting up, worth telling the user about. */
export const WAKING_AFTER_MS = 1_500;

/** A cold start (container plus database) takes up to about a minute; give it a margin before giving up. */
export const GIVE_UP_AFTER_MS = 90_000;

/** Pause between readiness checks while waiting. */
export const RETRY_EVERY_MS = 3_000;

/**
 * Wakes the API as soon as the page loads and tracks whether it's up. The API scales to zero and its database
 * pauses when no one is using them, so the first visit after a quiet spell waits up to a minute; the startup bar
 * and the sign-in page explain that wait instead of leaving a blank screen.
 */
@Injectable({ providedIn: 'root' })
export class ServerStatus {
  private readonly health = inject(HealthApi);

  private readonly _state = signal<ServerState>('checking');
  private readonly _startedAt = signal(Date.now());

  readonly state = this._state.asReadonly();
  /** When the current wait began, for "0:12" counters. */
  readonly startedAt = this._startedAt.asReadonly();

  private running = false;
  private wakingTimer?: ReturnType<typeof setTimeout>;

  /** Starts checking, unless a check is already running or the server is known to be up. */
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

  /** After "unreachable": try again from the start. */
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
    // Still waking (or the database is still resuming): ask again shortly.
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
