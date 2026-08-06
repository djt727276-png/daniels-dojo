import { Injectable, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { MemberApi } from './member-api';
import { RealtimeService } from './realtime';

/**
 * Live unread badges for the shell: notification and conversation counts from
 * the member dashboard endpoint, refreshed when the realtime doorbell reports
 * a change and after a reconnect.
 *
 * Values are real server state only — no count is ever invented, and both
 * reset to zero on sign-out.
 */
@Injectable({ providedIn: 'root' })
export class UnreadStatusService {
  private readonly auth = inject(AuthService);
  private readonly api = inject(MemberApi);
  private readonly realtime = inject(RealtimeService);

  readonly notifications = signal(0);
  readonly conversations = signal(0);

  constructor() {
    effect(() => {
      if (this.auth.session()) {
        this.realtime.connect();
        this.refresh();
      } else {
        this.notifications.set(0);
        this.conversations.set(0);
      }
    });

    merge(this.realtime.unreadChanged, this.realtime.reconnected)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (this.auth.session()) {
          this.refresh();
        }
      });
  }

  private refresh(): void {
    this.api.getDashboard().subscribe({
      next: (dashboard) => {
        this.notifications.set(dashboard.unreadNotificationCount);
        this.conversations.set(dashboard.unreadConversationCount);
      },
      // Badges are an enhancement; a failed fetch just leaves them unchanged.
      error: () => undefined,
    });
  }
}
