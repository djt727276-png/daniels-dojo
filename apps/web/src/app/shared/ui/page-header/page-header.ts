import { Component, input } from '@angular/core';

/**
 * Standard page heading block: the page's single `h1`, an optional supporting
 * line, and a slot for contextual actions.
 *
 * Every routed page uses this so heading level and landmark structure stay
 * consistent instead of each screen inventing its own.
 */
@Component({
  selector: 'app-page-header',
  template: `
    <header class="page-header">
      <div class="page-header__text">
        <h1 class="page-header__title">{{ title() }}</h1>
        @if (description()) {
          <p class="page-header__description">{{ description() }}</p>
        }
      </div>
      <div class="page-header__actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: `
    .page-header {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--dd-space-4);
      padding-bottom: var(--dd-space-4);
      border-bottom: 1px solid var(--dd-outline);
    }

    .page-header__text {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
      min-width: 0;
    }

    .page-header__title {
      font-size: var(--dd-text-2xl);
      line-height: var(--dd-leading-tight);
      font-weight: var(--dd-weight-bold);
      overflow-wrap: anywhere;
    }

    .page-header__description {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }

    .page-header__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-2);
    }
  `,
})
export class PageHeader {
  /** Page title, rendered as the page's only `h1`. */
  readonly title = input.required<string>();

  /** Optional supporting sentence shown beneath the title. */
  readonly description = input<string>();
}
