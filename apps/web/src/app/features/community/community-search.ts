import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { CommunityApi, ForumSearchResult } from '../../core/community/community-api';
import { PagedResult } from '../../core/catalog/catalog.model';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatusChip } from '../../shared/ui/status-chip/status-chip';

type SearchState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly results: PagedResult<ForumSearchResult> }
  | { readonly kind: 'error'; readonly message: string };

/**
 * Discussion search.
 *
 * The server decides what matches and what may be excerpted — a snippet is only ever the
 * text of a still-published post whose author the reader has not blocked. Snippets render
 * through text bindings, so stored text cannot become markup here.
 */
@Component({
  selector: 'app-community-search',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header title="Search discussions" description="Find threads by title or reply.">
        <a matButton routerLink="/community">Back to the community</a>
      </app-page-header>

      <form class="search" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" class="search__field">
          <mat-label>Search</mat-label>
          <input
            matInput
            type="search"
            [formControl]="query"
            maxlength="100"
            placeholder="e.g. lighting, first commission, layers"
            data-testid="search-input"
          />
        </mat-form-field>
        <button matButton="filled" type="submit" data-testid="search-submit">Search</button>
      </form>

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Searching…" />
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="submit()" />
        }

        @case ('ready') {
          @if (results(); as page) {
            @if (page.items.length === 0) {
              <app-empty-state
                title="No matches"
                message="Nothing matched that search. Try fewer or different words."
                data-testid="search-empty"
              />
            } @else {
              <p class="search__count" data-testid="search-count">
                {{ page.totalCount }} {{ page.totalCount === 1 ? 'result' : 'results' }}
              </p>

              <ul class="results" data-testid="search-results">
                @for (result of page.items; track result.threadId) {
                  <li class="results__item">
                    <div class="results__head">
                      <a
                        class="results__title"
                        [routerLink]="['/community/t', result.threadId]"
                        [attr.data-testid]="'result-' + result.threadId"
                      >
                        {{ result.title }}
                      </a>

                      @if (result.isSolved) {
                        <app-status-chip label="Solved" tone="success" srPrefix="Thread" />
                      }

                      @if (result.status !== 'Open') {
                        <app-status-chip
                          [label]="result.status"
                          tone="neutral"
                          srPrefix="Thread status"
                        />
                      }
                    </div>

                    @if (result.snippet; as snippet) {
                      <p class="results__snippet">{{ snippet }}</p>
                    }

                    <p class="results__meta">
                      {{ result.categoryName }} ·
                      {{ result.lastActivityAtUtc | date: 'mediumDate' }}
                    </p>
                  </li>
                }
              </ul>

              @if (page.totalPages > page.page) {
                <div>
                  <button matButton type="button" (click)="loadMore()" data-testid="search-more">
                    Show more results
                  </button>
                </div>
              }
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .search {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
      align-items: baseline;
    }

    .search__field {
      flex: 1 1 18rem;
      max-width: 32rem;
    }

    .search__count {
      color: var(--dd-on-surface-variant);
    }

    .results {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .results__item {
      padding: var(--dd-space-3) var(--dd-space-4);
      background: var(--dd-surface);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-md);
    }

    .results__head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
    }

    .results__title {
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }

    .results__snippet {
      margin-top: var(--dd-space-2);
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .results__meta {
      margin-top: var(--dd-space-2);
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class CommunitySearch {
  private readonly api = inject(CommunityApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly state = signal<SearchState>({ kind: 'idle' });
  protected readonly page = signal(1);

  protected readonly query = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(2), Validators.maxLength(100)],
  });

  constructor() {
    const initial = this.route.snapshot.queryParamMap.get('q');

    if (initial && initial.trim().length >= 2) {
      this.query.setValue(initial);
      this.run(initial.trim(), 1);
    }
  }

  protected results(): PagedResult<ForumSearchResult> | null {
    const current = this.state();
    return current.kind === 'ready' ? current.results : null;
  }

  protected errorMessage(): string {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  }

  protected submit(): void {
    if (this.query.invalid) {
      this.query.markAsTouched();
      return;
    }

    const text = this.query.value.trim();

    // The query lives in the URL so a search can be shared or revisited.
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: text },
      replaceUrl: true,
    });

    this.run(text, 1);
  }

  protected loadMore(): void {
    const current = this.results();

    if (current) {
      this.run(this.query.value.trim(), current.page + 1, current);
    }
  }

  private run(text: string, page: number, previous?: PagedResult<ForumSearchResult>): void {
    if (!previous) {
      this.state.set({ kind: 'loading' });
    }

    this.api.search(text, page).subscribe({
      next: (results) => {
        this.page.set(page);
        this.state.set({
          kind: 'ready',
          results: previous
            ? { ...results, items: [...previous.items, ...results.items] }
            : results,
        });
      },
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'The search could not run just now.').message,
        }),
    });
  }
}
