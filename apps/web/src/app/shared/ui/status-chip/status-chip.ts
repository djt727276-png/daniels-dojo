import { Component, computed, input } from '@angular/core';

/** Semantic tone driving both the colour and the text prefix. */
export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

/**
 * Compact status indicator.
 *
 * Status is never carried by colour alone: the chip always renders the status
 * word itself, and a visually hidden prefix names what the status describes so
 * a screen reader hears "Status: Published" rather than a bare word.
 */
@Component({
  selector: 'app-status-chip',
  template: `
    <span class="chip" [class]="toneClass()">
      <span class="dd-visually-hidden">{{ srPrefix() }}: </span>
      <span aria-hidden="true" class="chip__dot"></span>
      {{ label() }}
    </span>
  `,
  styles: `
    .chip {
      display: inline-flex;
      align-items: center;
      gap: var(--dd-space-2);
      padding: 0.15rem var(--dd-space-3);
      border-radius: var(--dd-radius-pill);
      font-size: var(--dd-text-sm);
      font-weight: var(--dd-weight-medium);
      white-space: nowrap;
    }

    .chip__dot {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: currentcolor;
    }

    .chip--neutral {
      background: var(--dd-surface-variant);
      color: var(--dd-on-surface-variant);
    }

    .chip--success {
      background: var(--dd-success-container);
      color: var(--dd-success);
    }

    .chip--warning {
      background: var(--dd-warning-container);
      color: var(--dd-warning);
    }

    .chip--danger {
      background: var(--dd-danger-container);
      color: var(--dd-danger);
    }

    .chip--info {
      background: var(--dd-info-container);
      color: var(--dd-info);
    }
  `,
})
export class StatusChip {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');

  /** What the status describes, announced before the label. */
  readonly srPrefix = input('Status');

  protected readonly toneClass = computed(() => `chip--${this.tone()}`);
}

/** Maps a catalog publication status to a chip tone. */
export function publicationTone(status: string): StatusTone {
  switch (status) {
    case 'Published':
      return 'success';
    case 'Draft':
      return 'warning';
    case 'Archived':
      return 'neutral';
    default:
      return 'neutral';
  }
}
