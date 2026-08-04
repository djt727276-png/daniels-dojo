import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { debounceTime } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import { CommunityApi, MemberCard } from '../../core/community/community-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, LoadingState } from '../../shared/ui/state-views/state-views';

/**
 * Finding other members.
 *
 * Only members who deliberately turned discovery on can be found here, so an empty result is
 * the normal outcome rather than a bug. Handles are the only identifier the client ever uses.
 */
@Component({
  selector: 'app-people',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeader,
    LoadingState,
    EmptyState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="People"
        description="Search for members who have chosen to be discoverable."
      />

      <mat-form-field appearance="outline" class="people__search">
        <mat-label>Search by handle</mat-label>
        <input matInput type="search" [formControl]="search" data-testid="people-search" />
        <mat-hint>Type at least two characters.</mat-hint>
      </mat-form-field>

      @if (message(); as note) {
        <p class="people__message" role="alert" data-testid="people-message">{{ note }}</p>
      }

      @if (searching()) {
        <app-loading-state message="Searching…" />
      } @else if (searched() && results().length === 0) {
        <app-empty-state
          title="No members found"
          message="Nobody matching that handle has made their profile discoverable."
          data-testid="people-empty"
        />
      } @else {
        <ul class="people" data-testid="people-results">
          @for (member of results(); track member.userId) {
            <li>
              <mat-card appearance="outlined">
                <mat-card-content class="dd-stack">
                  <h2 class="people__handle">{{ member.handle }}</h2>
                  @if (member.bio) {
                    <p class="people__bio">{{ member.bio }}</p>
                  }

                  <div class="people__actions">
                    @if (member.isFriend) {
                      <span class="people__status" [attr.data-testid]="'friend-' + member.handle">
                        Already friends
                      </span>
                    } @else if (member.requestPending) {
                      <span class="people__status">Request pending</span>
                    } @else if (member.canReceiveFriendRequests) {
                      <button
                        matButton="filled"
                        type="button"
                        [disabled]="busy()"
                        (click)="sendRequest(member)"
                        [attr.data-testid]="'add-' + member.handle"
                      >
                        Send friend request
                      </button>
                    } @else {
                      <span class="people__status" [attr.data-testid]="'closed-' + member.handle">
                        Not accepting friend requests
                      </span>
                    }

                    <button
                      matButton
                      type="button"
                      [disabled]="busy()"
                      (click)="block(member)"
                      [attr.data-testid]="'block-' + member.handle"
                    >
                      Block
                    </button>
                  </div>
                </mat-card-content>
              </mat-card>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .people__search {
      max-width: 24rem;
    }

    .people {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: var(--dd-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .people__handle {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .people__bio,
    .people__status {
      color: var(--dd-on-surface-variant);
    }

    .people__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
    }

    .people__message {
      color: var(--dd-danger);
    }
  `,
})
export class People {
  private readonly api = inject(CommunityApi);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly results = signal<readonly MemberCard[]>([]);
  protected readonly searching = signal(false);
  protected readonly searched = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  constructor() {
    this.search.valueChanges.pipe(debounceTime(300)).subscribe((term) => this.run(term));
  }

  private run(term: string): void {
    const trimmed = term.trim();

    if (trimmed.length < 2) {
      this.results.set([]);
      this.searched.set(false);
      return;
    }

    this.searching.set(true);
    this.message.set(null);

    this.api.searchMembers(trimmed).subscribe({
      next: (results) => {
        this.searching.set(false);
        this.searched.set(true);
        this.results.set(results);
      },
      error: (error: unknown) => {
        this.searching.set(false);
        this.searched.set(true);
        this.results.set([]);
        this.message.set(toApiFailure(error, 'We could not search just now.').message);
      },
    });
  }

  protected sendRequest(member: MemberCard): void {
    this.busy.set(true);
    this.message.set(null);

    this.api.sendFriendRequest(member.handle).subscribe({
      next: () => {
        this.busy.set(false);
        this.run(this.search.value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.message.set(toApiFailure(error, 'The request could not be sent.').message);
      },
    });
  }

  protected block(member: MemberCard): void {
    this.busy.set(true);
    this.message.set(null);

    this.api.block(member.handle, 'Personal').subscribe({
      next: () => {
        this.busy.set(false);
        this.run(this.search.value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.message.set(toApiFailure(error, 'The block could not be applied.').message);
      },
    });
  }
}
