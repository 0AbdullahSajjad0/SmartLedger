import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthApi } from '../../../core/auth/auth-api';
import { AuthStore } from '../../../core/auth/auth-store';
import { describeHttpError } from '../../../core/http/describe-http-error';

type ConfirmState = 'confirming' | 'confirmed' | 'failed';

@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink],
  templateUrl: './confirm-email.html',
  styleUrl: '../auth-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmail implements OnInit {
  private readonly api = inject(AuthApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthStore);

  readonly user = input<string>();

  private readonly token = new URLSearchParams(inject(ActivatedRoute).snapshot.fragment ?? '').get('token');

  protected readonly state = signal<ConfirmState>('confirming');
  protected readonly error = signal<string | null>(null);

  constructor() {
    const location = inject(Location);
    location.replaceState(location.path(false));
  }

  ngOnInit(): void {
    const user = this.user();
    if (!user || !this.token) {
      this.fail('This link is incomplete. Copy the whole address from the email, or send a new one from your Account page.');
      return;
    }

    this.api
      .confirmEmail(user, this.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (confirmed) => {
          if (this.auth.user()?.id === confirmed.id) {
            this.auth.updateUser(confirmed);
          }
          this.state.set('confirmed');
        },
        error: (error: unknown) => this.fail(describeHttpError(error)),
      });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.state.set('failed');
  }
}
