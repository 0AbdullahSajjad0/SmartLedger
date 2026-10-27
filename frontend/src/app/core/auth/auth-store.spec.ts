import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthStore } from './auth-store';
import { TEST_API, authResponse, provideAuthTesting } from './testing';
import { TokenStorage } from './token-storage';

describe('AuthStore', () => {
  let store: AuthStore;
  let storage: TokenStorage;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    store = TestBed.inject(AuthStore);
    storage = TestBed.inject(TokenStorage);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('is signed out after restoring when no session was saved', async () => {
    await store.restoreSession();

    expect(store.status()).toBe('signed-out');
    expect(store.isSignedIn()).toBe(false);
  });

  it('restores a saved session with the refresh token', async () => {
    storage.set('saved-refresh');

    const restoring = store.restoreSession();
    const request = http.expectOne(`${TEST_API}/api/auth/refresh`);
    expect(request.request.body).toEqual({ refreshToken: 'saved-refresh' });
    request.flush(authResponse('access-1', 'refresh-2'));
    await restoring;

    expect(store.isSignedIn()).toBe(true);
    expect(store.user()?.displayName).toBe('Ada Lovelace');
    expect(storage.get()).toBe('refresh-2');
  });

  it('shares one refresh between requests that need a token at the same time', async () => {
    storage.set('saved-refresh');

    const first = store.getAccessToken();
    const second = store.getAccessToken();
    http.expectOne(`${TEST_API}/api/auth/refresh`).flush(authResponse('access-1', 'refresh-2'));

    expect(await first).toBe('access-1');
    expect(await second).toBe('access-1');
  });

  it('ends the session and asks to sign in again when the refresh token is rejected', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const signingIn = store.signIn({ email: 'ada@example.com', password: 'correct horse battery', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));
    await signingIn;

    const refreshing = store.refresh();
    http.expectOne(`${TEST_API}/api/auth/refresh`).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await refreshing).toBe(false);
    expect(store.status()).toBe('signed-out');
    expect(storage.get()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/sign-in'], { queryParams: { expired: 1 } });
  });

  it('keeps the saved session when the server is unreachable during restore', async () => {
    storage.set('saved-refresh');

    const restoring = store.restoreSession();
    http.expectOne(`${TEST_API}/api/auth/refresh`).error(new ProgressEvent('error'));
    await restoring;

    expect(store.status()).toBe('signed-out');
    expect(storage.get()).toBe('saved-refresh');
  });
});
