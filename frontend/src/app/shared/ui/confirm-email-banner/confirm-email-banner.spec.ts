import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthStore } from '../../../core/auth/auth-store';
import { TEST_API, authResponse, provideAuthTesting } from '../../../core/auth/testing';
import { ConfirmEmailBanner } from './confirm-email-banner';

describe('ConfirmEmailBanner', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [ConfirmEmailBanner], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(emailConfirmed: boolean) {
    const signIn = TestBed.inject(AuthStore).signIn({ email: 'ada@example.com', password: 'secret', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush({
      ...authResponse('access-1', 'refresh-1'),
      user: { ...authResponse('', '').user, emailConfirmed },
    });
    await signIn;

    const fixture = TestBed.createComponent(ConfirmEmailBanner);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const button = (label: string) => [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    return { fixture, page, button };
  }

  it('stays away once the address is confirmed', async () => {
    const { page } = await render(true);

    expect(page.textContent?.trim()).toBe('');
  });

  it('asks to confirm, and sends the link again', async () => {
    const { fixture, page, button } = await render(false);
    expect(page.textContent).toContain('We sent a link to ada@example.com');

    button('Send the link again').click();
    (await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${TEST_API}/api/account/confirmation-email` }))).flush(
      null,
      { status: 202, statusText: 'Accepted' },
    );
    await fixture.whenStable();

    expect(page.textContent).toContain('A new link is on its way');
  });

  it('can be put aside for the rest of the visit', async () => {
    const { fixture, page, button } = await render(false);

    button('Not now').click();
    await fixture.whenStable();

    expect(page.textContent?.trim()).toBe('');
    expect(sessionStorage.getItem('smartledger.confirmBannerHidden')).toBe('1');
  });
});
