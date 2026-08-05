import { Component, input } from '@angular/core';

import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * Shared layout for the written public pages — legal documents, FAQ, about.
 *
 * One reading column, one heading structure, one max-width. Every page projected into it
 * inherits the same rhythm, so the legal set reads as one product rather than five documents
 * formatted five ways.
 */
@Component({
  selector: 'app-prose-page',
  imports: [PageHeader],
  template: `
    <div class="dd-page prose">
      <app-page-header [title]="title()" [description]="description()" />
      <div class="prose__body">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    .prose__body {
      max-width: var(--dd-reading-max);
      margin-top: var(--dd-space-5);

      /* Projected content is plain semantic HTML; the rhythm lives here once. */
      ::ng-deep h2 {
        margin-top: var(--dd-space-6);
        margin-bottom: var(--dd-space-3);
        font-size: var(--dd-text-xl);
        font-weight: var(--dd-weight-medium);
      }

      ::ng-deep h3 {
        margin-top: var(--dd-space-5);
        margin-bottom: var(--dd-space-2);
        font-size: var(--dd-text-lg);
        font-weight: var(--dd-weight-medium);
      }

      ::ng-deep p,
      ::ng-deep li {
        margin-bottom: var(--dd-space-3);
        line-height: var(--dd-leading-base);
      }

      ::ng-deep ul,
      ::ng-deep ol {
        padding-left: var(--dd-space-5);
      }
    }
  `,
})
export class ProsePage {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
