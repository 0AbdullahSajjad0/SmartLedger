import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { HealthApi } from '../../core/api/health-api';

type ApiStatus = 'checking' | 'waking' | 'online' | 'offline';

/** How long a health check can take before we tell the user the server is waking up. */
const WAKING_THRESHOLD_MS = 3_000;

@Component({
  selector: 'app-home',
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly healthApi = inject(HealthApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly status = signal<ApiStatus>('checking');

  constructor() {
    this.checkApi();
  }

  protected checkApi(): void {
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
