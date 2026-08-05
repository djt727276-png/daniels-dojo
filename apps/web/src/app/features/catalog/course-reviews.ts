import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, Injectable, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Observable } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import { API_BASE_PATH } from '../../core/configuration/app-config';

/** One published review. */
export interface ReviewView {
  readonly id: string;
  readonly reviewerName: string;
  readonly rating: number;
  readonly body: string;
  readonly createdAtUtc: string;
  readonly editedAtUtc: string | null;
  readonly isMine: boolean;
}

/** A course's review page with its honest aggregate. */
export interface CourseReviews {
  readonly averageRating: number | null;
  readonly reviewCount: number;
  readonly reviews: readonly ReviewView[];
  readonly totalCount: number;
  readonly myReview: ReviewView | null;
  readonly canReview: boolean;
}

@Injectable({ providedIn: 'root' })
export class ReviewsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);

  forCourse(slug: string, page = 0): Observable<CourseReviews> {
    return this.http.get<CourseReviews>(
      `${this.base}/v1/catalog/courses/${slug}/reviews?page=${page}`,
    );
  }

  write(slug: string, rating: number, body: string): Observable<ReviewView> {
    return this.http.put<ReviewView>(`${this.base}/v1/learning/courses/${slug}/review`, {
      rating,
      body,
    });
  }

  remove(slug: string): Observable<unknown> {
    return this.http.delete(`${this.base}/v1/learning/courses/${slug}/review`);
  }
}

/**
 * The reviews block on a course page.
 *
 * The aggregate is exactly what the server computed from published reviews — no stars are
 * painted that a stored number could contradict. Whether the visitor may write one is also
 * the server's answer: entitlement plus at least one completed lesson, decided in one place.
 */
