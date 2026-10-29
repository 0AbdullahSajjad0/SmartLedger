import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API } from '../auth/testing';
import { API_BASE_URL } from '../config/api-base-url';
import { GIVE_UP_AFTER_MS, RETRY_EVERY_MS, ServerStatus, WAKING_AFTER_MS } from './server-status';

const READY_URL = `${TEST_API}/health/ready`;

describe('ServerStatus', () => {
  let http: HttpTestingController;
  let server: ServerStatus;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: TEST_API }],
    });
    http = TestBed.inject(HttpTestingController);
    server = TestBed.inject(ServerStatus);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const unhealthy = { status: 503, statusText: 'Service Unavailable' };

  it('is ready straight away when the server is awake, without ever saying it is waking', () => {
    server.wake();
    http.expectOne(READY_URL).flush('Healthy');
    vi.advanceTimersByTime(WAKING_AFTER_MS + 1);

    expect(server.state()).toBe('ready');
  });

  it('says the server is waking when the answer is slow', () => {
    server.wake();
    expect(server.state()).toBe('checking');

    vi.advanceTimersByTime(WAKING_AFTER_MS + 1);
    expect(server.state()).toBe('waking');

    http.expectOne(READY_URL).flush('Healthy');
    expect(server.state()).toBe('ready');
  });

  it('keeps asking while the database is still resuming', () => {
    server.wake();
    http.expectOne(READY_URL).flush('Unhealthy', unhealthy);
    expect(server.state()).toBe('waking');

    vi.advanceTimersByTime(RETRY_EVERY_MS);
    http.expectOne(READY_URL).flush('Healthy');

    expect(server.state()).toBe('ready');
  });

  it('gives up after the longest a cold start takes, and can be retried', () => {
    server.wake();
    for (let waited = 0; waited < GIVE_UP_AFTER_MS; waited += RETRY_EVERY_MS) {
      http.expectOne(READY_URL).flush('', unhealthy);
      vi.advanceTimersByTime(RETRY_EVERY_MS);
    }
    http.match(READY_URL).forEach((request) => request.flush('', unhealthy));
    expect(server.state()).toBe('unreachable');

    server.retry();
    http.expectOne(READY_URL).flush('Healthy');
    expect(server.state()).toBe('ready');
  });

  it('asks only once when several places want the server awake', () => {
    server.wake();
    server.wake();

    http.expectOne(READY_URL).flush('Healthy');
  });
});
