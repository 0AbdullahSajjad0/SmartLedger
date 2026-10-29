import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ServerStatus } from '../../../core/api/server-status';

/**
 * A slim bar across the top of every page while the server starts up from idle, with a running count, so a
 * minute's wait reads as "starting" rather than "broken". Gone as soon as the server answers.
 */
@Component({
  selector: 'app-startup-bar',
  templateUrl: './startup-bar.html',
  styleUrl: './startup-bar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StartupBar {
  protected readonly server = inject(ServerStatus);

  private readonly now = signal(Date.now());

  /** "0:12" since the wait began. */
  protected readonly elapsed = computed(() => {
    const seconds = Math.max(0, Math.floor((this.now() - this.server.startedAt()) / 1000));
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  constructor() {
    // Tick once a second, only while there's a wait to count.
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
