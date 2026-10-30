import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthApi } from '../../../core/auth/auth-api';
import { describeHttpError } from '../../../core/http/describe-http-error';

/**
 * Chooses a new password from the emailed link: /reset-password?user={id}#token={token}. The token travels after
 * "#", which browsers never send to a server, and is removed from the address bar as soon as the page has it, so it
 * doesn't stay in the history.
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPassword {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Query parameter: the account the link is for. */
  readonly user = input<string>();

  protected readonly token = readToken(inject(ActivatedRoute).snapshot.fragment);

  protected readonly form = inject(NonNullableFormBuilder).group({
    newPassword: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });

  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly serverError = signal<string | null>(null);

  constructor() {
    const location = inject(Location);
    location.replaceState(location.path(false));
  }

  protected showError(): boolean {
    const control = this.form.controls.newPassword;
    return control.invalid && (control.touched || this.submitted());
  }

  protected submit(): void {
    this.submitted.set(true);
    const user = this.user();
    if (this.form.invalid || this.submitting() || !user || !this.token) {
      return;
    }

    this.submitting.set(true);
    this.serverError.set(null);
    this.api
      .resetPassword(user, this.token, this.form.getRawValue().newPassword)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.router.navigate(['/sign-in'], { queryParams: { reset: 1 } }),
        error: (error: unknown) => this.serverError.set(describeHttpError(error)),
      });
  }
}

/** The token from "token=…" after the "#", or null when the link was cut short. */
function readToken(fragment: string | null): string | null {
  return new URLSearchParams(fragment ?? '').get('token') || null;
}
