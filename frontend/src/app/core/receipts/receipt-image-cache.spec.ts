import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../auth/auth-store';
import { TEST_API, authResponse } from '../auth/testing';
import { API_BASE_URL } from '../config/api-base-url';
import { ReceiptImageCache, receiptImageCacheInterceptor } from './receipt-image-cache';

const preview = (id: string) => `${TEST_API}/api/receipts/${id}/preview`;

describe('receiptImageCacheInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([receiptImageCacheInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: TEST_API },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => http.verify());

  async function load(url: string): Promise<Blob> {
    const pending = firstValueFrom(client.get(url, { responseType: 'blob' }));
    // Only asked of the server when it isn't cached; expectNone keeps a cached hit honest.
    http.match(url).forEach((request) => request.flush(new Blob([url], { type: 'image/jpeg' })));
    return pending;
  }

  it('downloads a preview once, then answers from memory', async () => {
    const first = await load(preview('r-1'));
    const second = firstValueFrom(client.get(preview('r-1'), { responseType: 'blob' }));
    http.expectNone(preview('r-1'));

    expect(await (await second).text()).toBe(await first.text());
  });

  it("leaves everything that isn't a preview alone", async () => {
    const url = `${TEST_API}/api/receipts/r-1/file`;
    await load(url);

    void firstValueFrom(client.get(url, { responseType: 'blob' }));
    http.expectOne(url).flush(new Blob(['again']));
  });

  it('keeps only the most recently used previews', async () => {
    const cache = TestBed.inject(ReceiptImageCache);
    for (let i = 0; i < 14; i++) {
      await load(preview(`r-${i}`));
    }

    expect(cache.size).toBe(12);
    void firstValueFrom(client.get(preview('r-0'), { responseType: 'blob' }));
    http.expectOne(preview('r-0')).flush(new Blob(['evicted, so asked again']));
  });

  it('forgets everything when the user signs out', async () => {
    const auth = TestBed.inject(AuthStore);
    const cache = TestBed.inject(ReceiptImageCache);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const signIn = auth.signIn({ email: 'ada@example.com', password: 'secret', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));
    await signIn;
    await load(preview('r-1'));
    TestBed.tick();
    expect(cache.size).toBe(1);

    const signOut = auth.signOut();
    http.match(`${TEST_API}/api/auth/logout`).forEach((r) => r.flush(null));
    await signOut;
    TestBed.tick();

    expect(cache.size).toBe(0);
  });
});
