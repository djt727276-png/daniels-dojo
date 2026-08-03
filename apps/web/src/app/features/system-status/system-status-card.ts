import { Component, computed, inject, signal } from '@angular/core';

import { SystemStatusApi } from '../../core/api/system-status-api';
import { SystemStatus } from '../../core/api/system-status.model';
import { StatusIndicator } from '../../shared/ui/status-indicator';

type StatusView =
  | { readonly kind: 'loading' }
  | { readonly kind: 'healthy'; readonly status: SystemStatus }
  | { readonly kind: 'unavailable' };

/**
 * Displays the live API status by calling the backend system-status endpoint.
 * Exposes explicit loading, healthy, and unavailable states and a retry action.
 */
@Component({
  selector: 'app-system-status-card',
  imports: [StatusIndicator],
  templateUrl: './system-status-card.html',
  styleUrl: './system-status-card.scss',
})
export class SystemStatusCard {
  private readonly api = inject(SystemStatusApi);

  protected readonly view = signal<StatusView>({ kind: 'loading' });

  /** Non-null only while the healthy status payload is available. */
  protected readonly healthy = computed<SystemStatus | null>(() => {
    const current = this.view();
    return current.kind === 'healthy' ? current.status : null;
  });

  constructor() {
    this.load();
  }

  protected retry(): void {
    this.load();
  }

  private load(): void {
    this.view.set({ kind: 'loading' });
    this.api.getStatus().subscribe({
      next: (status) => this.view.set({ kind: 'healthy', status }),
      error: () => this.view.set({ kind: 'unavailable' }),
    });
  }
}
