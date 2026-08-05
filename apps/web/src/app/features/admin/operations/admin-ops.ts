import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../../core/api/problem-details';
import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import { AdminCourseListItem } from '../../../core/admin/admin-catalog.model';
import {
  AdminOperationsApi,
  FeatureFlagView,
  OpsSnapshot,
} from '../../../core/admin/admin-operations-api';
import {
  ConfirmDialog,
  ConfirmDialogResult,
} from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { StatusChip } from '../../../shared/ui/status-chip/status-chip';

/**
 * Operations: what is running, the kill switches, and course announcements.
 *
 * The snapshot is read live — migrations from the database, provider modes from the
 * configuration the process actually loaded — so this page cannot show a hoped-for state.
 * Flags require a recorded reason in both directions.
 */
@Component({
  selector: 'app-admin-ops',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageHeader,
    StatusChip,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Operations"
        description="What is actually running, the platform switches, and announcements."
      >
        <a matButton routerLink="/admin">Back to administration</a>
      </app-page-header>

      @if (failure(); as message) {
        <p class="ops__failure" role="alert">{{ message }}</p>
      }

      @if (ops(); as snapshot) {
        <mat-card appearance="outlined">
          <mat-card-content class="dd-stack">
            <h2 class="ops__heading">Runtime</h2>
            <dl class="ops__facts" data-testid="ops-snapshot">
              <dt>Environment</dt>
              <dd>{{ snapshot.environmentName }}</dd>
              <dt>Version</dt>
              <dd class="ops__code">{{ snapshot.informationalVersion ?? '(not stamped)' }}</dd>
              <dt>Database</dt>
              <dd>
                {{ snapshot.databaseReachable ? 'Reachable' : 'UNREACHABLE' }} — last migration
                <span class="ops__code">{{ snapshot.lastAppliedMigration }}</span>
                @if (snapshot.pendingMigrationCount > 0) {
                  <strong data-testid="pending-migrations">
                    · {{ snapshot.pendingMigrationCount }} pending!
                  </strong>
                }
              </dd>
              <dt>Media storage</dt>
              <dd>{{ snapshot.mediaStorageMode }}</dd>
              <dt>Video provider</dt>
              <dd>{{ snapshot.videoProviderMode }}</dd>
              <dt>Payments</dt>
              <dd>{{ snapshot.paymentProviderMode }}</dd>
            </dl>
          </mat-card-content>
        </mat-card>
      }

      <mat-card appearance="outlined">
        <mat-card-content class="dd-stack">
          <h2 class="ops__heading">Switches</h2>
          <p class="ops__hint">
            Kill switches, not configuration: a switch that has never been touched is on, and
            flipping one takes effect immediately with a recorded reason.
          </p>

          <ul class="ops__flags" data-testid="flag-list">
            @for (flag of flags(); track flag.key) {
              <li class="ops__flag">
                <div class="ops__flag-text">
                  <p class="ops__flag-key">{{ flag.key }}</p>
                  <p class="ops__hint">{{ flag.description }}</p>
                </div>
                <app-status-chip
                  [label]="flag.enabled ? 'On' : 'Off'"
                  [tone]="flag.enabled ? 'success' : 'danger'"
                  srPrefix="Switch"
                />
                <button
                  matButton="outlined"
                  type="button"
                  [disabled]="busy()"
                  (click)="toggle(flag)"
                  [attr.data-testid]="'toggle-flag-' + flag.key"
                >
                  Turn {{ flag.enabled ? 'off' : 'on' }}
                </button>
              </li>
            }
          </ul>
        </mat-card-content>
      </mat-card>

      <mat-card appearance="outlined">
        <mat-card-content class="dd-stack">
          <h2 class="ops__heading">Course announcement</h2>
          <p class="ops__hint">
            Posts a pinned thread in the Announcements category and notifies every member enrolled
            in the course. Plain text, like all forum content.
          </p>

          @if (announced(); as note) {
            <p class="ops__announced" role="status" data-testid="announcement-posted">
              {{ note }}
            </p>
          }

          <form class="dd-stack" [formGroup]="announcement" (ngSubmit)="announce()">
            <mat-form-field appearance="outline">
              <mat-label>Course</mat-label>
              <mat-select formControlName="courseId" data-testid="announce-course">
                @for (course of courses(); track course.id) {
                  <mat-option [value]="course.id">{{ course.title }}</mat-option>
                }
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Title</mat-label>
              <input matInput formControlName="title" data-testid="announce-title" />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Announcement</mat-label>
              <textarea
                matInput
                rows="4"
                formControlName="body"
                data-testid="announce-body"
              ></textarea>
            </mat-form-field>

            <div>
              <button
                matButton="filled"
                type="submit"
                [disabled]="busy() || announcement.invalid"
                data-testid="announce-post"
              >
                Post announcement
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .ops__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .ops__hint {
      color: var(--dd-on-surface-variant);
    }

    .ops__failure {
      padding: var(--dd-space-3) var(--dd-space-4);
      color: var(--dd-danger);
      background: var(--dd-danger-container);
      border-radius: var(--dd-radius-md);
    }

    .ops__facts {
      display: grid;
      grid-template-columns: minmax(8rem, auto) 1fr;
      gap: var(--dd-space-2) var(--dd-space-4);
      margin: 0;

      dt {
        font-weight: var(--dd-weight-medium);
        color: var(--dd-on-surface-variant);
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
    }

    .ops__code {
      font-family: var(--dd-font-mono, monospace);
      font-size: var(--dd-text-sm);
    }

    .ops__flags {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .ops__flag {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .ops__flag-text {
      flex: 1 1 16rem;
      min-width: 0;
    }

    .ops__flag-key {
      font-family: var(--dd-font-mono, monospace);
      font-weight: var(--dd-weight-medium);
    }

    .ops__announced {
      padding: var(--dd-space-3) var(--dd-space-4);
      background: var(--dd-success-container);
      color: var(--dd-success);
      border-radius: var(--dd-radius-md);
    }
  `,
})
export class AdminOps {
  private readonly api = inject(AdminOperationsApi);
  private readonly catalog = inject(AdminCatalogApi);
  private readonly dialog = inject(MatDialog);

  protected readonly ops = signal<OpsSnapshot | null>(null);
  protected readonly flags = signal<readonly FeatureFlagView[]>([]);
  protected readonly courses = signal<readonly AdminCourseListItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly announced = signal<string | null>(null);

  protected readonly announcement = new FormGroup({
    courseId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    body: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.api.getOps().subscribe({
      next: (snapshot) => this.ops.set(snapshot),
      error: (error: unknown) => this.fail(error),
    });
    this.loadFlags();
    this.catalog.listCourses({ pageSize: 50 }).subscribe({
      next: (page) => this.courses.set(page.items),
      error: () => undefined,
    });
  }

  protected toggle(flag: FeatureFlagView): void {
    const turningOff = flag.enabled;

    this.dialog
      .open<ConfirmDialog, unknown, ConfirmDialogResult>(ConfirmDialog, {
        data: {
          title: `Turn ${flag.key} ${turningOff ? 'off' : 'on'}?`,
          message: flag.description,
          confirmLabel: turningOff ? 'Turn off' : 'Turn on',
          destructive: turningOff,
          requireReason: true,
          reasonLabel: 'Reason (recorded)',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.busy.set(true);
        this.api.setFlag(flag.key, !flag.enabled, result.reason).subscribe({
          next: () => {
            this.busy.set(false);
            this.loadFlags();
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.fail(error);
          },
        });
      });
  }

  protected announce(): void {
    if (this.announcement.invalid) {
      this.announcement.markAllAsTouched();
      return;
    }

    const value = this.announcement.getRawValue();

    this.busy.set(true);
    this.failure.set(null);
    this.announced.set(null);

    this.api.postAnnouncement(value.courseId, value.title.trim(), value.body.trim()).subscribe({
      next: (posted) => {
        this.busy.set(false);
        this.announcement.reset();
        this.announced.set(
          `Posted. ${posted.membersNotified} enrolled ` +
            `${posted.membersNotified === 1 ? 'member was' : 'members were'} notified.`,
        );
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.fail(error);
      },
    });
  }

  private loadFlags(): void {
    this.api.listFlags().subscribe({
      next: (flags) => this.flags.set(flags),
      error: (error: unknown) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.failure.set(toApiFailure(error, 'That could not be loaded or saved.').message);
  }
}
