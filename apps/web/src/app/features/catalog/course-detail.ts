import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { CatalogApi } from '../../core/catalog/catalog-api';
import {
  CourseDetail as CourseDetailModel,
  formatLevel,
  formatPrice,
} from '../../core/catalog/catalog.model';
import {
  BillingApi,
  COMMERCE_ALREADY_OWNED,
  COMMERCE_PROVIDER_DISABLED,
} from '../../core/commerce/billing-api';
import { CourseReviewsSection } from './course-reviews';
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
    PageHeader,
    CourseReviewsSection,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  templateUrl: './course-detail.html',
  styleUrl: './course-detail.scss',
})
export class CourseDetail {
  private readonly api = inject(CatalogApi);
  private readonly billing = inject(BillingApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);

  protected readonly formatPrice = formatPrice;
  protected readonly formatLevel = formatLevel;
  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly shared = signal(false);
  protected readonly buying = signal(false);
  protected readonly purchaseNote = signal<string | null>(null);

  /** Whether a session exists — deciding only which button to show, never what is allowed. */
  protected readonly signedIn = this.auth.session;

  protected slug = '';

  private jsonLd: HTMLScriptElement | null = null;

  constructor() {
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
    this.load();

    inject(DestroyRef).onDestroy(() => this.jsonLd?.remove());
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getCourse(this.slug).subscribe({
      next: (course) => {
        this.state.set({ kind: 'ready', course });
        this.publishJsonLd(course);
      },
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

  /**
   * Starts a hosted checkout and follows its URL. The server decides everything —
   * availability, ownership, provider mode — and its refusals are shown as sentences,
   * not silently swallowed.
   */
  protected buy(offerId: string): void {
    this.buying.set(true);
    this.purchaseNote.set(null);

    this.billing.startCheckout(offerId).subscribe({
      next: (started) => {
        // Absolute (Stripe) or relative (deterministic stand-in) — assign handles both.
        this.document.location.assign(started.checkoutUrl);
      },
      error: (error: unknown) => {
        this.buying.set(false);
        const failure = toApiFailure(error, 'Checkout could not be started just now.');

        this.purchaseNote.set(
          failure.code === COMMERCE_PROVIDER_DISABLED
            ? 'Purchasing is not switched on in this environment yet.'
            : failure.code === COMMERCE_ALREADY_OWNED
              ? 'You already have access to this — see My Learning.'
              : failure.message,
        );
      },
    });
  }

  /** Web Share where the platform offers it; copy-the-link everywhere else. */
  protected share(course: CourseDetailModel): void {
    const url = this.document.location.href;

    if (typeof navigator.share === 'function') {
      void navigator.share({ title: course.title, text: course.summary, url }).catch(() => {
        // Cancelled by the user; nothing to do.
      });
      return;
    }

    void navigator.clipboard.writeText(url).then(() => {
      this.shared.set(true);
      setTimeout(() => this.shared.set(false), 2500);
    });
  }

  /**
   * Structured data for search engines, built only from values already public on this
   * page. Serialized with JSON.stringify into a script element's textContent, so course
   * text can never break out as markup.
   */
  private publishJsonLd(course: CourseDetailModel): void {
    const offers = [course.lifetimePrice, course.membershipPrice]
      .filter((price) => price !== null)
      .map((price) => ({
        '@type': 'Offer',
        price: (price.amountMinor / 100).toFixed(2),
        priceCurrency: price.currency,
      }));

    const payload = {
      '@context': 'https://schema.org',
      '@type': 'Course',
      name: course.title,
      description: course.summary,
      provider: {
        '@type': 'Organization',
        name: "Daniel's Dojo",
        url: this.document.location.origin,
      },
      ...(offers.length > 0 ? { offers } : {}),
    };

    this.jsonLd?.remove();
    this.jsonLd = this.document.createElement('script');
    this.jsonLd.type = 'application/ld+json';
    this.jsonLd.textContent = JSON.stringify(payload);
    this.document.head.appendChild(this.jsonLd);
  }
}
