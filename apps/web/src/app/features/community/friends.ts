import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import {
  BlockView,
  CommunityApi,
  FriendRequestView,
  FriendView,
} from '../../core/community/community-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

/** Friends, pending requests in both directions, and the members this account has blocked. */
@Component({
  selector: 'app-friends',
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
      <app-page-header title="Friends" description="Your connections, requests, and blocks.">
        <a matButton="filled" routerLink="/people">Find members</a>
      </app-page-header>

      @if (message(); as note) {
        <p class="friends__message" role="alert" data-testid="friends-message">{{ note }}</p>
      }

      @if (loading()) {
        <app-loading-state message="Loading your connections…" />
      } @else if (failed()) {
        <app-error-state message="We could not load your connections just now." (retry)="load()" />
      } @else {
        <section class="dd-stack" aria-labelledby="requests-heading">
          <h2 id="requests-heading" class="friends__heading">Requests</h2>

          @if (requests().length === 0) {
            <p class="friends__empty" data-testid="no-requests">No requests waiting.</p>
          } @else {
            <ul class="friends__list" data-testid="request-list">
              @for (request of requests(); track request.id) {
                <li class="friends__row">
                  <span class="friends__handle">{{ request.otherHandle }}</span>
                  <span class="friends__note">
                    {{ request.incoming ? 'wants to connect' : 'request sent' }}
                  </span>

                  <span class="friends__actions">
                    @if (request.incoming) {
                      <button
                        matButton="filled"
                        type="button"
                        [disabled]="busy()"
                        (click)="respond(request, 'accept')"
                        [attr.data-testid]="'accept-' + request.otherHandle"
                      >
                        Accept
                      </button>
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="respond(request, 'decline')"
                      >
                        Decline
                      </button>
                    } @else {
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="respond(request, 'cancel')"
                      >
                        Cancel
                      </button>
                    }
                  </span>
                </li>
              }
            </ul>
          }
        </section>

        <section class="dd-stack" aria-labelledby="friends-heading">
          <h2 id="friends-heading" class="friends__heading">Friends</h2>

          @if (friends().length === 0) {
            <app-empty-state
              title="No friends yet"
              message="Find members who have made themselves discoverable, then send a request."
              data-testid="no-friends"
            />
          } @else {
            <ul class="friends__list" data-testid="friend-list">
              @for (friend of friends(); track friend.userId) {
                <li class="friends__row">
                  <span class="friends__handle">{{ friend.handle }}</span>
                  <span class="friends__actions">
                    <button
                      matButton
                      type="button"
                      [disabled]="busy()"
                      (click)="remove(friend)"
                      [attr.data-testid]="'unfriend-' + friend.handle"
                    >
                      Remove
                    </button>
                  </span>
                </li>
              }
            </ul>
          }
        </section>

        <section class="dd-stack" aria-labelledby="blocks-heading">
          <h2 id="blocks-heading" class="friends__heading">Blocked</h2>

          @if (blocks().length === 0) {
            <p class="friends__empty" data-testid="no-blocks">You have not blocked anyone.</p>
          } @else {
            <ul class="friends__list" data-testid="block-list">
              @for (block of blocks(); track block.userId) {
                <li class="friends__row">
                  <span class="friends__handle">{{ block.handle }}</span>
                  <span class="friends__actions">
                    <button
                      matButton
                      type="button"
                      [disabled]="busy()"
                      (click)="unblock(block)"
                      [attr.data-testid]="'unblock-' + block.handle"
                    >
                      Unblock
                    </button>
                  </span>
                </li>
              }
            </ul>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .friends__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .friends__list {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .friends__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
      padding: var(--dd-space-3);
      background: var(--dd-surface);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-md);
    }

    .friends__handle {
      flex: 1 1 10rem;
      font-weight: var(--dd-weight-medium);
    }

    .friends__note,
    .friends__empty {
      color: var(--dd-on-surface-variant);
    }

    .friends__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-2);
    }

    .friends__message {
      color: var(--dd-danger);
    }
  `,
})
export class Friends {
  private readonly api = inject(CommunityApi);

  protected readonly friends = signal<readonly FriendView[]>([]);
  protected readonly requests = signal<readonly FriendRequestView[]>([]);
  protected readonly blocks = signal<readonly BlockView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);

    forkJoin({
      friends: this.api.listFriends(),
      requests: this.api.listFriendRequests(),
      blocks: this.api.listBlocks(),
    }).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.friends.set(result.friends);
        this.requests.set(result.requests);
        this.blocks.set(result.blocks);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  protected respond(request: FriendRequestView, action: 'accept' | 'decline' | 'cancel'): void {
    this.act(this.api.respondToFriendRequest(request.id, action));
  }

  protected remove(friend: FriendView): void {
    this.act(this.api.removeFriend(friend.userId));
  }

  protected unblock(block: BlockView): void {
    this.act(this.api.unblock(block.userId));
  }

  private act(request: Observable<void>): void {
    this.busy.set(true);
    this.message.set(null);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.message.set(toApiFailure(error, 'That could not be completed.').message);
      },
    });
  }
}
