import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';
import { TEST_API, authResponse, provideAuthTesting } from '../../core/auth/testing';
import { AccountPage } from './account-page';

const ACCOUNT_URL = `${TEST_API}/api/account`;

describe('AccountPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [AccountPage], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);

    // Signed in as Ada, with refresh token "refresh-1" saved for this device.
    const signIn = TestBed.inject(AuthStore).signIn({ email: 'ada@example.com', password: 'secret', rememberMe: false });
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));
    await signIn;
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  async function render() {
    const fixture = TestBed.createComponent(AccountPage);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const button = (label: string) => [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    const type = (selector: string, value: string) => {
      const input = page.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const settle = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, page, button, type, settle };
  }

  it('shows who is signed in and saves a new name for the masthead', async () => {
    const { page, button, type, settle } = await render();
    expect(page.querySelector('.intro')?.textContent).toContain('ada@example.com');
    expect(page.querySelector<HTMLInputElement>('#displayName')?.value).toBe('Ada Lovelace');

    type('#displayName', '  Ada King ');
    button('Save name').click();
    const request = await vi.waitFor(() => http.expectOne({ method: 'PUT', url: ACCOUNT_URL }));
    expect(request.request.body).toEqual({ displayName: 'Ada King' });
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({ ...authResponse('', '').user, displayName: 'Ada King' });
    await settle();

    expect(TestBed.inject(AuthStore).user()?.displayName).toBe('Ada King');
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Saved.');
  });

  it('refuses a blank name without asking the server', async () => {
    const { page, button, type, settle } = await render();

    type('#displayName', '   ');
    button('Save name').click();
    await settle();

    expect(page.querySelector('#displayName-error')?.textContent).toContain('Enter the name');
  });

  it('changes the password, keeping this device signed in', async () => {
    const { page, button, type, settle } = await render();

    type('#currentPassword', 'correct horse battery staple');
    type('#newPassword', 'a different long passphrase');
    button('Change password').click();
    const request = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${ACCOUNT_URL}/password` }));
    expect(request.request.body).toEqual({
      currentPassword: 'correct horse battery staple',
      newPassword: 'a different long passphrase',
      refreshToken: 'refresh-1',
    });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(page.textContent).toContain('Password changed. Other devices have been signed out.');
    expect(page.querySelector<HTMLInputElement>('#currentPassword')?.value).toBe('');
    expect(TestBed.inject(AuthStore).isSignedIn()).toBe(true);
  });

  it("says when the current password is wrong, without signing out", async () => {
    const { page, button, type, settle } = await render();

    type('#currentPassword', 'not it');
    type('#newPassword', 'a different long passphrase');
    button('Change password').click();
    const request = await vi.waitFor(() => http.expectOne(`${ACCOUNT_URL}/password`));
    request.flush({ title: "That password isn't right." }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain("That password isn't right.");
    expect(TestBed.inject(AuthStore).isSignedIn()).toBe(true);
  });

  it('asks for a new password of at least 10 characters', async () => {
    const { page, button, type, settle } = await render();

    type('#currentPassword', 'correct horse battery staple');
    type('#newPassword', 'short');
    button('Change password').click();
    await settle();

    expect(page.querySelector('#newPassword-hint')?.textContent).toContain('Use at least 10 characters.');
  });

  it('signs out the other devices', async () => {
    const { page, button, settle } = await render();

    button('Sign out other devices').click();
    const request = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${ACCOUNT_URL}/sign-out-others` }));
    expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(page.textContent).toContain('Every other device has been signed out.');
  });

  it('downloads the data as a zip, and explains the hourly limit', async () => {
    const { page, button, settle } = await render();
    URL.createObjectURL = vi.fn(() => 'blob:export');
    URL.revokeObjectURL = vi.fn();
    let savedAs: string | null = null;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      savedAs = this.download;
    });

    button('Download my data (.zip)').click();
    (await vi.waitFor(() => http.expectOne(`${ACCOUNT_URL}/export`))).flush(new Blob(['PK'], { type: 'application/zip' }));
    await settle();
    expect(savedAs).toMatch(/^smartledger-export-\d{4}-\d{2}-\d{2}\.zip$/);

    button('Download my data (.zip)').click();
    (await vi.waitFor(() => http.expectOne(`${ACCOUNT_URL}/export`))).flush(new Blob([]), {
      status: 429,
      statusText: 'Too Many Requests',
    });
    await settle();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('5 times an hour');
  });

  it('deletes the account only after the password is entered, then leaves for the sign-in page', async () => {
    const { page, button, type, settle } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    button('Delete my account').click();
    await settle();
    expect(page.querySelector('#delete-question')?.textContent).toContain("ada@example.com and everything in it? This can't be undone.");

    button('Delete permanently').click();
    await settle();
    expect(page.querySelector('#deletePassword-error')?.textContent).toContain('Enter your password.');

    type('#deletePassword', 'correct horse battery staple');
    button('Delete permanently').click();
    const request = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${ACCOUNT_URL}/delete` }));
    expect(request.request.body).toEqual({ password: 'correct horse battery staple' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(TestBed.inject(AuthStore).isSignedIn()).toBe(false);
    expect(localStorage.getItem('smartledger.refreshToken')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/sign-in'], { queryParams: { deleted: 1 } });
  });

  it('can back out of deleting', async () => {
    const { page, button, settle } = await render();

    button('Delete my account').click();
    await settle();
    button('Cancel').click();
    await settle();

    expect(page.querySelector('#deletePassword')).toBeNull();
    expect(button('Delete my account')).toBeDefined();
  });
});
