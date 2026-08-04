import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { CatalogApi } from '../../core/catalog/catalog-api';
import { LessonPreview as LessonPreviewModel } from '../../core/catalog/catalog.model';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type PreviewState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly preview: LessonPreviewModel }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error' };

/**
 * Free preview of a published Article lesson.
 *
 * The body is rendered through a text binding inside a preformatted block. It is never passed
 * to `innerHTML` and no Markdown-to-HTML library is involved, so stored content cannot become
 * executable markup in a reader's browser.
 */
@Component({
  selector: 'app-lesson-preview',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading preview…" />
        }

        @case ('missing') {
          <app-empty-state
            title="Preview not available"
            message="This lesson does not have a free preview."
            data-testid="preview-missing"
          >
            <a matButton="filled" [routerLink]="['/courses', courseSlug]">Back to the course</a>
          </app-empty-state>
        }

        @case ('error') {
          <app-error-state message="We could not load this preview just now." (retry)="load()" />
        }

        @default {
          @if (state(); as current) {
            @if (current.kind === 'ready') {
              <app-page-header
                [title]="current.preview.title"
                [description]="'Free preview from ' + current.preview.courseTitle"
              >
                <a matButton [routerLink]="['/courses', current.preview.courseSlug]">
                  Back to the course
                </a>
              </app-page-header>

              <mat-card appearance="outlined">
                <mat-card-content>
                  @if (current.preview.summary) {
                    <p class="preview__summary">{{ current.preview.summary }}</p>
                  }

                  <!--
                    Text binding inside <pre>: line breaks are preserved and any markup in the
                    stored body is displayed literally rather than being parsed.
                  -->
                  <pre class="preview__body" data-testid="preview-body">{{
                    current.preview.body
                  }}</pre>
                </mat-card-content>
              </mat-card>
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .preview__summary {
      margin-bottom: var(--dd-space-4);
      color: var(--dd-on-surface-variant);
    }

    .preview__body {
      max-width: var(--dd-reading-max);
      margin: 0;
      font-family: var(--dd-font-sans);
      font-size: var(--dd-text-base);
      line-height: var(--dd-leading-base);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
  `,
})
export class LessonPreview {
  private readonly api = inject(CatalogApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly state = signal<PreviewState>({ kind: 'loading' });

  protected courseSlug = '';
  private lessonSlug = '';

  constructor() {
    this.courseSlug = this.route.snapshot.paramMap.get('courseSlug') ?? '';
    this.lessonSlug = this.route.snapshot.paramMap.get('lessonSlug') ?? '';
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getLessonPreview(this.courseSlug, this.lessonSlug).subscribe({
      next: (preview) => this.state.set({ kind: 'ready', preview }),
      error: (error: unknown) =>
        this.state.set(
          (error as { status?: number } | null)?.status === 404
            ? { kind: 'missing' }
            : { kind: 'error' },
        ),
    });
  }
}
