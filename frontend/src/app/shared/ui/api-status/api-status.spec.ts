import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../../core/auth/testing';
import { ApiStatus } from './api-status';

describe('ApiStatus', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ApiStatus], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows the server as online when the health check succeeds', async () => {
    const fixture = TestBed.createComponent(ApiStatus);

    http.expectOne(`${TEST_API}/health`).flush('Healthy');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('SERVER · ONLINE');
  });

  it('offers a retry when the server is unreachable', async () => {
    const fixture = TestBed.createComponent(ApiStatus);

    http.expectOne(`${TEST_API}/health`).flush('', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('SERVER · UNREACHABLE');
    expect(element.querySelector('button')?.textContent).toContain('Retry');
  });
});
