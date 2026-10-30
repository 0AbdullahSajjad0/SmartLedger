import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { AccountApi } from '../../core/account/account-api';
import { AuthStore } from '../../core/auth/auth-store';
import { describeHttpError } from '../../core/http/describe-http-error';
import { saveFile } from '../../shared/files/save-file';

class RequestState {
  readonly busy = signal(false);
  readonly done = signal(false);
  readonly error = signal<string | null>(null);

  constructor(private readonly destroyRef: DestroyRef) {}

  send<T>(request: Observable<T>, onSuccess: (value: T) => void = () => undefined, describe = describeHttpError): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.done.set(false);
    this.error.set(null);
    request
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.done.set(true);
          onSuccess(value);
        },
        error: (error: unknown) => this.error.set(describe(error)),
      });
  }
}

@Component({
  selector: 'app-account-page',
  imports: [ReactiveFormsModule],
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountPage {
  private readonly api = inject(AccountApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly forms = inject(NonNullableFormBuilder);
  protected readonly auth = inject(AuthStore);

  protected readonly profile = this.forms.group({
    displayName: [this.auth.user()?.displayName ?? '', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)]],
  });
  protected readonly password = this.forms.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });
  protected readonly deletion = this.forms.group({ password: ['', Validators.required] });

  protected readonly saveProfile = new RequestState(this.destroyRef);
  protected readonly changePassword = new RequestState(this.destroyRef);
  protected readonly saveAlerts = new RequestState(this.destroyRef);
  protected readonly sendConfirmation = new RequestState(this.destroyRef);
  protected readonly signOutOthers = new RequestState(this.destroyRef);
  protected readonly download = new RequestState(this.destroyRef);
  protected readonly deleteAccount = new RequestState(this.destroyRef);

  protected readonly submitted = signal<ReadonlySet<'profile' | 'password' | 'deletion'>>(new Set());
  protected readonly confirmingDelete = signal(false);

  protected showError(control: AbstractControl, form: 'profile' | 'password' | 'deletion'): boolean {
    return control.invalid && (control.touched || this.submitted().has(form));
  }

  protected submitProfile(): void {
    this.markSubmitted('profile');
    if (this.profile.invalid) {
      return;
    }
    this.saveProfile.send(this.api.updateProfile(this.profile.getRawValue().displayName.trim()), (user) => {
      this.auth.updateUser(user);
      this.profile.reset({ displayName: user.displayName });
    });
  }

  protected submitPassword(): void {
    this.markSubmitted('password');
    if (this.password.invalid) {
      return;
    }
    const { currentPassword, newPassword } = this.password.getRawValue();
    this.changePassword.send(this.api.changePassword(currentPassword, newPassword), () => {
      this.password.reset();
      this.unmarkSubmitted('password');
    });
  }

  protected setBudgetAlerts(box: HTMLInputElement): void {
    const on = box.checked;
    this.saveAlerts.send(
      this.api.updateNotifications(on),
      (user) => this.auth.updateUser(user),
      (error) => {
        box.checked = !on;
        return describeHttpError(error);
      },
    );
  }

  protected sendConfirmationEmail(): void {
    this.sendConfirmation.send(this.api.sendConfirmationEmail());
  }

  protected signOutOtherDevices(): void {
    this.signOutOthers.send(this.api.signOutOtherDevices());
  }

  protected downloadData(): void {
    this.download.send(
      this.api.export(),
      (blob) => saveFile(blob, `smartledger-export-${localDate()}.zip`),
      (error) =>
        error instanceof HttpErrorResponse && error.status === 429
          ? 'You can download your data 5 times an hour. Please try again later.'
          : describeHttpError(error),
    );
  }

  protected cancelDelete(): void {
    this.confirmingDelete.set(false);
    this.deletion.reset();
    this.unmarkSubmitted('deletion');
    this.deleteAccount.error.set(null);
  }

  protected submitDeletion(): void {
    this.markSubmitted('deletion');
    if (this.deletion.invalid) {
      return;
    }
    this.deleteAccount.send(this.api.delete(this.deletion.getRawValue().password), () => this.auth.accountDeleted());
  }

  private markSubmitted(form: 'profile' | 'password' | 'deletion'): void {
    this.submitted.update((forms) => new Set(forms).add(form));
  }

  private unmarkSubmitted(form: 'profile' | 'password' | 'deletion'): void {
    this.submitted.update((forms) => new Set([...forms].filter((f) => f !== form)));
  }
}

function localDate(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
