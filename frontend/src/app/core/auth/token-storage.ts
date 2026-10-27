import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

const REFRESH_TOKEN_KEY = 'smartledger.refreshToken';

/**
 * Keeps the refresh token in localStorage so a session survives closing the tab.
 * The access token is never stored; it lives in memory only.
 *
 * Trade-off (chosen deliberately until the app has its own domain): script injected through an XSS bug
 * could read this token. Mitigated by short-lived, single-use tokens with reuse detection on the API and
 * a strict Content Security Policy on the site. The plan is to move to an HttpOnly cookie later.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorage {
  private readonly window = inject(DOCUMENT).defaultView;

  get(): string | null {
    try {
      return this.window?.localStorage.getItem(REFRESH_TOKEN_KEY) ?? null;
    } catch {
      return null; // Storage can be blocked (private mode, browser settings).
    }
  }

  set(token: string): void {
    try {
      this.window?.localStorage.setItem(REFRESH_TOKEN_KEY, token);
    } catch {
      // Without storage the session simply won't survive a reload.
    }
  }

  clear(): void {
    try {
      this.window?.localStorage.removeItem(REFRESH_TOKEN_KEY);
    } catch {
      // Nothing to clear.
    }
  }

  /** Calls back when another tab signs out, so every tab ends the session together. Returns an unsubscribe function. */
  onRemovedInAnotherTab(callback: () => void): () => void {
    const listener = (event: StorageEvent) => {
      if (event.key === REFRESH_TOKEN_KEY && event.newValue === null) {
        callback();
      }
    };
    this.window?.addEventListener('storage', listener);
    return () => this.window?.removeEventListener('storage', listener);
  }
}
