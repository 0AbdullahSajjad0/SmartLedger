import { Location } from '@angular/common';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TEST_API, provideAuthTesting } from '../../../core/auth/testing';
import { ResetPassword } from './reset-password';

const RESET_URL = `${TEST_API}/api/auth/reset-password`;

describe('ResetPassword', () => {
  let http: HttpTestingController;

  function setUp(fragment: string | null) {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [...provideAuthTesting(), { provide: ActivatedRoute, useValue: { snapshot: { fragment } } }],
    });
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  async function render(user: string | undefined = 'user-1') {
    const replaceState = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ResetPassword);
    fixture.componentRef.setInput('user', user);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const type = (value: string) => {
      const input = page.querySelector<HTMLInputElement>('#newPassword')!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      page.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    return { fixture, page, type, submit, replaceState };
  }

  it('takes the token from after the "#", clears it from the address, and sets the new password', async () => {
    setUp('token=abc_123-XYZ');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { type, submit, replaceState } = await render();
    expect(replaceState).toHaveBeenCalled();

    type('a brand new long passphrase');
    await submit();
    const request = http.expectOne({ method: 'POST', url: RESET_URL });
    expect(request.request.body).toEqual({ userId: 'user-1', token: 'abc_123-XYZ', newPassword: 'a brand new long passphrase' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(navigate).toHaveBeenCalledWith(['/sign-in'], { queryParams: { reset: 1 } });
  });

  it('asks for at least 10 characters', async () => {
    setUp('token=abc');
    const { page, type, submit } = await render();

    type('short');
    await submit();

    expect(page.querySelector('#newPassword-hint')?.textContent).toContain('Use at least 10 characters.');
  });

  it("explains an expired or used link, with a way to ask for another", async () => {
    setUp('token=abc');
    const { fixture, page, type, submit } = await render();

    type('a brand new long passphrase');
    await submit();
    http.expectOne(RESET_URL).flush(
      { title: 'This link has expired or was already used. Ask for a new one.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('This link has expired or was already used.');
    expect(page.querySelector('a[href="/forgot-password"]')).not.toBeNull();
  });

  it('says so when the link was cut short', async () => {
    setUp(null);
    const { page } = await render();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('This link is incomplete.');
    expect(page.querySelector('form')).toBeNull();
  });
});
