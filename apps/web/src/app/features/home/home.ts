import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

import { CatalogApi } from '../../core/catalog/catalog-api';
import { CourseCard, formatLevel, formatPrice } from '../../core/catalog/catalog.model';
import { DdIcon } from '../../shared/ui/icon/dd-icon';
import { SystemStatusCard } from '../system-status/system-status-card';

/**
 * Public landing page: what Daniel's Dojo is, a few featured courses pulled from the real
 * catalog, and the Phase 1 system-status card.
 *
 * Featured courses are simply the first page of the catalog, so there is no separate curation
 * concept to keep in sync and the page can never show a course the catalog would not.
 */
@Component({
  selector: 'app-home',
  imports: [RouterLink, MatButtonModule, DdIcon, SystemStatusCard],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly api = inject(CatalogApi);

  protected readonly title = "Daniel's Dojo";
  protected readonly formatPrice = formatPrice;
  protected readonly formatLevel = formatLevel;

  protected readonly featured = signal<readonly CourseCard[]>([]);
  protected readonly loaded = signal(false);

  constructor() {
    this.api.listCourses({ page: 1, pageSize: 3 }).subscribe({
      next: (page) => {
        this.featured.set(page.items);
        this.loaded.set(true);
      },
      // The landing page still renders without the catalog; it simply shows no featured list.
      error: () => this.loaded.set(true),
    });
  }
}
