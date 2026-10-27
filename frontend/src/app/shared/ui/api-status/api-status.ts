import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { HealthApi } from '../../../core/api/health-api';

type ServerStatus = 'checking' | 'waking' | 'online' | 'offline';

/** How long a health check can take before we tell the user the server is waking up. */
const WAKING_THRESHOLD_MS = 3_000;

/**
 * One-line server status. The API scales to zero when idle, so the first request can take a while;
 * this explains the wait instead of leaving the user staring at a button that doesn't respond.
 */
@Component({
  selector: 'app-api-status',
  templateUrl: './api-status.html',
  styleUrl: './api-status.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiStatus {
  private readonly healthApi = inject(HealthApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly status = signal<ServerStatus>('checking');

  constructor() {
    this.check();
  }

  protected check(): void {
    this.status.set('checking');

    const wakingTimer = setTimeout(() => {
      if (this.status() === 'checking') {
        this.status.set('waking');
      }
    }, WAKING_THRESHOLD_MS);

    this.healthApi
      .check()
      .pipe(
        finalize(() => clearTimeout(wakingTimer)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (healthy) => this.status.set(healthy ? 'online' : 'offline'),
        error: () => this.status.set('offline'),
      });
  }
}
