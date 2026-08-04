import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { CommunityApi, NotificationView } from '../../core/community/community-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

/**
 * Where a notification points.
 *
 * The API sends a target type and identifier, never content, so opening one goes back through
 * the ordinary screen — which re-applies every privacy, friendship, and block rule. A
 * notification can therefore never become a way to read something you have lost access to.
 */
function targetRoute(notification: NotificationView): readonly string[] | null {
  switch (notification.targetType) {
    case 'Post':
    case 'Thread':
      // A post id has no route of its own, so this lands on the community page where the
      // thread it belongs to is listed under recent discussion.
      return ['/community'];
    case 'Conversation':
      return ['/messages', notification.targetId];
    case 'FriendRequest':
    case 'Friendship':
      return ['/friends'];
    case 'Profile':
      return ['/account'];
    default:
      return null;
  }
}

/** What the link should say, so the destination is never a surprise. */
function targetLabel(notification: NotificationView): string {
  switch (notification.targetType) {
    case 'Conversation':
      return 'Open the conversation';
    case 'FriendRequest':
    case 'Friendship':
      return 'Open friends';
    case 'Profile':
      return 'Open your account';
    default:
      return 'Open the community';
  }
}

/** Human-readable sentence for each notification kind. */
function describe(notification: NotificationView): string {
  const who = notification.actorHandle ?? 'Someone';

  switch (notification.kind) {
    case 'FriendRequest':
      return `${who} sent you a friend request.`;
    case 'FriendAccepted':
      return `${who} accepted your friend request.`;
    case 'ThreadReply':
      return `${who} replied in a thread you follow.`;
    case 'PostReaction':
      return `${who} liked your post.`;
    case 'DirectMessage':
      return `${who} sent you a message.`;
    case 'Moderation':
      return 'A moderator took action on something of yours.';
    default:
      return 'Something happened.';
  }
}

/**
 * The notification inbox.
 *
 * Each entry is a pointer, never content: the API sends a kind and a target, and opening the
 * target re-applies every privacy, friendship, and block rule. A notification therefore cannot
 * become a way to read something you have since lost access to.
 */
@Component({
  selector: 'app-notifications',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header title="Notifications" description="What happened while you were away.">
        <button
          matButton="outlined"
          type="button"
          [disabled]="busy() || notifications().length === 0"
          (click)="markAllRead()"
          data-testid="mark-all-read"
        >
          Mark all read
        </button>
      </app-page-header>

      @if (loading()) {
        <app-loading-state message="Loading notifications…" />
      } @else if (failed()) {
        <app-error-state [message]="errorMessage()" (retry)="load()" />
      } @else if (notifications().length === 0) {
        <app-empty-state
          title="Nothing new"
          message="Replies, reactions, friend requests, and messages show up here."
          data-testid="notifications-empty"
        >
          <a matButton="filled" routerLink="/community">Visit the community</a>
        </app-empty-state>
      } @else {
        <ul class="notifications" data-testid="notification-list">
          @for (notification of notifications(); track notification.id) {
            <li>
              <mat-card appearance="outlined">
                <mat-card-content class="notifications__row">
                  <p
                    class="notifications__text"
                    [class.notifications__text--unread]="!notification.read"
                    [attr.data-testid]="'notification-' + notification.id"
                  >
                    {{ describe(notification) }}
                  </p>

                  @if (route(notification); as target) {
                    <a
                      matButton="outlined"
                      [routerLink]="target"
                      (click)="markRead(notification)"
                      [attr.data-testid]="'open-' + notification.id"
                    >
                      {{ label(notification) }}
                    </a>
                  }

                  @if (!notification.read) {
                    <button
                      matButton
                      type="button"
                      [disabled]="busy()"
                      (click)="markRead(notification)"
                      [attr.data-testid]="'mark-read-' + notification.id"
                    >
                      Mark read
                    </button>
                  }
                </mat-card-content>
              </mat-card>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .notifications {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .notifications__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .notifications__text {
      flex: 1 1 14rem;
      color: var(--dd-on-surface-variant);
    }

    .notifications__text--unread {
      color: var(--dd-on-surface);
      font-weight: var(--dd-weight-medium);
    }
  `,
})
export class Notifications {
  private readonly api = inject(CommunityApi);

  protected readonly describe = describe;
  protected readonly route = targetRoute;
  protected readonly label = targetLabel;
  protected readonly notifications = signal<readonly NotificationView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.api.listNotifications().subscribe({
      next: (page) => {
        this.loading.set(false);
        this.notifications.set(page.items);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.failed.set(true);
        this.errorMessage.set(
          toApiFailure(error, 'We could not load your notifications just now.').message,
        );
      },
    });
  }

  protected markRead(notification: NotificationView): void {
    this.mark(notification.id);
  }

  protected markAllRead(): void {
    this.mark(null);
  }

  private mark(notificationId: string | null): void {
    this.busy.set(true);

    this.api.markNotificationsRead(notificationId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }
}
