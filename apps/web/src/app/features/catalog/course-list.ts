import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';

import { CatalogApi } from '../../core/catalog/catalog-api';
import {
  COURSE_LEVELS,
  CourseCard,
  PagedResult,
  formatLevel,
  formatPrice,
} from '../../core/catalog/catalog.model';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type ListState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: PagedResult<CourseCard> }
  | { readonly kind: 'error' };

/**
 * Public course catalog: search, level and tag filters, and paging.
 *
 * Filters live in the URL so a filtered view can be shared and the browser Back button
 * behaves. Every value shown comes from the API — prices in particular are formatted from
 * the returned minor units and currency, never hard-coded.
 */
@Component({
  selector: 'app-course-list',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatPaginatorModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  templateUrl: './course-list.html',
  styleUrl: './course-list.scss',
})
export class CourseList {
  private readonly api = inject(CatalogApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly levels = COURSE_LEVELS;
  protected readonly formatPrice = formatPrice;
  protected readonly formatLevel = formatLevel;

  protected readonly state = signal<ListState>({ kind: 'loading' });

  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    level: new FormControl('', { nonNullable: true }),
    tag: new FormControl('', { nonNullable: true }),
  });

  private page = 1;
  private pageSize = 12;

  constructor() {
    // Seed the controls from the URL so a shared link restores the same view.
    const params = this.route.snapshot.queryParamMap;
    this.filters.setValue({
      search: params.get('search') ?? '',
      level: params.get('level') ?? '',
      tag: params.get('tag') ?? '',
    });
    this.page = Number(params.get('page') ?? 1) || 1;

    this.filters.valueChanges.pipe(debounceTime(300)).subscribe(() => {
      this.page = 1;
      this.applyToUrl();
      this.load();
    });

    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.applyToUrl();
    this.load();
  }

  protected clearFilters(): void {
    this.filters.setValue({ search: '', level: '', tag: '' });
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    const value = this.filters.getRawValue();

    this.api.listCourses({ ...value, page: this.page, pageSize: this.pageSize }).subscribe({
      next: (page) => this.state.set({ kind: 'ready', page }),
      error: () => this.state.set({ kind: 'error' }),
    });
  }

  private applyToUrl(): void {
    const value = this.filters.getRawValue();

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        search: value.search || null,
        level: value.level || null,
        tag: value.tag || null,
        page: this.page > 1 ? this.page : null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
