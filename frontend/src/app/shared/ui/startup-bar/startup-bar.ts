import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ServerStatus } from '../../../core/api/server-status';

@Component({
  selector: 'app-startup-bar',
  templateUrl: './startup-bar.html',
  styleUrl: './startup-bar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StartupBar {
  protected readonly server = inject(ServerStatus);

  private readonly now = signal(Date.now());

  protected readonly elapsed = computed(() => {
    const seconds = Math.max(0, Math.floor((this.now() - this.server.startedAt()) / 1000));
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  constructor() {
    effect((onCleanup) => {
      if (this.server.state() !== 'waking') {
        return;
      }
      this.now.set(Date.now());
      const timer = setInterval(() => this.now.set(Date.now()), 1000);
      onCleanup(() => clearInterval(timer));
    });
  }
}
