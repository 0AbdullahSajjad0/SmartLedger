import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TEST_API, authResponse, provideAuthTesting } from '../../../core/auth/testing';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  let http: HttpTestingController;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [SignIn], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  function render(returnUrl?: string) {
    const fixture = TestBed.createComponent(SignIn);
    if (returnUrl) {
      fixture.componentRef.setInput('returnUrl', returnUrl);
    }
    fixture.detectChanges();
    return fixture;
  }

  function fill(element: HTMLElement, selector: string, value: string) {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(element: HTMLElement) {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  it('shows field errors and sends nothing when submitted empty', async () => {
    const fixture = render();

    submit(fixture.nativeElement);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Enter a valid email address.');
    expect(fixture.nativeElement.textContent).toContain('Enter your password.');
    http.expectNone(`${TEST_API}/api/auth/login`);
  });

  it('signs in and returns to the page the user came from', async () => {
    const fixture = render('/reports');
    fill(fixture.nativeElement, '#email', 'ada@example.com');
    fill(fixture.nativeElement, '#password', 'correct horse battery');

    submit(fixture.nativeElement);
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));

    await vi.waitFor(() => expect(navigateByUrl).toHaveBeenCalledWith('/reports'));
  });

  it('ignores a return address that would leave the site', async () => {
    const fixture = render('//evil.example');
    fill(fixture.nativeElement, '#email', 'ada@example.com');
    fill(fixture.nativeElement, '#password', 'correct horse battery');

    submit(fixture.nativeElement);
    http.expectOne(`${TEST_API}/api/auth/login`).flush(authResponse('access-1', 'refresh-1'));

    await vi.waitFor(() => expect(navigateByUrl).toHaveBeenCalledWith('/overview'));
  });

  it("shows the server's message when sign-in is refused", async () => {
    const fixture = render();
    fill(fixture.nativeElement, '#email', 'ada@example.com');
    fill(fixture.nativeElement, '#password', 'wrong password');

    submit(fixture.nativeElement);
    http
      .expectOne(`${TEST_API}/api/auth/login`)
      .flush({ title: 'Email or password is incorrect.' }, { status: 401, statusText: 'Unauthorized' });

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Email or password is incorrect.');
    });
  });

  it('confirms a deleted account, instead of saying the session ended', () => {
    const fixture = TestBed.createComponent(SignIn);
    fixture.componentRef.setInput('deleted', true);
    fixture.componentRef.setInput('expired', true);
    fixture.detectChanges();

    const notice = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(notice?.textContent).toContain('Your account and everything in it have been deleted.');
  });
});
