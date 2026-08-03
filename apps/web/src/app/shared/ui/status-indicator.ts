import { Component, input } from '@angular/core';

export type StatusTone = 'healthy' | 'unavailable' | 'loading';

/**
 * Small presentational status pill: a coloured dot plus a text label. Purely
 * driven by inputs so it can be reused wherever a status needs to be shown.
 */
@Component({
  selector: 'app-status-indicator',
  templateUrl: './status-indicator.html',
  styleUrl: './status-indicator.scss',
})
export class StatusIndicator {
  readonly tone = input.required<StatusTone>();
  readonly label = input.required<string>();
}
