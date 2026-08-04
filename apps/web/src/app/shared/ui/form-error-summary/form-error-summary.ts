import { Component, input } from '@angular/core';

/** One entry in the summary. */
export interface FormErrorEntry {
  /** Control name, used to move focus to the offending field. */
  readonly field: string;

  /** Human-readable message. Never a raw server payload. */
  readonly message: string;
}

/**
 * Accessible validation summary shown above a form after a failed submit.
 *
 * Announced as an alert, and each entry is a link that moves focus to the
 * control, which is the standard pattern for keyboard and screen-reader users
 * who would otherwise have to hunt for the invalid field.
 */
@Component({
  selector: 'app-form-error-summary',
  template: `
    @if (errors().length > 0) {
      <div class="summary" role="alert" tabindex="-1" data-testid="form-error-summary">
        <h2 class="summary__title">
          {{ errors().length }} problem{{ errors().length === 1 ? '' : 's' }} with this form
        </h2>
        <ul class="summary__list">
          @for (error of errors(); track error.field) {
            <li>
              <a [href]="'#' + controlId(error.field)" (click)="focusField($event, error.field)">
                {{ error.message }}
              </a>
            </li>
          }
        </ul>
      </div>
    }
  `,
  styles: `
    .summary {
      padding: var(--dd-space-4);
      background: var(--dd-danger-container);
      border-left: 4px solid var(--dd-danger);
      border-radius: var(--dd-radius-md);
    }

    .summary__title {
      margin-bottom: var(--dd-space-2);
      font-size: var(--dd-text-base);
      font-weight: var(--dd-weight-bold);
      color: var(--dd-danger);
    }

    .summary__list {
      margin: 0;
      padding-left: var(--dd-space-5);
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-1);
    }
  `,
})
export class FormErrorSummary {
  readonly errors = input.required<readonly FormErrorEntry[]>();

  /** Convention shared with the form templates so anchors resolve. */
  protected controlId(field: string): string {
    return `field-${field}`;
  }

  protected focusField(event: Event, field: string): void {
    event.preventDefault();

    const target = document.getElementById(this.controlId(field));
    target?.focus();
  }
}
