import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The Daniel's Dojo brand mark: a geometric torii gate drawn as a code-native
 * SVG so it stays crisp at every size and inherits `currentColor`.
 *
 * `lockup` adds the wordmark beside the gate for headers and the storefront;
 * `mark` is the compact form for tight navigation and footers. Colour comes
 * from the surrounding text colour, with the gate itself defaulting to
 * mastery gold via the `gold` input — the one deliberate use of gold outside
 * achievement, per the brand system.
 */
@Component({
  selector: 'dd-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="dd-logo" [class.dd-logo--lockup]="variant() === 'lockup'">
      <svg
        class="dd-logo__mark"
        [class.dd-logo__mark--gold]="gold()"
        viewBox="0 0 48 44"
        aria-hidden="true"
        focusable="false"
      >
        <path d="M2 6.4C8.9 3.4 16.2 1.9 24 1.9S39.1 3.4 46 6.4l-1 4.7H3Z" />
        <rect x="6.6" y="15.7" width="34.8" height="4.2" rx="1.3" />
        <path d="M9 11.1h5.5L15.8 44H8.2Z" />
        <path d="M33.5 11.1H39L39.8 44h-7.6Z" />
      </svg>
      @if (variant() === 'lockup') {
        <span class="dd-logo__wordmark">Daniel&#8217;s&nbsp;Dojo</span>
      }
    </span>
  `,
  styles: `
    .dd-logo {
      display: inline-flex;
      align-items: center;
      gap: 0.625rem;
      color: inherit;
    }

    .dd-logo__mark {
      display: block;
      width: 1.75em;
      height: auto;
      fill: currentColor;
    }

    .dd-logo__mark--gold {
      fill: var(--dd-gold);
    }

    .dd-logo__wordmark {
      font-weight: var(--dd-weight-semibold);
      font-size: 1.1875em;
      letter-spacing: -0.01em;
      white-space: nowrap;
    }
  `,
})
export class DdLogo {
  /** `lockup` renders mark + wordmark; `mark` renders the gate alone. */
  readonly variant = input<'lockup' | 'mark'>('lockup');

  /** Gold gate (brand default); false inherits the text colour instead. */
  readonly gold = input(true);
}
