import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../../core/auth/auth-store';
import { describeHttpError } from '../../../core/http/describe-http-error';

type RegisterField = 'displayName' | 'email' | 'password' | 'baseCurrency';

/** Currencies offered at sign-up. Receipts can be in any currency; reports convert to this one. */
const BASE_CURRENCIES = [
  { code: 'USD', name: 'US dollar' },
  { code: 'EUR', name: 'Euro' },
  { code: 'GBP', name: 'British pound' },
  { code: 'PKR', name: 'Pakistani rupee' },
  { code: 'AED', name: 'UAE dirham' },
  { code: 'SAR', name: 'Saudi riyal' },
  { code: 'INR', name: 'Indian rupee' },
  { code: 'CAD', name: 'Canadian dollar' },
  { code: 'AUD', name: 'Australian dollar' },
] as const;

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Register {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly currencies = BASE_CURRENCIES;

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    // Matches the API's rule: length, not forced symbols.
    password: ['', [Validators.required, Validators.minLength(10)]],
    baseCurrency: ['USD', Validators.required],
  });

  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly serverError = signal<string | null>(null);

  protected showError(field: RegisterField): boolean {
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
      const value = this.form.getRawValue();
      await this.auth.register({ ...value, displayName: value.displayName.trim() });
      await this.router.navigateByUrl('/overview');
    } catch (error) {
      this.serverError.set(describeHttpError(error));
    } finally {
      this.submitting.set(false);
    }
  }
}
