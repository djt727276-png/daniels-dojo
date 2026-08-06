import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';

import { toApiFailure } from '../../../core/api/problem-details';
import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import { AdminCourseListItem } from '../../../core/admin/admin-catalog.model';
import { AdminOperationsApi, AdminUserView } from '../../../core/admin/admin-operations-api';
import { AuthService } from '../../../core/auth/auth.service';
import {
  ConfirmDialog,
  ConfirmDialogResult,
} from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip } from '../../../shared/ui/status-chip/status-chip';

/**
 * Account administration: search, roles, status, and manual course grants.
 *
 * Every action here requires a typed reason that the server records in the audit trail,
 * and the server refuses actions against the caller's own account — this screen merely
 * hides those buttons; the protection is not client-side.
 */
@Component({
  selector: 'app-admin-users',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Members"
        description="Search accounts, manage roles and status, and grant courses. Every action records a reason."
      >
        <a matButton routerLink="/admin">Back to administration</a>
      </app-page-header>

      <mat-form-field appearance="outline" class="users__search">
        <mat-label>Search by name or email</mat-label>
        <input matInput type="search" [formControl]="search" data-testid="user-search" />
      </mat-form-field>

      @if (failure(); as message) {
        <p class="users__failure" role="alert" data-testid="users-error">{{ message }}</p>
      }

      @if (loading()) {
        <app-loading-state message="Loading members…" />
      } @else if (users().length === 0) {
        <app-empty-state
          title="No members found"
          message="Nobody matches that search."
          data-testid="users-empty"
        />
      } @else {
        <ul class="users" data-testid="user-list">
          @for (user of users(); track user.id) {
            <li>
              <mat-card appearance="outlined">
                <mat-card-content class="users__row">
                  <div class="users__identity">
                    <p class="users__name">{{ user.displayName }}</p>
                    <p class="users__email">{{ user.email }}</p>
                    <p class="users__meta">
                      Joined {{ user.createdAtUtc | date: 'mediumDate' }} ·
                      {{ user.entitlementCount }}
                      {{ user.entitlementCount === 1 ? 'entitlement' : 'entitlements' }}
                    </p>
                  </div>

                  <div class="users__chips">
                    <app-status-chip
                      [label]="user.status"
                      [tone]="user.status === 'Active' ? 'success' : 'danger'"
                      srPrefix="Account status"
                    />
                    @for (role of user.roles; track role) {
                      <app-status-chip [label]="role" tone="info" srPrefix="Role" />
                    }
                  </div>

                  @if (user.id !== myUserId()) {
                    <div class="users__actions">
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="toggleAdmin(user)"
                        [attr.data-testid]="'toggle-admin-' + user.id"
                      >
                        {{ user.roles.includes('Admin') ? 'Remove Admin' : 'Make Admin' }}
                      </button>
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="toggleStatus(user)"
                        [attr.data-testid]="'toggle-status-' + user.id"
                      >
                        {{ user.status === 'Active' ? 'Disable account' : 'Re-enable account' }}
                      </button>
                      <button
                        matButton
                        type="button"
                        [disabled]="busy()"
                        (click)="startGrant(user)"
                        [attr.data-testid]="'grant-' + user.id"
                      >
                        Grant a course
                      </button>
                    </div>
                  } @else {
                    <p class="users__meta">This is you. Another administrator manages you.</p>
                  }

                  @if (granting() === user.id) {
                    <div class="users__grant">
                      <mat-form-field appearance="outline" class="users__grant-field">
                        <mat-label>Course</mat-label>
                        <mat-select [formControl]="grantCourseId" data-testid="grant-course">
                          @for (course of courses(); track course.id) {
                            <mat-option [value]="course.id">{{ course.title }}</mat-option>
                          }
                        </mat-select>
                      </mat-form-field>
                      <mat-form-field appearance="outline" class="users__grant-field">
                        <mat-label>Reason (recorded)</mat-label>
                        <input matInput [formControl]="grantReason" data-testid="grant-reason" />
                      </mat-form-field>
                      <button
                        matButton="filled"
                        type="button"
                        [disabled]="busy() || !grantCourseId.value || !grantReason.value"
                        (click)="confirmGrant(user)"
                        data-testid="grant-confirm"
                      >
                        Grant
                      </button>
                      <button matButton type="button" (click)="granting.set(null)">Cancel</button>
                    </div>
                  }
                </mat-card-content>
              </mat-card>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .users__search {
      max-width: 24rem;
    }

    .users__failure {
      padding: var(--dd-space-3) var(--dd-space-4);
      color: var(--dd-danger);
      background: var(--dd-danger-container);
      border-radius: var(--dd-radius-md);
    }

    .users {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .users__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-4);
    }

    .users__identity {
      flex: 1 1 16rem;
      min-width: 0;
    }

    .users__name {
      font-weight: var(--dd-weight-medium);
    }

    .users__email,
    .users__meta {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .users__chips,
    .users__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
    }

    .users__grant {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--dd-space-3);
      width: 100%;
    }

    .users__grant-field {
      flex: 1 1 14rem;
    }
  `,
})
export class AdminUsers {
  private readonly api = inject(AdminOperationsApi);
  private readonly catalog = inject(AdminCatalogApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  protected readonly users = signal<readonly AdminUserView[]>([]);
  protected readonly courses = signal<readonly AdminCourseListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly granting = signal<string | null>(null);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly grantCourseId = new FormControl('', { nonNullable: true });
  protected readonly grantReason = new FormControl('', { nonNullable: true });

  protected myUserId(): string | null {
    return this.auth.session()?.userId ?? null;
  }

  constructor() {
    this.load('');
    this.search.valueChanges.pipe(debounceTime(300)).subscribe((term) => this.load(term));
    this.catalog.listCourses({ pageSize: 50 }).subscribe({
      next: (page) => this.courses.set(page.items),
      error: () => undefined,
    });
  }

  protected load(term: string): void {
    this.loading.set(true);

    this.api.searchUsers(term.trim()).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.users.set(page.items);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.failure.set(toApiFailure(error, 'Members could not be loaded.').message);
      },
    });
  }

  protected toggleAdmin(user: AdminUserView): void {
    const makeAdmin = !user.roles.includes('Admin');

    this.withReason(
      makeAdmin ? `Grant Admin to ${user.displayName}?` : `Remove Admin from ${user.displayName}?`,
      makeAdmin
        ? 'They will hold every operator permission. The grant is recorded.'
        : 'They will lose operator access immediately. The removal is recorded.',
      (reason) => this.run(this.api.setAdminRole(user.id, makeAdmin, reason)),
    );
  }

  protected toggleStatus(user: AdminUserView): void {
    const disable = user.status === 'Active';

    this.withReason(
      disable ? `Disable ${user.displayName}?` : `Re-enable ${user.displayName}?`,
      disable
        ? 'They will be refused at sign-in until re-enabled. History is retained.'
        : 'They will be able to sign in again.',
      (reason) =>
        this.run(this.api.setUserStatus(user.id, disable ? 'Disabled' : 'Active', reason)),
    );
  }

  protected startGrant(user: AdminUserView): void {
    this.granting.set(user.id);
    this.grantCourseId.setValue('');
    this.grantReason.setValue('');
  }

  protected confirmGrant(user: AdminUserView): void {
    this.run(
      this.api.grantCourse(user.id, this.grantCourseId.value, this.grantReason.value.trim()),
      () => this.granting.set(null),
    );
  }

  private withReason(title: string, message: string, act: (reason: string) => void): void {
    this.dialog
      .open<ConfirmDialog, unknown, ConfirmDialogResult>(ConfirmDialog, {
        data: {
          title,
          message,
          confirmLabel: 'Confirm',
          requireReason: true,
          reasonLabel: 'Reason (recorded in the audit trail)',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          act(result.reason);
        }
      });
  }

  private run(
    request: ReturnType<AdminOperationsApi['setAdminRole']>,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);
    this.failure.set(null);

    request.subscribe({
      next: (updated) => {
        this.busy.set(false);
        onSuccess?.();
        this.users.update((current) =>
          current.map((user) => (user.id === updated.id ? updated : user)),
        );
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(toApiFailure(error, 'That change was not applied.').message);
      },
    });
  }
}
