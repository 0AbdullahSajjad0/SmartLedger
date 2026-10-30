import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';
import { ConfirmEmailBanner } from '../../shared/ui/confirm-email-banner/confirm-email-banner';

interface NavItem {
  label: string;
  path: string;
  icon: 'overview' | 'receipts' | 'scan' | 'reports' | 'categories';
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ConfirmEmailBanner],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  protected readonly auth = inject(AuthStore);

  protected readonly nav: readonly NavItem[] = [
    { label: 'Overview', path: '/overview', icon: 'overview' },
    { label: 'Receipts', path: '/receipts', icon: 'receipts' },
    { label: 'Scan', path: '/scan', icon: 'scan' },
    { label: 'Reports', path: '/reports', icon: 'reports' },
    { label: 'Categories', path: '/categories', icon: 'categories' },
  ];

  protected signOut(): void {
    void this.auth.signOut();
  }
}
