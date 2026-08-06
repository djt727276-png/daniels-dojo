import { Injectable, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, merge } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { MemberApi } from './member-api';
import { RealtimeService } from './realtime';

/** Minimum quiet time between navigation-triggered refreshes. */
const NAVIGATION_REFRESH_MS = 30_000;

/**
 * Live unread badges for the shell: notification and conversation counts from
 * the member dashboard endpoint.
 *
 * The service deliberately does not open the realtime connection itself — the
 * community screens own that lifecycle exactly as before. It listens for the
 * doorbell when some screen has the connection up, and otherwise refreshes on
 * sign-in and (rate-limited) on navigation, so the badge stays honest without
 * adding a new always-on socket to every page.
 *
 * Values are real server state only — no count is ever invented, and both
 * reset to zero on sign-out.
 */
@Injectable({ providedIn: 'root' })
export class UnreadStatusService {
  private readonly auth = inject(AuthService);
  private readonly api = inject(MemberApi);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);

  readonly notifications = signal(0);
  readonly conversations = signal(0);

  private lastRefreshAt = 0;

  constructor() {
    effect(() => {
      if (this.auth.session()) {
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

    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (this.auth.session() && Date.now() - this.lastRefreshAt > NAVIGATION_REFRESH_MS) {
          this.refresh();
        }
      });
  }

  private refresh(): void {
    this.lastRefreshAt = Date.now();
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
