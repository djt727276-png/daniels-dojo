import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

/**
 * Busy indicator with a screen-reader status region.
 *
 * The spinner alone conveys nothing to assistive technology, so the message is
 * always announced politely as well.
 */
@Component({
  selector: 'app-loading-state',
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="state" role="status" aria-live="polite">
      <mat-spinner [diameter]="32" aria-hidden="true" />
      <p class="state__message">{{ message() }}</p>
    </div>
  `,
  styles: `
    .state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--dd-space-3);
      padding: var(--dd-space-8) var(--dd-space-4);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class LoadingState {
  /** Announced while the region is busy. */
  readonly message = input('Loading…');
}

/**
 * Honest empty state. Explains why there is nothing here and, where one
 * exists, offers the action that would change that.
 */
@Component({
  selector: 'app-empty-state',
  imports: [MatButtonModule],
  template: `
    <div class="state">
      <svg class="state__mark" viewBox="0 0 48 44" aria-hidden="true" focusable="false">
        <path d="M2 6.4C8.9 3.4 16.2 1.9 24 1.9S39.1 3.4 46 6.4l-1 4.7H3Z" />
        <rect x="6.6" y="15.7" width="34.8" height="4.2" rx="1.3" />
        <path d="M9 11.1h5.5L15.8 44H8.2Z" />
        <path d="M33.5 11.1H39L39.8 44h-7.6Z" />
      </svg>
      <h2 class="state__title">{{ title() }}</h2>
      <p class="state__message">{{ message() }}</p>
      @if (actionLabel()) {
        <button matButton="filled" type="button" (click)="action.emit()">
          {{ actionLabel() }}
        </button>
      }
      <ng-content />
    </div>
  `,
  styles: `
    .state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--dd-space-3);
      padding: var(--dd-space-8) var(--dd-space-4);
      text-align: center;
      background: var(--dd-surface);
      border: 1px dashed var(--dd-outline);
      border-radius: var(--dd-radius-lg);
    }

    .state__mark {
      width: 3rem;
      fill: color-mix(in srgb, var(--dd-on-surface-variant) 45%, transparent);
    }

    .state__title {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-semibold);
    }

    .state__message {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input.required<string>();

  /** Omit to render no action. */
  readonly actionLabel = input<string>();

  readonly action = output<void>();
}

/**
 * Recoverable error state.
 *
 * The message shown is always a caller-supplied, human-readable sentence —
 * never a server payload — so no token, claim, or internal detail can reach
 * the screen through this component.
 */
@Component({
  selector: 'app-error-state',
  imports: [MatButtonModule],
  template: `
    <div class="state" role="alert">
      <h2 class="state__title">{{ title() }}</h2>
      <p class="state__message">{{ message() }}</p>
      @if (retryLabel()) {
        <button matButton="filled" type="button" (click)="retry.emit()">
          {{ retryLabel() }}
        </button>
      }
    </div>
  `,
  styles: `
    .state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--dd-space-3);
      padding: var(--dd-space-6) var(--dd-space-4);
      text-align: center;
      background: var(--dd-danger-container);
      border-radius: var(--dd-radius-lg);
    }

    .state__title {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
      color: var(--dd-danger);
    }

    .state__message {
      max-width: var(--dd-reading-max);
    }
  `,
})
export class ErrorState {
  readonly title = input('Something went wrong');
  readonly message = input.required<string>();
  readonly retryLabel = input<string>('Try again');
  readonly retry = output<void>();
}
