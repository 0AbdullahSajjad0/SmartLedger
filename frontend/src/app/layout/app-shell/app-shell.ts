import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';

interface NavItem {
  label: string;
  path: string;
}

/** Frame for every signed-in page: the espresso masthead with navigation, then the page. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  protected readonly auth = inject(AuthStore);

  /** Grows as features ship: Reports and Categories come later. */
  protected readonly nav: readonly NavItem[] = [
    { label: 'Overview', path: '/overview' },
    { label: 'Receipts', path: '/receipts' },
    { label: 'Scan', path: '/scan' },
  ];

  protected signOut(): void {
    void this.auth.signOut();
  }
}
