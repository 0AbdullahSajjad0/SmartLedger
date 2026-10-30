import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ServerStatus } from '../../../core/api/server-status';

@Component({
  selector: 'app-api-status',
  templateUrl: './api-status.html',
  styleUrl: './api-status.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiStatus {
  protected readonly server = inject(ServerStatus);

  constructor() {
    this.server.wake();
  }
}
