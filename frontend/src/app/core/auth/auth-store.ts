import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthResponse, RegisterRequest, SessionStatus, SignInRequest, User } from './auth.models';
import { TokenStorage } from './token-storage';

/** Refresh this long before the access token expires, so requests never race its expiry. */
const EXPIRY_MARGIN_MS = 30_000;

/** Name of the browser-wide lock that stops two tabs from using the same refresh token at once. */
const REFRESH_LOCK = 'smartledger.refresh';

/**
 * The single source of truth for the signed-in session. State is held in signals: components read
 * `user()`, `status()` and `isSignedIn()`; only this class can change them.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly storage = inject(TokenStorage);
  private readonly router = inject(Router);

  private readonly _user = signal<User | null>(null);
  private readonly _status = signal<SessionStatus>('restoring');

  /** Memory only: gone on reload, restored from the refresh token. */
  private accessToken: string | null = null;
  private accessTokenExpiresAt = 0;
  private refreshInFlight: Promise<boolean> | null = null;

  readonly user = this._user.asReadonly();
  readonly status = this._status.asReadonly();
  readonly isSignedIn = computed(() => this._status() === 'signed-in');

  constructor() {
    const stopListening = this.storage.onRemovedInAnotherTab(() => this.endSession({ redirect: true }));
    inject(DestroyRef).onDestroy(stopListening);
  }

  /** Called once on startup: signs back in with a saved refresh token, if there is one. */
  async restoreSession(): Promise<void> {
    if (!this.storage.get()) {
      this._status.set('signed-out');
      return;
    }

    try {
      await this.refresh();
    } catch {
      // The API couldn't be reached. Keep the saved token so a later reload can still restore the session.
      this._status.set('signed-out');
    }
  }

  async signIn(request: SignInRequest): Promise<void> {
    this.startSession(await firstValueFrom(this.api.signIn(request)));
  }

  async register(request: RegisterRequest): Promise<void> {
    this.startSession(await firstValueFrom(this.api.register(request)));
  }

  async signOut(): Promise<void> {
    const refreshToken = this.storage.get();
    this.endSession({ redirect: true });

    if (refreshToken) {
      // Best effort: the session is already gone locally, the API call just revokes the token server-side.
      await firstValueFrom(this.api.signOut(refreshToken)).catch(() => undefined);
    }
  }

  /** An access token that is valid for at least the next 30 seconds, refreshing first if needed. */
  async getAccessToken(): Promise<string | null> {
    if (this.accessToken && Date.now() < this.accessTokenExpiresAt - EXPIRY_MARGIN_MS) {
      return this.accessToken;
    }

    // Signed out: nothing to refresh, send the request without a token.
    if (!this.storage.get()) {
      return null;
    }

    return (await this.refresh()) ? this.accessToken : null;
  }

  /**
   * Swaps the refresh token for new tokens. Concurrent callers in this tab share one request, and a
   * browser-wide lock makes other tabs wait their turn and then use the newest token, because reusing
   * an already-exchanged token would look like theft to the API and end the session everywhere.
   * Resolves false when the session is over (the user must sign in again).
   */
  refresh(): Promise<boolean> {
    this.refreshInFlight ??= this.withRefreshLock(() => this.exchangeRefreshToken()).finally(() => {
      this.refreshInFlight = null;
    });
    return this.refreshInFlight;
  }

  private async exchangeRefreshToken(): Promise<boolean> {
    // Read inside the lock: another tab may have just rotated it.
    const refreshToken = this.storage.get();
    if (!refreshToken) {
      this.endSession({ redirect: this._status() === 'signed-in' });
      return false;
    }

    try {
      this.startSession(await firstValueFrom(this.api.refresh(refreshToken)));
      return true;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.endSession({ redirect: this._status() === 'signed-in', expired: true });
        return false;
      }
      throw error; // Network trouble is not a reason to sign the user out.
    }
  }

  private withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
    const locks = (globalThis.navigator as Navigator | undefined)?.locks;
    return locks ? locks.request(REFRESH_LOCK, work) : work();
  }

  private startSession(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.accessTokenExpiresAt = Date.parse(response.accessTokenExpiresAt);
    this.storage.set(response.refreshToken);
    this._user.set(response.user);
    this._status.set('signed-in');
  }

  private endSession(options: { redirect: boolean; expired?: boolean }): void {
    this.accessToken = null;
    this.accessTokenExpiresAt = 0;
    this.storage.clear();
    this._user.set(null);
    this._status.set('signed-out');

    if (options.redirect) {
      void this.router.navigate(['/sign-in'], { queryParams: options.expired ? { expired: 1 } : {} });
    }
  }
}
