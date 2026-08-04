import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { toApiFailure } from '../../../core/api/problem-details';
import {
  AdminForumCategory,
  CommunityApi,
  ModerationReport,
  ModerationTarget,
} from '../../../core/community/community-api';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip, StatusTone } from '../../../shared/ui/status-chip/status-chip';

/** Maps a report status to a chip tone. */
function reportTone(status: string): StatusTone {
  switch (status) {
    case 'Open':
      return 'warning';
    case 'Reviewing':
      return 'info';
    case 'Resolved':
      return 'success';
    default:
      return 'neutral';
  }
}

/**
 * The moderation queue.
 *
 * Every decision asks for a reason, which is recorded in the audit trail alongside the actor
 * and the target. There is no bulk action and no way to browse conversations: review is scoped
 * to what somebody actually reported.
 */
@Component({
  selector: 'app-admin-moderation',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Moderation"
        description="Reports members have filed, newest first. Every decision is recorded."
      />

      <h2 class="moderation__heading">Report queue</h2>

      <mat-form-field appearance="outline" class="moderation__filter">
        <mat-label>Status</mat-label>
        <mat-select [formControl]="status" data-testid="report-status-filter">
          <mat-option value="Open">Open</mat-option>
          <mat-option value="Reviewing">Reviewing</mat-option>
          <mat-option value="Resolved">Resolved</mat-option>
          <mat-option value="Dismissed">Dismissed</mat-option>
          <mat-option value="">All</mat-option>
        </mat-select>
      </mat-form-field>

      @if (message(); as note) {
        <p class="moderation__message" role="alert" data-testid="moderation-message">{{ note }}</p>
      }

      @if (loading()) {
        <app-loading-state message="Loading reports…" />
      } @else if (failed()) {
        <app-error-state message="We could not load the moderation queue." (retry)="load()" />
      } @else if (reports().length === 0) {
        <app-empty-state
          title="Nothing to review"
          message="No reports match this filter."
          data-testid="moderation-empty"
        />
      } @else {
        <ul class="moderation" data-testid="report-list">
          @for (report of reports(); track report.id) {
            <li>
              <mat-card appearance="outlined">
                <mat-card-content class="dd-stack">
                  <div class="moderation__row">
                    <div class="moderation__identity">
                      <h2 class="moderation__title">
                        {{ report.targetType }} · {{ report.reasonCode }}
                      </h2>
                      <p class="moderation__meta">Reported by {{ report.reporterHandle }}</p>
                    </div>

                    <app-status-chip
                      [label]="report.status"
                      [tone]="tone(report.status)"
                      srPrefix="Report status"
                    />
                  </div>

                  @if (report.detail) {
                    <p class="moderation__detail">{{ report.detail }}</p>
                  }

                  @if (openTarget()?.reportId === report.id) {
                    <div class="moderation__target" [attr.data-testid]="'target-' + report.id">
                      <p class="moderation__meta">
                        {{ openTarget()!.authorHandle }} · {{ openTarget()!.status }}
                        @if (openTarget()!.context) {
                          · {{ openTarget()!.context }}
                        }
                      </p>

                      <!--
                        Reported text is bound as text inside a preformatted block, exactly like
                        every other member-authored body. Reviewing something abusive must not
                        also mean running it.
                      -->
                      <pre class="moderation__content">{{ openTarget()!.content }}</pre>
                    </div>
                  }

                  @if (report.resolution) {
                    <p class="moderation__meta">Outcome: {{ report.resolution }}</p>
                  }

                  @if (report.status === 'Open' || report.status === 'Reviewing') {
                    <div class="moderation__actions">
                      @if (report.status === 'Open') {
                        <button
                          matButton="outlined"
                          type="button"
                          [disabled]="busy()"
                          (click)="decide(report, 'Reviewing')"
                          [attr.data-testid]="'review-' + report.id"
                        >
                          Start reviewing
                        </button>
                      }
                      <button
                        matButton="filled"
                        type="button"
                        [disabled]="busy()"
                        (click)="decide(report, 'Resolved')"
                        [attr.data-testid]="'resolve-' + report.id"
                      >
                        Resolve
                      </button>
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="decide(report, 'Dismissed')"
                        [attr.data-testid]="'dismiss-' + report.id"
                      >
                        Dismiss
                      </button>

                      <button
                        matButton="outlined"
                        type="button"
                        [disabled]="busy()"
                        (click)="toggleTarget(report)"
                        [attr.data-testid]="'view-target-' + report.id"
                      >
                        {{
                          openTarget()?.reportId === report.id
                            ? 'Hide the report'
                            : 'View what was reported'
                        }}
                      </button>

                      @if (report.targetType === 'Post') {
                        <button
                          matButton="outlined"
                          type="button"
                          [disabled]="busy()"
                          (click)="removePost(report)"
                          [attr.data-testid]="'remove-post-' + report.id"
                        >
                          Remove the post
                        </button>
                      }
                    </div>
                  }
                </mat-card-content>
              </mat-card>
            </li>
          }
        </ul>
      }

      <!-- ------------------------------------------------------------ categories -->
      <section class="dd-stack" aria-labelledby="categories-heading">
        <h2 id="categories-heading" class="moderation__heading">Forum categories</h2>
        <p class="moderation__meta">
          Archiving a category hides it from members. Every thread inside it is kept.
        </p>

        @if (categories().length > 0) {
          <ul class="moderation" data-testid="category-list">
            @for (category of categories(); track category.id) {
              <li>
                <mat-card appearance="outlined">
                  <mat-card-content class="moderation__row">
                    <div class="moderation__identity">
                      <h3 class="moderation__title">{{ category.name }}</h3>
                      <p class="moderation__meta">
                        /{{ category.slug }} · position {{ category.sortOrder }} ·
                        {{ category.threadCount }}
                        {{ category.threadCount === 1 ? 'thread' : 'threads' }}
                      </p>
                    </div>

                    <app-status-chip
                      [label]="category.status"
                      [tone]="category.status === 'Active' ? 'success' : 'neutral'"
                      srPrefix="Category status"
                    />

                    <button
                      matButton="outlined"
                      type="button"
                      [disabled]="busy()"
                      (click)="toggleCategory(category)"
                      [attr.data-testid]="'category-toggle-' + category.slug"
                    >
                      {{ category.status === 'Active' ? 'Archive' : 'Reactivate' }}
                    </button>
                  </mat-card-content>
                </mat-card>
              </li>
            }
          </ul>
        }

        <mat-card appearance="outlined">
          <mat-card-content>
            <h3 class="moderation__title">New category</h3>

            <form class="moderation__form" [formGroup]="categoryForm" (ngSubmit)="createCategory()">
              <mat-form-field appearance="outline" class="moderation__field">
                <mat-label>Name</mat-label>
                <input matInput formControlName="name" data-testid="category-name" />
              </mat-form-field>

              <mat-form-field appearance="outline" class="moderation__field">
                <mat-label>Slug</mat-label>
                <input matInput formControlName="slug" data-testid="category-slug" />
                <mat-hint>Lowercase letters, numbers, and single hyphens.</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline" class="moderation__field">
                <mat-label>Position</mat-label>
                <input matInput type="number" min="0" formControlName="sortOrder" />
              </mat-form-field>

              <mat-form-field appearance="outline" class="moderation__field--wide">
                <mat-label>Description</mat-label>
                <input matInput formControlName="description" data-testid="category-description" />
              </mat-form-field>

              <button
                matButton="filled"
                type="submit"
                [disabled]="busy()"
                data-testid="create-category"
              >
                Create category
              </button>
            </form>
          </mat-card-content>
        </mat-card>
      </section>
    </div>
  `,
  styles: `
    .moderation__filter {
      max-width: 16rem;
    }

    .moderation {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .moderation__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .moderation__identity {
      flex: 1 1 14rem;
      min-width: 0;
    }

    .moderation__title {
      font-size: var(--dd-text-base);
      font-weight: var(--dd-weight-medium);
    }

    .moderation__meta,
    .moderation__detail {
      color: var(--dd-on-surface-variant);
    }

    .moderation__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-2);
    }

    .moderation__message {
      color: var(--dd-danger);
    }

    .moderation__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .moderation__form {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: var(--dd-space-3);
      margin-top: var(--dd-space-3);
    }

    .moderation__field {
      flex: 1 1 12rem;
    }

    .moderation__field--wide {
      flex: 1 1 100%;
    }

    .moderation__target {
      padding: var(--dd-space-3);
      background: var(--dd-surface-variant);
      border-radius: var(--dd-radius-md);
    }

    .moderation__content {
      max-width: var(--dd-reading-max);
      margin: var(--dd-space-2) 0 0;
      font-family: var(--dd-font-sans);
      font-size: var(--dd-text-base);
      line-height: var(--dd-leading-base);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
  `,
})
export class AdminModeration {
  private readonly api = inject(CommunityApi);
  private readonly dialog = inject(MatDialog);

  protected readonly tone = reportTone;
  protected readonly status = new FormControl('Open', { nonNullable: true });
  protected readonly reports = signal<readonly ModerationReport[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  /**
   * The one reported item currently open for review.
   *
   * Only one at a time, and only while its report is still open: reading someone's reported
   * private message is a deliberate act, and the server records each one.
   */
  protected readonly openTarget = signal<ModerationTarget | null>(null);

  /** Every category, including archived ones, which members never see. */
  protected readonly categories = signal<readonly AdminForumCategory[]>([]);

  protected readonly categoryForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    slug: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    sortOrder: new FormControl(0, { nonNullable: true }),
  });

  constructor() {
    this.status.valueChanges.subscribe(() => this.load());
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.api.listReports(this.status.value).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.reports.set(page.items);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });

    this.api.listAdminCategories().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.categories.set([]),
    });
  }

  protected createCategory(): void {
    this.categoryForm.markAllAsTouched();

    if (this.categoryForm.invalid) {
      this.message.set('Give the category a name, a slug, and a description.');
      return;
    }

    const value = this.categoryForm.getRawValue();
    this.busy.set(true);
    this.message.set(null);

    this.api
      .createCategory(
        value.slug.trim(),
        value.name.trim(),
        value.description.trim(),
        value.sortOrder,
      )
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.categoryForm.reset({ name: '', slug: '', description: '', sortOrder: 0 });
          this.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.message.set(toApiFailure(error, 'The category could not be created.').message);
        },
      });
  }

  protected toggleCategory(category: AdminForumCategory): void {
    const target = category.status === 'Active' ? 'Archived' : 'Active';

    this.askReason(
      target === 'Archived' ? `Archive "${category.name}"?` : `Reactivate "${category.name}"?`,
      (reason) => {
        this.busy.set(true);
        this.message.set(null);

        this.api.setCategoryStatus(category.id, target, reason, category.rowVersion).subscribe({
          next: () => {
            this.busy.set(false);
            this.load();
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.message.set(toApiFailure(error, 'The category was not changed.').message);
          },
        });
      },
    );
  }

  protected toggleTarget(report: ModerationReport): void {
    if (this.openTarget()?.reportId === report.id) {
      this.openTarget.set(null);
      return;
    }

    this.busy.set(true);
    this.message.set(null);

    this.api.getReportTarget(report.id).subscribe({
      next: (target) => {
        this.busy.set(false);
        this.openTarget.set(target);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.openTarget.set(null);
        this.message.set(
          toApiFailure(error, 'That report no longer has content to review.').message,
        );
      },
    });
  }

  protected decide(report: ModerationReport, targetStatus: string): void {
    this.askReason(`Move this report to ${targetStatus}?`, (reason) => {
      this.busy.set(true);
      this.message.set(null);

      this.api.decideReport(report.id, targetStatus, reason, report.rowVersion).subscribe({
        next: () => {
          this.busy.set(false);

          // A decided report locks its content again, so the open panel is closed with it.
          this.openTarget.set(null);
          this.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.message.set(toApiFailure(error, 'The decision was not saved.').message);
        },
      });
    });
  }

  protected removePost(report: ModerationReport): void {
    this.askReason('Remove the reported post?', (reason) => {
      this.busy.set(true);
      this.message.set(null);

      this.api.removePostAsModerator(report.targetId, reason).subscribe({
        next: () => {
          this.busy.set(false);
          this.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.message.set(toApiFailure(error, 'The post was not removed.').message);
        },
      });
    });
  }

  private askReason(title: string, onConfirmed: (reason: string) => void): void {
    this.dialog
      .open(ConfirmDialog, {
        data: {
          title,
          message: 'Your reason is recorded in the audit trail alongside your name.',
          confirmLabel: 'Confirm',
          requireReason: true,
          reasonLabel: 'Reason',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          onConfirmed(result.reason);
        }
      });
  }
}
