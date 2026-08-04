import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import {
  AdminOverview,
  AuditActivityEntry,
  CommunityApi,
} from '../../core/community/community-api';
import { AuthService } from '../../core/auth/auth.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatCard } from '../../shared/ui/stat-card/stat-card';

/** Turns an audit action name into a sentence an operator can scan. */
function describeAction(entry: AuditActivityEntry): string {
  const [area, subject, verb] = entry.action.split('.');
  const readable = (verb ?? entry.action).replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();

  return `${entry.actorDisplayName} ${readable} a ${(subject ?? area).toLowerCase()}`;
}

/**
 * Administrator landing page.
 *
 * Everything an operator needs to decide what to do next: what is waiting to be published,
 * what is on sale, what has been reported, and what was changed recently. The activity strip
 * is the audit trail's safe fields only — action, target, actor, reason — never content.
 *
 * Reaching this route requires the Admin role the API reported. The route guard is convenience
 * only; every call below is authorized again on the server against the local database.
 */
@Component({
  selector: 'app-admin',
  imports: [
    DatePipe,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    PageHeader,
    StatCard,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Administration"
        [description]="'Signed in as ' + (session()?.displayName ?? 'an administrator') + '.'"
      >
        <a matButton="filled" routerLink="/admin/catalog" data-testid="admin-open-catalog">
          Open the catalog
        </a>
      </app-page-header>

      <p class="dd-visually-hidden" data-testid="admin-greeting">
        Signed in as {{ session()?.displayName }} with administrator access.
      </p>

      @if (loading()) {
        <app-loading-state message="Loading the overview…" />
      } @else if (failed()) {
        <app-error-state [message]="errorMessage()" (retry)="load()" />
      } @else if (overview(); as view) {
        <section class="stats" aria-label="Catalog">
          <app-stat-card
            label="Draft courses"
            [value]="view.draftCourses"
            note="Not visible to students"
          />
          <app-stat-card
            label="Ready to publish"
            [value]="view.coursesReadyToPublish"
            note="Drafts that meet the prerequisites"
          />
          <app-stat-card
            label="Published courses"
            [value]="view.publishedCourses"
            note="Live now"
          />
          <app-stat-card label="Archived courses" [value]="view.archivedCourses" note="Withdrawn" />
        </section>

        <section class="stats" aria-label="Pricing and community">
          <app-stat-card label="Active offers" [value]="view.activeOffers" note="Purchasable" />
          <app-stat-card label="Draft offers" [value]="view.draftOffers" note="Not yet on sale" />
          <app-stat-card
            label="Open reports"
            [value]="view.openReports"
            note="Waiting for a moderator"
          />
          <app-stat-card
            label="Under review"
            [value]="view.reviewingReports"
            note="Someone is on it"
          />
        </section>

        <section class="dd-stack" aria-labelledby="admin-links-heading">
          <h2 id="admin-links-heading" class="admin__heading">Where to go</h2>

          <mat-card appearance="outlined">
            <mat-card-content class="links">
              <a routerLink="/admin/catalog" data-testid="link-catalog">
                Catalog — author courses, sections, and lessons
              </a>
              <a routerLink="/admin/pricing" data-testid="link-pricing">
                Pricing — offers and prices held in this database
              </a>
              <a routerLink="/admin/community" data-testid="link-moderation">
                Moderation — {{ view.openReports }} open
                {{ view.openReports === 1 ? 'report' : 'reports' }} and
                {{ view.forumCategories }} forum
                {{ view.forumCategories === 1 ? 'category' : 'categories' }}
              </a>
            </mat-card-content>
          </mat-card>
        </section>

        <section class="dd-stack" aria-labelledby="admin-activity-heading">
          <h2 id="admin-activity-heading" class="admin__heading">Recent administrative activity</h2>

          @if (view.recentActivity.length === 0) {
            <app-empty-state
              title="Nothing yet"
              message="Publishing, pricing, and moderation decisions appear here as they happen."
              data-testid="no-activity"
            />
          } @else {
            <mat-card appearance="outlined">
              <mat-card-content>
                <ol class="activity" data-testid="admin-activity">
                  @for (entry of view.recentActivity; track entry.id) {
                    <li class="activity__item">
                      <span class="activity__what">{{ describe(entry) }}</span>
                      @if (entry.reason) {
                        <span class="activity__reason">“{{ entry.reason }}”</span>
                      }
                      <span class="activity__when">{{ entry.occurredAtUtc | date: 'short' }}</span>
                    </li>
                  }
                </ol>
              </mat-card-content>
            </mat-card>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      gap: var(--dd-space-4);
    }

    .admin__heading {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .links {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
    }

    .activity {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .activity__item {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--dd-space-2);
      padding-bottom: var(--dd-space-3);
      border-bottom: 1px solid var(--dd-outline);
    }

    .activity__item:last-child {
      padding-bottom: 0;
      border-bottom: none;
    }

    .activity__what {
      font-weight: var(--dd-weight-medium);
    }

    .activity__reason,
    .activity__when {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .activity__when {
      margin-left: auto;
    }
  `,
})
export class Admin {
  private readonly auth = inject(AuthService);
  private readonly api = inject(CommunityApi);

  protected readonly describe = describeAction;
  protected readonly session = this.auth.session;

  protected readonly overview = signal<AdminOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly errorMessage = signal('');

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.api.getOverview().subscribe({
      next: (overview) => {
        this.loading.set(false);
        this.overview.set(overview);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.failed.set(true);
        this.errorMessage.set(
          toApiFailure(error, 'We could not load the administration overview.').message,
        );
      },
    });
  }
}
