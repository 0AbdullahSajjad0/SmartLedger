import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { Home } from './home';

describe('Home', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('shows the API as online when the health check succeeds', async () => {
    const fixture = TestBed.createComponent(Home);

    httpMock.expectOne(`${environment.apiBaseUrl}/health`).flush('Healthy');
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('API online');
  });

  it('shows the API as unreachable with a retry button when the health check fails', async () => {
    const fixture = TestBed.createComponent(Home);

    httpMock
      .expectOne(`${environment.apiBaseUrl}/health`)
      .flush('', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('API unreachable');
    expect(element.querySelector('button')?.textContent).toContain('Retry');
  });
});
