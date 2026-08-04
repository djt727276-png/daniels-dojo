import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { PagedResult } from '../../core/catalog/catalog.model';
import { CommunityApi, ForumThreadSummary } from '../../core/community/community-api';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatusChip } from '../../shared/ui/status-chip/status-chip';

type ThreadsState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: PagedResult<ForumThreadSummary> }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error'; readonly message: string };

/** Threads in one category, with the form that starts a new one. */
@Component({
  selector: 'app-category-threads',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
    FormErrorSummary,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header [title]="categorySlug" description="Threads in this category.">
        <a matButton routerLink="/community">All categories</a>
      </app-page-header>

      <app-form-error-summary [errors]="errors()" />

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading threads…" />
        }

        @case ('missing') {
          <app-empty-state
            title="Category not found"
            message="This category is not open."
            data-testid="category-missing"
          >
            <a matButton="filled" routerLink="/community">Back to the community</a>
          </app-empty-state>
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (threads().length === 0) {
            <app-empty-state
              title="No threads yet"
              message="Be the first to start a discussion here."
              data-testid="no-threads"
            />
          } @else {
            <ul class="threads" data-testid="thread-list">
              @for (thread of threads(); track thread.id) {
                <li>
                  <mat-card appearance="outlined">
                    <mat-card-content class="threads__row">
                      <div class="threads__identity">
                        <h2 class="threads__title">
                          <a
                            [routerLink]="['/community/t', thread.id]"
                            [attr.data-testid]="'thread-' + thread.id"
                          >
                            {{ thread.title }}
                          </a>
                        </h2>
                        <p class="threads__meta">
                          {{ thread.authorHandle }} · {{ thread.replyCount }}
                          {{ thread.replyCount === 1 ? 'reply' : 'replies' }}
                        </p>
                      </div>

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
                    </mat-card-content>
                  </mat-card>
                </li>
              }
            </ul>
          }
        }
      }

      <mat-card appearance="outlined">
        <mat-card-content>
          <h2 class="threads__heading">Start a thread</h2>

          <form class="dd-stack" [formGroup]="form" (ngSubmit)="createThread()">
            <mat-form-field appearance="outline">
              <mat-label>Title</mat-label>
              <input matInput id="field-title" formControlName="title" data-testid="thread-title" />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Your post</mat-label>
              <textarea
                matInput
                id="field-body"
                rows="5"
                formControlName="body"
                data-testid="thread-body"
              ></textarea>
              <mat-hint>Plain text. Formatting and links are not rendered.</mat-hint>
            </mat-form-field>

            <div>
              <button
                matButton="filled"
                type="submit"
                [disabled]="saving()"
                data-testid="create-thread"
              >
                Post thread
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .threads {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .threads__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .threads__identity {
      flex: 1 1 14rem;
      min-width: 0;
    }

    .threads__title {
      font-size: var(--dd-text-base);
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }

    .threads__meta {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .threads__heading {
      margin-bottom: var(--dd-space-3);
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }
  `,
})
export class CategoryThreads {
  private readonly api = inject(CommunityApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly categorySlug = this.route.snapshot.paramMap.get('categorySlug') ?? '';
  protected readonly state = signal<ThreadsState>({ kind: 'loading' });
  protected readonly saving = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);

  protected readonly threads = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.page.items : [];
  });

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    body: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.listThreads(this.categorySlug).subscribe({
      next: (page) => this.state.set({ kind: 'ready', page }),
      error: (error: unknown) => {
        const failure = toApiFailure(error, 'We could not load this category.');
        this.state.set(
          failure.status === 404
            ? { kind: 'missing' }
            : { kind: 'error', message: failure.message },
        );
      },
    });
  }

  protected createThread(): void {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      this.errors.set([{ field: 'title', message: 'Add a title and a first post.' }]);
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);
    this.errors.set([]);

    this.api.createThread(this.categorySlug, value.title.trim(), value.body.trim()).subscribe({
      next: (thread) => {
        this.saving.set(false);
        void this.router.navigate(['/community/t', thread.id]);
      },
      error: (error: unknown) => {
        this.saving.set(false);

        const failure = toApiFailure(error, 'Your thread could not be posted.');
        this.errors.set(
          failure.fieldErrors.length > 0
            ? failure.fieldErrors
            : [{ field: 'title', message: failure.message }],
        );
      },
    });
  }
}
