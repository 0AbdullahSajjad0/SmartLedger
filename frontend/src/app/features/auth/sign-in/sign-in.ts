import { ChangeDetectionStrategy, Component, booleanAttribute, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../../core/auth/auth-store';
import { describeHttpError } from '../../../core/http/describe-http-error';

type SignInField = 'email' | 'password';

@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './sign-in.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignIn {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  /** Query parameter: the page that sent the user here, to return to after signing in. */
  readonly returnUrl = input<string>();

  /** Query parameter: set when the session ran out. */
  readonly expired = input(false, { transform: booleanAttribute });

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    rememberMe: [false],
  });

  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly serverError = signal<string | null>(null);

  /** Errors appear once a field has been left, or after a submit attempt, not while typing the first time. */
  protected showError(field: SignInField): boolean {
    const control = this.form.controls[field];
    return control.invalid && (control.touched || this.submitted());
  }

  protected async submit(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.serverError.set(null);

    try {
      await this.auth.signIn(this.form.getRawValue());
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch (error) {
      this.serverError.set(describeHttpError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  /** Only follow paths inside this app; "//evil.example" would leave it (an open redirect). */
  private safeReturnUrl(): string {
    const url = this.returnUrl();
    return url?.startsWith('/') && !url.startsWith('//') ? url : '/overview';
  }
}
