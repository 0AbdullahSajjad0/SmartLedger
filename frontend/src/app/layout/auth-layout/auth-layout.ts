import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ApiStatus } from '../../shared/ui/api-status/api-status';

/** Frame for sign-in and registration: the brand panel on the left, the form on the right. */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet, ApiStatus],
  templateUrl: './auth-layout.html',
  styleUrl: './auth-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthLayout {
  /** Decorative sample rows; the real ledger starts empty. */
  protected readonly excerpt = [
    { date: '19 OCT', vendor: 'Paper & Co. Office Supply', amount: '126.45' },
    { date: '18 OCT', vendor: 'CloudHost Inc.', amount: '89.00' },
    { date: '15 OCT', vendor: 'Grand Plaza Hotel', amount: '670.15' },
    { date: '12 OCT', vendor: 'Volt Electric', amount: '356.20' },
  ];
}
