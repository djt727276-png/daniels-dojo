import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { LearningApi, MyLearningCourse } from '../../core/learning/learning-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type LearningState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly courses: readonly MyLearningCourse[] }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The learner's shelf.
 *
 * Everything shown here is resolved server-side from entitlements at read time — the shelf is
 * a projection of what the member actually holds, so a lapsed membership empties it without
 * any client-side bookkeeping. "Continue" deep-links to the lesson the server picked.
 */
@Component({
  selector: 'app-my-learning',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatProgressBarModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="My Learning"
        description="Courses you hold, with your progress. Pick up where you left off."
      />

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading your courses…" />
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (courses().length === 0) {
            <app-empty-state
              title="Nothing here yet"
              message="Courses appear here once a membership or purchase grants them. The catalog and free previews are open to everyone."
              data-testid="learning-empty"
            >
              <a matButton="filled" routerLink="/courses">Browse the catalog</a>
            </app-empty-state>
          } @else {
            <ul class="learning" data-testid="learning-list">
              @for (course of courses(); track course.courseId) {
                <li>
                  <mat-card appearance="outlined">
                    <mat-card-content class="learning__body">
                      <h2 class="learning__title">
                        <a [routerLink]="['/courses', course.slug]">{{ course.title }}</a>
                      </h2>
                      <p class="learning__summary">{{ course.summary }}</p>

                      <div class="learning__progress">
                        <mat-progress-bar
                          mode="determinate"
                          [value]="course.percentComplete"
                          [attr.aria-label]="'Progress in ' + course.title"
                        />
                        <span class="learning__count">
                          {{ course.completedLessons }} of {{ course.totalLessons }} lessons
                          @if (course.percentComplete === 100) {
                            — complete
                          }
                        </span>
                      </div>
                    </mat-card-content>
                    <mat-card-actions>
                      @if (course.resumeLessonId; as resume) {
                        <a matButton="filled" [routerLink]="['/learn/lessons', resume]">
                          {{ course.completedLessons === 0 ? 'Start' : 'Continue' }}
                        </a>
                      }
                      <a matButton [routerLink]="['/courses', course.slug]">Outline</a>
                    </mat-card-actions>
                  </mat-card>
                </li>
              }
            </ul>
          }
        }
      }
    </div>
  `,
  styles: `
    .learning {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
      gap: var(--dd-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .learning__title {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .learning__summary {
      color: var(--dd-on-surface-variant);
    }

    .learning__progress {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
      margin-top: var(--dd-space-4);
    }

    .learning__count {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class MyLearning {
  private readonly api = inject(LearningApi);

  protected readonly state = signal<LearningState>({ kind: 'loading' });

  protected readonly courses = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.courses : [];
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

    this.api.getMyLearning().subscribe({
      next: (courses) => this.state.set({ kind: 'ready', courses }),
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'We could not load your courses just now.').message,
        }),
    });
  }
}
