import { Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

/**
 * Single headline figure with a label and optional supporting note.
 *
 * The label is associated with the value through `aria-describedby` so the
 * figure is never announced as a bare number.
 */
@Component({
  selector: 'app-stat-card',
  imports: [MatCardModule],
  template: `
    <mat-card appearance="outlined" class="stat">
      <mat-card-content class="stat__content">
        <p class="stat__label" [id]="labelId()">{{ label() }}</p>
        <p class="stat__value" [attr.aria-describedby]="labelId()">{{ value() }}</p>
        @if (note()) {
          <p class="stat__note">{{ note() }}</p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .stat {
      height: 100%;
    }

    .stat__content {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-1);
    }

    .stat__label {
      font-size: var(--dd-text-xs);
      font-weight: var(--dd-weight-medium);
      letter-spacing: var(--dd-tracking-label);
      text-transform: uppercase;
      color: var(--dd-on-surface-variant);
    }

    .stat__value {
      font-size: var(--dd-text-2xl);
      font-weight: var(--dd-weight-bold);
      line-height: var(--dd-leading-tight);
      font-variant-numeric: tabular-nums;
      letter-spacing: -0.02em;
    }

    .stat__note {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly note = input<string>();

  /** Stable id linking the value to its label for assistive technology. */
  protected labelId(): string {
    return `stat-${this.label()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')}`;
  }
}
