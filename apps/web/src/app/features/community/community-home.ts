import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import {
  CommunityApi,
  ForumCategorySummary,
  ForumThreadSummary,
} from '../../core/community/community-api';
import { CommunityStatus, MemberApi } from '../../core/community/member-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatusChip } from '../../shared/ui/status-chip/status-chip';

interface CommunityHomeView {
  readonly status: CommunityStatus;
  readonly categories: readonly ForumCategorySummary[];
  readonly recent: readonly ForumThreadSummary[];
}

type HomeState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly view: CommunityHomeView }
  | { readonly kind: 'error'; readonly message: string };

/**
 * Community landing page: the forum categories, plus an honest banner when the member cannot
 * take part yet.
 *
 * Reading is open to any signed-in member. Posting requires a completed profile, so the page
 * says so up front rather than letting someone write a reply and then refusing it.
 */
@Component({
  selector: 'app-community-home',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Community"
        description="Ask questions, share what you are building, and help other members."
      />

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading the community…" />
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (view(); as current) {
            @if (!current.status.granted) {
              <mat-card appearance="outlined">
                <mat-card-content class="dd-stack">
                  <p data-testid="participation-notice">{{ current.status.message }}</p>

                  @if (!current.status.profileExists) {
                    <div>
                      <a matButton="filled" routerLink="/community/setup" data-testid="setup-link">
                        Set up your profile
                      </a>
                    </div>
                  }
                </mat-card-content>
              </mat-card>
            }

            @if (current.categories.length === 0) {
              <app-empty-state
                title="No categories yet"
                message="An administrator has not opened a discussion area yet."
                data-testid="no-categories"
              />
            } @else {
              <h2 class="community__heading">Categories</h2>
              <ul class="categories" data-testid="category-list">
                @for (category of current.categories; track category.slug) {
                  <li>
                    <mat-card appearance="outlined">
                      <mat-card-content>
                        <h2 class="categories__title">
                          <a
                            [routerLink]="['/community/c', category.slug]"
                            [attr.data-testid]="'category-' + category.slug"
                          >
                            {{ category.name }}
                          </a>
                        </h2>
                        <p class="categories__description">{{ category.description }}</p>
                        <p class="categories__meta">
                          {{ category.threadCount }}
                          {{ category.threadCount === 1 ? 'thread' : 'threads' }}
                        </p>
                      </mat-card-content>
                    </mat-card>
                  </li>
                }
              </ul>

              <section class="dd-stack" aria-labelledby="recent-heading">
                <h2 id="recent-heading" class="community__heading">Recent discussion</h2>

                @if (current.recent.length === 0) {
                  <p class="community__muted" data-testid="no-recent-threads">
                    No threads yet. The first one can be yours.
                  </p>
                } @else {
                  <ul class="recent" data-testid="recent-threads">
                    @for (thread of current.recent; track thread.id) {
                      <li class="recent__item">
                        <a
                          class="recent__title"
                          [routerLink]="['/community/t', thread.id]"
                          [attr.data-testid]="'recent-' + thread.id"
                        >
                          {{ thread.title }}
                        </a>

                        @if (thread.isPinned) {
                          <app-status-chip label="Pinned" tone="info" srPrefix="Thread" />
                        }

                        @if (thread.status !== 'Open') {
                          <app-status-chip
                            [label]="thread.status"
                            tone="neutral"
                            srPrefix="Thread status"
                          />
                        }

                        <span class="community__muted">
                          {{ thread.authorHandle }} in {{ thread.categorySlug }} ·
                          {{ thread.replyCount }}
                          {{ thread.replyCount === 1 ? 'reply' : 'replies' }}
                        </span>
                      </li>
                    }
                  </ul>
                }
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .categories {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
      gap: var(--dd-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .categories__title {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .categories__description {
      color: var(--dd-on-surface-variant);
    }

    .categories__meta {
      margin-top: var(--dd-space-2);
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .community__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .community__muted {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .recent {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .recent__item {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
      padding: var(--dd-space-3);
      background: var(--dd-surface);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-md);
    }

    .recent__title {
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }
  `,
})
export class CommunityHome {
  private readonly api = inject(CommunityApi);
  private readonly members = inject(MemberApi);

  protected readonly state = signal<HomeState>({ kind: 'loading' });

  protected readonly view = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.view : null;
  });

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    forkJoin({
      status: this.members.getCommunityStatus(),
      categories: this.api.listCategories(),
      recent: this.api.listRecentThreads(6),
    }).subscribe({
      next: (view) => this.state.set({ kind: 'ready', view }),
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'We could not load the community just now.').message,
        }),
    });
  }
}
