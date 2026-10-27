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

  /** Grows as features ship: Receipts, Scan, Reports, Categories. */
  protected readonly nav: readonly NavItem[] = [{ label: 'Overview', path: '/overview' }];

  protected signOut(): void {
    void this.auth.signOut();
  }
}
