import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';

import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import { AdminCourseListItem, formatLevel } from '../../../core/admin/admin-catalog.model';
import { toApiFailure } from '../../../core/api/problem-details';
import { PagedResult } from '../../../core/catalog/catalog.model';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip, publicationTone } from '../../../shared/ui/status-chip/status-chip';

type ListState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: PagedResult<AdminCourseListItem> }
  | { readonly kind: 'error'; readonly message: string };

/**
 * Every course in the catalog, in every status.
 *
 * Deliberately distinct from the public list, which shows Published rows only. Filters live
 * in the URL so an operator can share "all archived courses" as a link and the browser Back
 * button returns to the same view.
 */
@Component({
  selector: 'app-admin-course-list',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Catalog"
        description="Author courses, sections, and lessons. Nothing reaches students until it is published."
      >
        <a matButton="filled" routerLink="/admin/catalog/courses/new" data-testid="new-course">
          New course
        </a>
      </app-page-header>

      <form class="filters" [formGroup]="filters" (ngSubmit)="$event.preventDefault()">
        <mat-form-field appearance="outline" class="filters__field">
          <mat-label>Search</mat-label>
          <input matInput formControlName="search" type="search" data-testid="admin-search" />
        </mat-form-field>

        <mat-form-field appearance="outline" class="filters__field">
          <mat-label>Status</mat-label>
          <mat-select formControlName="status" data-testid="admin-status-filter">
            <mat-option value="">All statuses</mat-option>
            <mat-option value="Draft">Draft</mat-option>
            <mat-option value="Published">Published</mat-option>
            <mat-option value="Archived">Archived</mat-option>
          </mat-select>
        </mat-form-field>
      </form>

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading courses…" />
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (courses().length === 0) {
            <app-empty-state
              title="No courses match"
              message="Adjust the filters, or create the first course."
              data-testid="admin-courses-empty"
            >
              <a matButton="filled" routerLink="/admin/catalog/courses/new">New course</a>
            </app-empty-state>
          } @else {
            <mat-card appearance="outlined">
              <table mat-table [dataSource]="courses()" data-testid="admin-course-table">
                <ng-container matColumnDef="title">
                  <th mat-header-cell *matHeaderCellDef>Course</th>
                  <td mat-cell *matCellDef="let course">
                    <a
                      class="course-link"
                      [routerLink]="['/admin/catalog/courses', course.id]"
                      [attr.data-testid]="'admin-course-' + course.slug"
                    >
                      {{ course.title }}
                    </a>
                    <span class="course-slug">{{ course.slug }}</span>
                  </td>
                </ng-container>

                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef>Status</th>
                  <td mat-cell *matCellDef="let course">
                    <app-status-chip [label]="course.status" [tone]="tone(course.status)" />
                  </td>
                </ng-container>

                <ng-container matColumnDef="level">
                  <th mat-header-cell *matHeaderCellDef>Level</th>
                  <td mat-cell *matCellDef="let course">{{ level(course.level) }}</td>
                </ng-container>

                <ng-container matColumnDef="outline">
                  <th mat-header-cell *matHeaderCellDef>Outline</th>
                  <td mat-cell *matCellDef="let course">
                    {{ course.sectionCount }} sections · {{ course.lessonCount }} lessons
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="columns"></tr>
                <tr mat-row *matRowDef="let row; columns: columns"></tr>
              </table>
            </mat-card>

            <mat-paginator
              [length]="total()"
              [pageSize]="pageSize()"
              [pageIndex]="pageIndex()"
              [pageSizeOptions]="[10, 20, 50]"
              (page)="onPage($event)"
              aria-label="Course pages"
            />
          }
        }
      }
    </div>
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-4);
    }

    .filters__field {
      flex: 1 1 16rem;
    }

    .course-link {
      display: block;
      font-weight: var(--dd-weight-medium);
    }

    .course-slug {
      display: block;
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    table {
      width: 100%;
    }
  `,
})
export class AdminCourseList {
  private readonly api = inject(AdminCatalogApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly columns = ['title', 'status', 'level', 'outline'];
  protected readonly state = signal<ListState>({ kind: 'loading' });
  protected readonly tone = publicationTone;
  protected readonly level = formatLevel;

  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    status: new FormControl('', { nonNullable: true }),
  });

  protected readonly courses = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.page.items : [];
  });

  protected readonly total = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.page.totalCount : 0;
  });

  protected readonly pageSize = signal(20);
  protected readonly pageIndex = signal(0);

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.filters.setValue(
      {
        search: params.get('search') ?? '',
        status: params.get('status') ?? '',
      },
      { emitEvent: false },
    );
    this.pageIndex.set(Math.max(0, Number(params.get('page') ?? '1') - 1));

    this.filters.valueChanges.pipe(debounceTime(250)).subscribe(() => {
      this.pageIndex.set(0);
      this.syncUrl();
      this.load();
    });

    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api
      .listCourses({
        search: this.filters.controls.search.value.trim(),
        status: this.filters.controls.status.value,
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (page) => this.state.set({ kind: 'ready', page }),
        error: (error: unknown) =>
          this.state.set({
            kind: 'error',
            message: toApiFailure(error, 'We could not load the catalog just now.').message,
          }),
      });
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.syncUrl();
    this.load();
  }

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        search: this.filters.controls.search.value || null,
        status: this.filters.controls.status.value || null,
        page: this.pageIndex() === 0 ? null : this.pageIndex() + 1,
      },
      replaceUrl: true,
    });
  }
}
