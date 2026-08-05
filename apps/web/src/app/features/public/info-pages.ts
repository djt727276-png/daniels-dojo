import { Component, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import { CatalogApi } from '../../core/catalog/catalog-api';
import { PublicPrice, formatPrice } from '../../core/catalog/catalog.model';
import {
  BillingApi,
  COMMERCE_ALREADY_OWNED,
  COMMERCE_PROVIDER_DISABLED,
} from '../../core/commerce/billing-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ProsePage } from './prose-page';

/**
 * Pricing.
 *
 * The membership price is the live published one, fetched from the same API that checkout
 * charges from, so this page can never drift from what Stripe charges. Each course page
 * shows its own exact lifetime price the same way.
 */
@Component({
  selector: 'app-pricing-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, PageHeader],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Pricing"
        description="Two honest ways in: everything while you subscribe, or own a course outright."
      />

      <div class="pricing">
        <mat-card appearance="outlined" class="pricing__card">
          <mat-card-header>
            <mat-card-title>Membership</mat-card-title>
            <mat-card-subtitle>All membership courses while active</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <p class="pricing__price">
              @if (membership(); as price) {
                <span class="pricing__amount" data-testid="membership-price">
                  {{ formatPrice(price) }}
                </span>
              } @else {
                <span class="pricing__interval" data-testid="membership-unpublished">
                  Membership pricing is being prepared.
                </span>
              }
            </p>
            <ul class="pricing__list">
              <li>Every membership-included course</li>
              <li>New courses as they publish</li>
              <li>Progress, resume, and certificates</li>
              <li>Full community access</li>
              <li>Cancel anytime — access runs to the end of the paid period</li>
            </ul>

            @if (purchaseNote(); as note) {
              <p class="pricing__note" role="status" data-testid="pricing-purchase-note">
                {{ note }}
              </p>
            }
          </mat-card-content>
          <mat-card-actions>
            @if (membership(); as price) {
              @if (signedIn()) {
                <button
                  matButton="filled"
                  type="button"
                  [disabled]="buying()"
                  (click)="startMembership(price.offerId)"
                  data-testid="start-membership"
                >
                  {{ buying() ? 'Starting checkout…' : 'Start membership' }}
                </button>
              } @else {
                <a matButton="filled" routerLink="/account" data-testid="start-membership">
                  Sign in to join
                </a>
              }
            }
            <a matButton routerLink="/courses">Browse what's included</a>
          </mat-card-actions>
        </mat-card>

        <mat-card appearance="outlined" class="pricing__card">
          <mat-card-header>
            <mat-card-title>Own a course</mat-card-title>
            <mat-card-subtitle>Lifetime access, one payment</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <p class="pricing__price">
              <span class="pricing__interval">One payment — exact price on each course page</span>
            </p>
            <ul class="pricing__list">
              <li>That course, forever — no subscription needed</li>
              <li>Keeps working if your membership lapses</li>
              <li>All lesson resources and future updates to the course</li>
              <li>Exact price shown on each course page</li>
            </ul>
          </mat-card-content>
          <mat-card-actions>
            <a matButton routerLink="/courses">Find your course</a>
          </mat-card-actions>
        </mat-card>
      </div>

      <p class="pricing__note">
        Free preview lessons are open on published courses — try before anything is charged.
        Payments are processed by Stripe; see the
        <a routerLink="/legal/refunds">refund policy</a>.
      </p>
    </div>
  `,
  styles: `
    .pricing {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(17rem, 1fr));
      gap: var(--dd-space-5);
    }

    .pricing__price {
      margin: var(--dd-space-3) 0;
    }

    .pricing__amount {
      font-size: var(--dd-text-3xl);
      font-weight: var(--dd-weight-bold);
      color: var(--dd-primary);
    }

    .pricing__interval {
      margin-left: var(--dd-space-2);
      color: var(--dd-on-surface-variant);
    }

    .pricing__list {
      padding-left: var(--dd-space-5);

      li {
        margin-bottom: var(--dd-space-2);
      }
    }

    .pricing__note {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class PricingPage {
  private readonly catalog = inject(CatalogApi);
  private readonly billing = inject(BillingApi);
  private readonly auth = inject(AuthService);
  private readonly document = inject(DOCUMENT);

  protected readonly formatPrice = formatPrice;
  protected readonly membership = signal<PublicPrice | null>(null);
  protected readonly buying = signal(false);
  protected readonly purchaseNote = signal<string | null>(null);

  /** Whether a session exists — deciding which button shows, never what is allowed. */
  protected readonly signedIn = this.auth.session;

  constructor() {
    this.catalog.getMembershipPrice().subscribe({
      next: (price) => this.membership.set(price),
      // 404 simply means no live price; the template already says so.
      error: () => this.membership.set(null),
    });
  }

  protected startMembership(offerId: string): void {
    this.buying.set(true);
    this.purchaseNote.set(null);

    this.billing.startCheckout(offerId).subscribe({
      next: (started) => this.document.location.assign(started.checkoutUrl),
      error: (error: unknown) => {
        this.buying.set(false);
        const failure = toApiFailure(error, 'Checkout could not be started just now.');

        this.purchaseNote.set(
          failure.code === COMMERCE_PROVIDER_DISABLED
            ? 'Purchasing is not switched on in this environment yet.'
            : failure.code === COMMERCE_ALREADY_OWNED
              ? 'You already have an active membership — see your account page.'
              : failure.message,
        );
      },
    });
  }
}

/** Frequently asked questions, answered from how the platform actually behaves. */
@Component({
  selector: 'app-faq-page',
  imports: [MatExpansionModule, PageHeader],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header title="Frequently asked questions" description="" />

      <mat-accordion class="faq" multi>
        @for (entry of faqs; track entry.q) {
          <mat-expansion-panel>
            <mat-expansion-panel-header>
              <mat-panel-title>{{ entry.q }}</mat-panel-title>
            </mat-expansion-panel-header>
            <p>{{ entry.a }}</p>
          </mat-expansion-panel>
        }
      </mat-accordion>
    </div>
  `,
  styles: `
    .faq {
      display: block;
      max-width: var(--dd-reading-max);
    }
  `,
})
export class FaqPage {
  protected readonly faqs = [
    {
      q: 'Do I need a membership to buy a single course?',
      a: 'No. Any course with a lifetime price can be bought outright with one payment, and it stays yours whether or not you ever subscribe.',
    },
    {
      q: 'What happens to my progress if I cancel the membership?',
      a: 'Your progress is never deleted. You keep access until the end of the period you paid for; if you re-subscribe later, you continue exactly where you left off. Courses you bought outright are unaffected entirely.',
    },
    {
      q: 'Can I watch on my phone?',
      a: 'Yes — the whole platform, including the video player, works on phones, tablets, and desktops.',
    },
    {
      q: 'How do free previews work?',
      a: 'Published courses mark selected lessons as previews. Anyone can watch those without an account; the full course opens when a membership or purchase covers it.',
    },
    {
      q: 'How do refunds work?',
      a: 'Membership billing mistakes are refunded on request. Course purchases can be refunded within 14 days if the course has not been substantially completed. Every refund is reviewed by a person.',
    },
    {
      q: 'How is my password handled?',
      a: 'It never touches our servers. Sign-in runs on Microsoft Entra External ID, the same identity platform used across Microsoft services, and Daniel’s Dojo only ever receives a signed token.',
    },
    {
      q: 'Is there a community?',
      a: 'Yes — course discussions, direct messages, and friends, moderated under the community guidelines. Community access comes with any active membership or course purchase.',
    },
  ];
}

/** About the dojo and the person behind it. Factual; no invented credentials. */
@Component({
  selector: 'app-about-page',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="About Daniel's Dojo"
      description="A small course platform with one conviction: you learn by building."
    >
      <h2>The idea</h2>
      <p>
        A dojo is a place you go to practise — not to watch someone else practise. The courses here
        are built the same way: every topic is taught by building something real, with the missteps
        and corrections left in, because that is where the learning is.
      </p>

      <h2>Who is behind it</h2>
      <p>
        Daniel's Dojo is built and taught by Daniel Terry, a software developer who has spent his
        career shipping enterprise applications and would rather show you the working system than
        the slideware. The platform itself — the site you are using right now — is built with the
        same tools and practices the courses teach.
      </p>

      <h2>How courses are made</h2>
      <p>
        Each course is recorded against a real project, split into focused lessons, and published
        only when the whole path from first lesson to working result has been walked. Resources,
        captions, and updates are added to the course you already own — buying a course means buying
        its future too.
      </p>
    </app-prose-page>
  `,
})
export class AboutPage {}

/**
 * Contact.
 *
 * Deliberately a direct email link rather than a form: a contact form without a transactional
 * email provider behind it is a message that silently goes nowhere, and this platform does
 * not ship dead controls. The form arrives with the email provider integration.
 */
@Component({
  selector: 'app-contact-page',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="Contact"
      description="A person reads these. Say what you need and you will get a straight answer."
    >
      <h2>Email</h2>
      <p>
        Write to
        <a href="mailto:djt727276&#64;gmail.com?subject=Daniel's%20Dojo">
          djt727276&#64;gmail.com
        </a>
        — include what you were doing and what happened if it is a problem report.
      </p>

      <h2>What to mention for a faster answer</h2>
      <ul>
        <li>
          <strong>Billing</strong> — the email on your account and roughly when you were charged.
        </li>
        <li><strong>Privacy</strong> — for data export or deletion requests.</li>
        <li><strong>Accessibility</strong> — treated with defect priority.</li>
        <li><strong>Community</strong> — for moderation matters; include a link to the content.</li>
      </ul>
    </app-prose-page>
  `,
})
export class ContactPage {}

/** Branded 404. The one page that must never itself be broken. */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule],
  template: `
    <div class="dd-page notfound">
      <p class="notfound__code" aria-hidden="true">404</p>
      <h1 class="notfound__title">This page left the dojo</h1>
      <p class="notfound__lead">
        The address may be mistyped, or the page may have moved. Nothing you had is lost.
      </p>
      <div class="notfound__actions">
        <a matButton="filled" routerLink="/">Go home</a>
        <a matButton routerLink="/courses">Browse courses</a>
      </div>
    </div>
  `,
  styles: `
    .notfound {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-4);
      align-items: center;
      padding-top: var(--dd-space-10);
      text-align: center;
    }

    .notfound__code {
      font-size: 5rem;
      font-weight: var(--dd-weight-bold);
      line-height: 1;
      color: var(--dd-primary);
      opacity: 0.35;
    }

    .notfound__title {
      font-size: var(--dd-text-2xl);
      font-weight: var(--dd-weight-medium);
    }

    .notfound__lead {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }

    .notfound__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
      justify-content: center;
    }
  `,
})
export class NotFound {}
