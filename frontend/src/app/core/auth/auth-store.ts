import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthResponse, RegisterRequest, SessionStatus, SignInRequest, User } from './auth.models';
import { TokenStorage } from './token-storage';

const EXPIRY_MARGIN_MS = 30_000;

const REFRESH_LOCK = 'smartledger.refresh';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly storage = inject(TokenStorage);
  private readonly router = inject(Router);

  private readonly _user = signal<User | null>(null);
  private readonly _status = signal<SessionStatus>('restoring');

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

  async restoreSession(): Promise<void> {
    if (!this.storage.get()) {
      this._status.set('signed-out');
      return;
    }

    try {
      await this.refresh();
    } catch {
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
      await firstValueFrom(this.api.signOut(refreshToken)).catch(() => undefined);
    }
  }

  updateUser(user: User): void {
    this._user.set(user);
  }

  accountDeleted(): void {
    this.endSession({ redirect: true, reason: 'deleted' });
  }

  async getAccessToken(): Promise<string | null> {
    if (this.accessToken && Date.now() < this.accessTokenExpiresAt - EXPIRY_MARGIN_MS) {
      return this.accessToken;
    }

    if (!this.storage.get()) {
      return null;
    }

    return (await this.refresh()) ? this.accessToken : null;
  }

  refresh(): Promise<boolean> {
    this.refreshInFlight ??= this.withRefreshLock(() => this.exchangeRefreshToken()).finally(() => {
      this.refreshInFlight = null;
    });
    return this.refreshInFlight;
  }

  private async exchangeRefreshToken(): Promise<boolean> {
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
        this.endSession({ redirect: this._status() === 'signed-in', reason: 'expired' });
        return false;
      }
      throw error;
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

  private endSession(options: { redirect: boolean; reason?: 'expired' | 'deleted' }): void {
    this.accessToken = null;
    this.accessTokenExpiresAt = 0;
    this.storage.clear();
    this._user.set(null);
    this._status.set('signed-out');

    if (options.redirect) {
      void this.router.navigate(['/sign-in'], { queryParams: options.reason ? { [options.reason]: 1 } : {} });
    }
  }
}
