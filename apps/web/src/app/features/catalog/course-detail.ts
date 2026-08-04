import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { CatalogApi } from '../../core/catalog/catalog-api';
import {
  CourseDetail as CourseDetailModel,
  formatLevel,
  formatPrice,
} from '../../core/catalog/catalog.model';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type DetailState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly course: CourseDetailModel }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error' };

/**
 * Public course detail: description, access options, and the published outline.
 *
 * Purchasing is not implemented in this phase, so the buy actions are visibly disabled and
 * labelled rather than pretending to work.
 */
@Component({
  selector: 'app-course-detail',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatChipsModule,
    MatExpansionModule,
    MatTooltipModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  templateUrl: './course-detail.html',
  styleUrl: './course-detail.scss',
})
export class CourseDetail {
  private readonly api = inject(CatalogApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly formatPrice = formatPrice;
  protected readonly formatLevel = formatLevel;
  protected readonly state = signal<DetailState>({ kind: 'loading' });

  protected slug = '';

  constructor() {
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getCourse(this.slug).subscribe({
      next: (course) => this.state.set({ kind: 'ready', course }),
      // A 404 means the course is not publicly available. The UI says exactly that and
      // nothing about whether it exists in some other state.
      error: (error: unknown) =>
        this.state.set(
          (error as { status?: number } | null)?.status === 404
            ? { kind: 'missing' }
            : { kind: 'error' },
        ),
    });
  }
}
