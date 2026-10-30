import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { AccountApi } from '../../../core/account/account-api';
import { AuthStore } from '../../../core/auth/auth-store';
import { describeHttpError } from '../../../core/http/describe-http-error';

const HIDDEN_KEY = 'smartledger.confirmBannerHidden';

/**
 * A note under the masthead while the email address is unconfirmed, with a button to send the link again. "Not now"
 * hides it until the browser tab is closed; it comes back next time, as alerts stay off until the address is confirmed.
 */
@Component({
  selector: 'app-confirm-email-banner',
  templateUrl: './confirm-email-banner.html',
  styleUrl: './confirm-email-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmailBanner {
  private readonly api = inject(AccountApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthStore);

  private readonly hidden = signal(readHidden());
  protected readonly sending = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly visible = computed(() => {
    const user = this.auth.user();
    return !!user && !user.emailConfirmed && !this.hidden();
  });

  protected sendAgain(): void {
    if (this.sending()) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    this.api
      .sendConfirmationEmail()
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.sent.set(true),
        error: (error: unknown) => this.error.set(describeHttpError(error)),
      });
  }

  protected hide(): void {
    this.hidden.set(true);
    try {
      sessionStorage.setItem(HIDDEN_KEY, '1');
    } catch {
      // Storage blocked: hidden for this page view only.
    }
  }
}

function readHidden(): boolean {
  try {
    return sessionStorage.getItem(HIDDEN_KEY) === '1';
  } catch {
    return false;
  }
}
