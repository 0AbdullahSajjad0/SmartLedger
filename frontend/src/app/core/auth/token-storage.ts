import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

const REFRESH_TOKEN_KEY = 'smartledger.refreshToken';

@Injectable({ providedIn: 'root' })
export class TokenStorage {
  private readonly window = inject(DOCUMENT).defaultView;

  get(): string | null {
    try {
      return this.window?.localStorage.getItem(REFRESH_TOKEN_KEY) ?? null;
    } catch {
      return null;
    }
  }

  set(token: string): void {
    try {
      this.window?.localStorage.setItem(REFRESH_TOKEN_KEY, token);
    } catch {
    }
  }

  clear(): void {
    try {
      this.window?.localStorage.removeItem(REFRESH_TOKEN_KEY);
    } catch {
    }
  }

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
