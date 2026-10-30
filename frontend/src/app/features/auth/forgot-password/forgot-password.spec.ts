import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../../core/auth/testing';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [ForgotPassword], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(ForgotPassword);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const type = (value: string) => {
      const input = page.querySelector<HTMLInputElement>('#email')!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      page.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    return { fixture, page, type, submit };
  }

  it('asks for a valid address before sending anything', async () => {
    const { page, type, submit } = await render();

    type('not an email');
    await submit();

    expect(page.querySelector('#email-error')?.textContent).toContain('Enter a valid email address.');
  });

  it('sends the request and says the same thing whether or not the account exists', async () => {
    const { fixture, page, type, submit } = await render();

    type(' ada@example.com ');
    await submit();
    const request = http.expectOne({ method: 'POST', url: `${TEST_API}/api/auth/forgot-password` });
    expect(request.request.body).toEqual({ email: 'ada@example.com' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await fixture.whenStable();

    expect(page.querySelector('[role="status"]')?.textContent).toContain('If an account uses ada@example.com');
    expect(page.querySelector('form')).toBeNull();
  });
});
