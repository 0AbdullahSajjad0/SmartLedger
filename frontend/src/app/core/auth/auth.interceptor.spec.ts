import { HttpClient } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from './auth-store';
import { TEST_API, authResponse, provideAuthTesting } from './testing';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideAuthTesting() });
    httpClient = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);

    const signingIn = TestBed.inject(AuthStore).signIn({ email: 'ada@example.com', password: 'x', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));
    await signingIn;
  });

  afterEach(() => http.verify());

  it('adds the access token to requests for our API', async () => {
    const response = firstValueFrom(httpClient.get(`${TEST_API}/api/receipts`));
    await Promise.resolve();

    const request = http.expectOne(`${TEST_API}/api/receipts`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush([]);
    await response;
  });

  it('never sends the token to other sites', async () => {
    const response = firstValueFrom(httpClient.get('https://elsewhere.example/data'));

    const request = http.expectOne('https://elsewhere.example/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
    await response;
  });

  it('refreshes and retries once when the API rejects the token', async () => {
    const response = firstValueFrom(httpClient.get(`${TEST_API}/api/receipts`));
    await Promise.resolve();

    http.expectOne(`${TEST_API}/api/receipts`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    http.expectOne(`${TEST_API}/api/auth/refresh`).flush(authResponse('access-2', 'refresh-2'));
    await vi.waitFor(() => http.expectOne(`${TEST_API}/api/receipts`).flush(['ok']));

    expect(await response).toEqual(['ok']);
  });
});
