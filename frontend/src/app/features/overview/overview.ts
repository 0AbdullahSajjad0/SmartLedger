import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthStore } from '../../core/auth/auth-store';

/** The signed-in home page. Shows the monthly statement once receipts exist; for now, a welcome. */
@Component({
  selector: 'app-overview',
  imports: [DatePipe],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Overview {
  private readonly auth = inject(AuthStore);

  protected readonly today = new Date();
  protected readonly user = this.auth.user;
  protected readonly firstName = computed(() => this.user()?.displayName.split(' ')[0] ?? '');
}