@Component({
  selector: 'app-course-reviews',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <section class="reviews" aria-labelledby="reviews-heading">
      <h2 id="reviews-heading" class="reviews__heading">
        Reviews
        @if (data(); as view) {
          @if (view.reviewCount > 0) {
            <span class="reviews__aggregate" data-testid="review-aggregate">
              <span aria-hidden="true">★</span>
              {{ view.averageRating }} · {{ view.reviewCount }}
              {{ view.reviewCount === 1 ? 'review' : 'reviews' }}
            </span>
          }
        }
      </h2>

      @if (failure(); as message) {
        <p class="reviews__failure" role="alert">{{ message }}</p>
      }

      @if (data(); as view) {
        @if (view.canReview || editing()) {
          <mat-card appearance="outlined">
            <mat-card-content>
              <form class="reviews__form" [formGroup]="form" (ngSubmit)="submit()">
                <mat-form-field class="reviews__rating">
                  <mat-label>Rating</mat-label>
                  <mat-select formControlName="rating" required>
                    @for (stars of [5, 4, 3, 2, 1]; track stars) {
                      <mat-option [value]="stars">
                        {{ '★'.repeat(stars) }} ({{ stars }})
                      </mat-option>
                    }
                  </mat-select>
                </mat-form-field>

                <mat-form-field class="reviews__body-field">
                  <mat-label>Your review</mat-label>
                  <textarea
                    matInput
                    formControlName="body"
                    rows="4"
                    maxlength="4000"
                    required
                    placeholder="What did you build? What surprised you?"
                  ></textarea>
                </mat-form-field>

                <div class="reviews__form-actions">
                  <button matButton="filled" type="submit" [disabled]="form.invalid || busy()">
                    {{ editing() ? 'Update review' : 'Publish review' }}
                  </button>
                  @if (editing()) {
                    <button matButton type="button" (click)="cancelEdit()">Cancel</button>
                  }
                </div>
              </form>
            </mat-card-content>
          </mat-card>
        }

        @if (view.myReview; as mine) {
          @if (!editing()) {
            <mat-card appearance="outlined" class="reviews__mine">
              <mat-card-content>
                <p class="reviews__meta">
                  <strong>Your review</strong> · {{ '★'.repeat(mine.rating) }}
                  @if (mine.editedAtUtc) {
                    · edited
                  }
                </p>
                <p class="reviews__text">{{ mine.body }}</p>
                <div class="reviews__form-actions">
                  <button matButton type="button" (click)="startEdit(mine)">Edit</button>
                  <button matButton type="button" (click)="remove()">Delete</button>
                </div>
              </mat-card-content>
            </mat-card>
          }
        }

        @if (view.reviews.length === 0 && !view.myReview) {
          <p class="reviews__empty">
            No reviews yet. Reviews come from members who hold the course and have completed at
            least one lesson.
          </p>
        } @else {
          <ul class="reviews__list">
            @for (review of view.reviews; track review.id) {
              @if (!review.isMine) {
                <li class="reviews__item">
                  <p class="reviews__meta">
                    <strong>{{ review.reviewerName }}</strong> ·
                    <span [attr.aria-label]="review.rating + ' out of 5 stars'">
                      {{ '★'.repeat(review.rating) }}
                    </span>
                    · {{ review.createdAtUtc | date: 'mediumDate' }}
                    @if (review.editedAtUtc) {
                      · edited
                    }
                  </p>
                  <p class="reviews__text">{{ review.body }}</p>
                </li>
              }
            }
          </ul>

          @if (view.totalCount > view.reviews.length) {
            <button matButton type="button" (click)="loadMore()">Show more reviews</button>
          }
        }
      }
    </section>
  `,
  styles: `
    .reviews {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-4);
      margin-top: var(--dd-space-6);
    }

    .reviews__heading {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
      align-items: baseline;
      font-size: var(--dd-text-xl);
      font-weight: var(--dd-weight-medium);
    }

    .reviews__aggregate {
      font-size: var(--dd-text-base);
      color: var(--dd-accent);
      font-weight: var(--dd-weight-medium);
    }

    .reviews__failure {
      padding: var(--dd-space-3) var(--dd-space-4);
      color: var(--dd-danger);
      background: var(--dd-danger-container);
      border-radius: var(--dd-radius-md);
    }

    .reviews__form {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
    }

    .reviews__form-actions {
      display: flex;
      gap: var(--dd-space-3);
    }

    .reviews__mine {
      border-left: 4px solid var(--dd-primary);
    }

    .reviews__empty {
      color: var(--dd-on-surface-variant);
    }

    .reviews__list {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .reviews__item {
      padding-bottom: var(--dd-space-4);
      border-bottom: 1px solid var(--dd-outline);
    }

    .reviews__meta {
      margin-bottom: var(--dd-space-2);
      color: var(--dd-on-surface-variant);
    }

    .reviews__text {
      max-width: var(--dd-reading-max);
      overflow-wrap: anywhere;
    }
  `,
})
export class CourseReviewsSection {
  private readonly api = inject(ReviewsApi);

  /** Course slug for reading. */
  readonly slug = input.required<string>();

  protected readonly data = signal<CourseReviews | null>(null);
  protected readonly failure = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly editing = signal(false);
  protected readonly page = signal(0);

  protected readonly form = new FormGroup({
    rating: new FormControl<number | null>(null, {
      validators: [Validators.required],
    }),
    body: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(4000)],
    }),
  });

  protected readonly hasData = computed(() => this.data() !== null);

  constructor() {
    // input() values are unavailable until the first change detection; defer the load.
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.api.forCourse(this.slug(), 0).subscribe({
      next: (data) => {
        this.page.set(0);
        this.data.set(data);
      },
      error: () => this.data.set(null),
    });
  }

  protected loadMore(): void {
    const next = this.page() + 1;

    this.api.forCourse(this.slug(), next).subscribe({
      next: (more) => {
        const current = this.data();

        if (current) {
          this.page.set(next);
          this.data.set({ ...current, reviews: [...current.reviews, ...more.reviews] });
        }
      },
      error: () => undefined,
    });
  }

  protected startEdit(mine: ReviewView): void {
    this.editing.set(true);
    this.form.setValue({ rating: mine.rating, body: mine.body });
  }

  protected cancelEdit(): void {
    this.editing.set(false);
    this.form.reset();
  }

  protected submit(): void {
    const { rating, body } = this.form.getRawValue();

    if (rating === null || !body) {
      return;
    }

    this.busy.set(true);
    this.failure.set(null);

    this.api.write(this.slug(), rating, body).subscribe({
      next: () => {
        this.busy.set(false);
        this.editing.set(false);
        this.form.reset();
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(toApiFailure(error).message);
      },
    });
  }

  protected remove(): void {
    this.api.remove(this.slug()).subscribe({
      next: () => this.load(),
      error: (error: unknown) => this.failure.set(toApiFailure(error).message),
    });
  }
}
