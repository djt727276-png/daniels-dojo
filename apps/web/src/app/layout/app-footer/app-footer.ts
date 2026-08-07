import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { DdLogo } from '../../shared/ui/brand/dd-logo';

/** A configurable social destination. Empty url hides the entry entirely. */
interface SocialLink {
  readonly label: string;
  readonly url: string;
}

/**
 * The site footer: product navigation, the legal set, and social links.
 *
 * Social entries render only when a URL is configured, so the footer never ships dead
 * buttons pointing at profiles that do not exist yet — an empty list simply collapses the
 * section.
 */
@Component({
  selector: 'app-footer',
  imports: [RouterLink, DdLogo],
  template: `
    <footer class="footer" role="contentinfo">
      <div class="footer__inner">
        <div class="footer__col">
          <p class="footer__brand"><dd-logo variant="lockup" /></p>
          <p class="footer__tagline">Build real software. Train like a professional.</p>
        </div>

        <nav class="footer__col" aria-label="Explore">
          <p class="footer__heading">Explore</p>
          <a routerLink="/courses">Courses</a>
          <a routerLink="/pricing">Pricing</a>
          <a routerLink="/about">About</a>
          <a routerLink="/faq">FAQ</a>
          <a routerLink="/contact">Contact</a>
        </nav>

        <nav class="footer__col" aria-label="Legal">
          <p class="footer__heading">Legal</p>
          <a routerLink="/legal/privacy">Privacy policy</a>
          <a routerLink="/legal/terms">Terms of service</a>
          <a routerLink="/legal/refunds">Refund policy</a>
          <a routerLink="/legal/community-guidelines">Community guidelines</a>
          <a routerLink="/legal/accessibility">Accessibility</a>
        </nav>

        @if (socials.length > 0) {
          <nav class="footer__col" aria-label="Social media">
            <p class="footer__heading">Follow</p>
            @for (social of socials; track social.label) {
              <a [href]="social.url" target="_blank" rel="noopener noreferrer">
                {{ social.label }}
              </a>
            }
          </nav>
        }
      </div>

      <p class="footer__legal">© {{ year }} Daniel's Dojo. All rights reserved.</p>
    </footer>
  `,
  styles: `
    .footer {
      margin-top: var(--dd-space-10);
      padding: var(--dd-space-8) var(--dd-space-4) var(--dd-space-5);
      background: var(--dd-surface);
      border-top: 1px solid var(--dd-outline);
    }

    .footer__inner {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      gap: var(--dd-space-6);
      max-width: var(--dd-content-max);
      margin: 0 auto;
    }

    .footer__col {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);

      a {
        color: var(--dd-on-surface-variant);
        text-decoration: none;

        &:hover {
          color: var(--dd-primary);
          text-decoration: underline;
        }
      }
    }

    .footer__brand {
      font-size: var(--dd-text-base);
    }

    .footer__tagline {
      color: var(--dd-on-surface-variant);
    }

    .footer__heading {
      font-size: var(--dd-text-sm);
      font-weight: var(--dd-weight-medium);
      color: var(--dd-on-surface-variant);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }

    .footer__legal {
      max-width: var(--dd-content-max);
      margin: var(--dd-space-6) auto 0;
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class AppFooter {
  protected readonly year = new Date().getFullYear();

  /**
   * Configured social destinations. Populate as real profiles exist; entries without a URL
   * never render.
   */
  protected readonly socials: readonly SocialLink[] = [];
}
