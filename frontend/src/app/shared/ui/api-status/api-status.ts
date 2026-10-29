import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ServerStatus } from '../../../core/api/server-status';

/**
 * One-line server status for the sign-in pages. It reads the app-wide check started when the page loaded, so
 * nothing is asked twice; the API scales to zero when idle, and this explains a slow first sign-in.
 */
@Component({
  selector: 'app-api-status',
  templateUrl: './api-status.html',
  styleUrl: './api-status.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiStatus {
  protected readonly server = inject(ServerStatus);

  constructor() {
    // Normally already started by the app; harmless if so.
    this.server.wake();
  }
}
