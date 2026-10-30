import { Location } from '@angular/common';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { AuthStore } from '../../../core/auth/auth-store';
import { TEST_API, authResponse, provideAuthTesting } from '../../../core/auth/testing';
import { ConfirmEmail } from './confirm-email';

const CONFIRM_URL = `${TEST_API}/api/auth/confirm-email`;

describe('ConfirmEmail', () => {
  let http: HttpTestingController;

  function setUp(fragment: string | null) {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ConfirmEmail],
      providers: [...provideAuthTesting(), { provide: ActivatedRoute, useValue: { snapshot: { fragment } } }],
    });
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  async function render(user: string | undefined = 'user-1') {
    const replaceState = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('user', user);
    fixture.detectChanges();
    const settle = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, page: fixture.nativeElement as HTMLElement, settle, replaceState };
  }

  it('confirms straight away from the link, and clears the token from the address bar', async () => {
    setUp('token=abc_123-XYZ');
    const { page, settle, replaceState } = await render();
    expect(replaceState).toHaveBeenCalled();

    const request = http.expectOne({ method: 'POST', url: CONFIRM_URL });
    expect(request.request.body).toEqual({ userId: 'user-1', token: 'abc_123-XYZ' });
    request.flush(authResponse('', '').user);
    await settle();

    expect(page.querySelector('h2')?.textContent).toContain('Confirmed');
    expect(page.querySelector('a[href="/sign-in"]')).not.toBeNull();
  });

  it('updates the signed-in user, so the reminder disappears without a reload', async () => {
    setUp('token=abc');
    const auth = TestBed.inject(AuthStore);
    const signIn = auth.signIn({ email: 'ada@example.com', password: 'secret', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush({
      ...authResponse('access-1', 'refresh-1'),
      user: { ...authResponse('', '').user, emailConfirmed: false },
    });
    await signIn;
    const { page, settle } = await render();

    (await vi.waitFor(() => http.expectOne(CONFIRM_URL))).flush(authResponse('', '').user);
    await settle();

    expect(auth.user()?.emailConfirmed).toBe(true);
    expect(page.querySelector('a[href="/overview"]')?.textContent).toContain('Continue to SmartLedger');
  });

  it('explains an expired link', async () => {
    setUp('token=abc');
    const { page, settle } = await render();

    http.expectOne(CONFIRM_URL).flush(
      { title: "This link has expired or isn't complete. Send a new one from your Account page." },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('This link has expired');
  });

  it('says so when the link was cut short, without asking the server', async () => {
    setUp(null);
    const { page, settle } = await render();
    await settle();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('This link is incomplete.');
  });
});
