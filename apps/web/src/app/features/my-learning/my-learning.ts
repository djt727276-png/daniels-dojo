import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { MemberApi, MyCourse } from '../../core/community/member-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type LearningState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly courses: readonly MyCourse[] }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The courses the member is enrolled in.
 *
 * Enrollment is created by purchasing, which is not open yet, so this list is legitimately
 * empty today. The empty state says why and points at the catalog rather than implying
 * something went wrong.
 */
@Component({
  selector: 'app-my-learning',
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
      <app-page-header
        title="My Learning"
        description="Courses you are enrolled in, most recently opened first."
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
              message="You are not enrolled in a course yet. Buying a course or a membership opens in a later release — until then, the catalog and free previews are open to everyone."
              data-testid="learning-empty"
            >
              <a matButton="filled" routerLink="/courses">Browse the catalog</a>
            </app-empty-state>
          } @else {
            <ul class="learning" data-testid="learning-list">
              @for (course of courses(); track course.slug) {
                <li>
                  <mat-card appearance="outlined">
                    <mat-card-content class="learning__body">
                      <h2 class="learning__title">
                        <a [routerLink]="['/courses', course.slug]">{{ course.title }}</a>
                      </h2>
                      <p class="learning__summary">{{ course.summary }}</p>
                    </mat-card-content>
                    <mat-card-actions>
                      <a matButton [routerLink]="['/courses', course.slug]">Open</a>
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
  `,
})
export class MyLearning {
  private readonly api = inject(MemberApi);

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

    this.api.getMyCourses().subscribe({
      next: (courses) => this.state.set({ kind: 'ready', courses }),
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'We could not load your courses just now.').message,
        }),
    });
  }
}
