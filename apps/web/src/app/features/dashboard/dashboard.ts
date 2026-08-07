import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { Dashboard as DashboardModel, MemberApi } from '../../core/community/member-api';
import { LearningApi, MyLearningCourse } from '../../core/learning/learning-api';
import { DdIcon } from '../../shared/ui/icon/dd-icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatCard } from '../../shared/ui/stat-card/stat-card';

type DashboardState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly dashboard: DashboardModel }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The signed-in member's landing page.
 *
 * Every figure is a real count from the API. Enrollment and purchasing arrive in a later
 * phase, so the page says that plainly instead of showing a progress bar that means nothing.
 */
@Component({
  selector: 'app-dashboard',
  imports: [
    RouterLink,
    MatCardModule,
    MatProgressBarModule,
    MatButtonModule,
    DdIcon,
    PageHeader,
    StatCard,
    LoadingState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading your dashboard…" />
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (dashboard(); as view) {
            <app-page-header
              [title]="'Welcome back, ' + view.displayName"
              description="Everything you are learning and taking part in, in one place."
            >
              <a matButton="filled" routerLink="/courses">Browse courses</a>
            </app-page-header>

            @if (continueCourse(); as next) {
              <section class="dd-card dashboard__continue" aria-label="Continue training">
                <div class="dashboard__continue-text">
                  <p class="dd-label dashboard__continue-label">Continue training</p>
                  <h2 class="dd-h2">{{ next.title }}</h2>
                  <p class="dashboard__note">
                    {{ next.completedLessons }} of {{ next.totalLessons }} lessons complete
                  </p>
                  <div class="dashboard__progress-row">
                    <mat-progress-bar
                      mode="determinate"
                      [value]="next.percentComplete"
                      [attr.aria-label]="'Progress in ' + next.title"
                    />
                    <span class="dashboard__percent">{{ next.percentComplete }}%</span>
                  </div>
                </div>
                <div class="dashboard__continue-actions">
                  @if (next.resumeLessonId; as resume) {
                    <a matButton="filled" [routerLink]="['/learn/lessons', resume]">
                      <span class="dashboard__cta-line">
                        <dd-icon name="play" [size]="18" />
                        {{ next.completedLessons === 0 ? 'Start lesson' : 'Continue lesson' }}
                      </span>
                    </a>
                  }
                  <a matButton routerLink="/my-learning">My Learning</a>
                </div>
              </section>
            }

            <section class="stats" aria-label="Your activity">
              <app-stat-card
                label="Your courses"
                [value]="view.enrolledCourseCount"
                note="Held through membership or purchase"
              />
              <app-stat-card
                label="Courses available"
                [value]="view.publishedCourseCount"
                note="Published in the catalog"
              />
              <app-stat-card label="Unread notifications" [value]="view.unreadNotificationCount" />
              <app-stat-card
                label="Friend requests"
                [value]="view.pendingFriendRequestCount"
                note="Waiting for your answer"
              />
            </section>

            @if (!view.purchasingAvailable) {
              <mat-card appearance="outlined">
                <mat-card-content>
                  <h2 class="dashboard__heading">Purchasing is not enabled here yet</h2>
                  <p class="dashboard__note" data-testid="purchasing-note">
                    You can read the catalog, watch free previews, and take part in the community.
                    Nothing in this environment charges you.
                  </p>
                </mat-card-content>
              </mat-card>
            }

            @if (!view.community.granted) {
              <mat-card appearance="outlined">
                <mat-card-content class="dd-stack">
                  <h2 class="dashboard__heading">Community</h2>
                  <p class="dashboard__note" data-testid="community-blocked-note">
                    {{ view.community.message }}
                  </p>

                  @if (!view.community.profileExists) {
                    <div>
                      <a
                        matButton="filled"
                        routerLink="/community/setup"
                        data-testid="start-community-setup"
                      >
                        Set up your profile
                      </a>
                    </div>
                  }
                </mat-card-content>
              </mat-card>
            } @else {
              <mat-card appearance="outlined">
                <mat-card-content class="dd-stack">
                  <h2 class="dashboard__heading">Community</h2>
                  <p class="dashboard__note">
                    You are taking part as
                    <strong data-testid="community-handle">{{ view.community.handle }}</strong
                    >.
                  </p>
                  <div class="dashboard__links">
                    <a matButton routerLink="/community">Forums</a>
                    <a matButton routerLink="/messages">
                      Messages ({{ view.unreadConversationCount }})
                    </a>
                    <a matButton routerLink="/notifications">Notifications</a>
                  </div>
                </mat-card-content>
              </mat-card>
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .dashboard__continue {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-5);
      align-items: center;
      justify-content: space-between;

      /* The member's own momentum earns the gold accent and a faint indigo field. */
      border-left: 4px solid var(--dd-accent);
      background:
        radial-gradient(
          120% 160% at 90% 0%,
          color-mix(in srgb, var(--dd-primary) 14%, transparent),
          transparent 60%
        ),
        var(--dd-surface);
    }

    .dashboard__continue-text {
      display: flex;
      flex: 1 1 18rem;
      flex-direction: column;
      gap: var(--dd-space-2);
    }

    .dashboard__continue-label {
      color: var(--dd-accent);
    }

    .dashboard__progress-row {
      display: flex;
      align-items: center;
      gap: var(--dd-space-3);
      margin-top: var(--dd-space-2);
    }

    .dashboard__progress-row mat-progress-bar {
      flex: 1;
    }

    .dashboard__percent {
      font-weight: var(--dd-weight-bold);
      font-variant-numeric: tabular-nums;
    }

    .dashboard__cta-line {
      display: inline-flex;
      align-items: center;
      gap: var(--dd-space-2);
    }

    .dashboard__continue-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
    }

    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      gap: var(--dd-space-4);
    }

    .dashboard__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
      margin-bottom: var(--dd-space-2);
    }

    .dashboard__note {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }

    .dashboard__links {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-2);
    }
  `,
})
export class Dashboard {
  private readonly api = inject(MemberApi);
  private readonly learning = inject(LearningApi);

  protected readonly state = signal<DashboardState>({ kind: 'loading' });

  /**
   * The most recently opened course the member holds, for the continue card. The server
   * orders the shelf by last access, so the first entry is where they left off.
   */
  protected readonly continueCourse = signal<MyLearningCourse | null>(null);

  protected readonly dashboard = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.dashboard : null;
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

    // Best effort: an empty or failed shelf simply hides the continue card. The dashboard
    // itself never fails because of it.
    this.learning.getMyLearning().subscribe({
      next: (courses) => this.continueCourse.set(courses[0] ?? null),
      error: () => this.continueCourse.set(null),
    });

    this.api.getDashboard().subscribe({
      next: (dashboard) => this.state.set({ kind: 'ready', dashboard }),
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'We could not load your dashboard just now.').message,
        }),
    });
  }
}
