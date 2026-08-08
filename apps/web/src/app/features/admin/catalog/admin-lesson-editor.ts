import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import {
  LessonVideoStatus,
  describeVideoStatus,
  videoStatusTone,
} from '../../../core/admin/admin-media-api';
import {
  AdminCourseDetail,
  AdminLesson,
  LESSON_TYPES,
  LessonType,
  PublicationStatus,
  allowedTransitions,
  formatDuration,
} from '../../../core/admin/admin-catalog.model';
import { toApiFailure } from '../../../core/api/problem-details';
import { StatusChip, publicationTone } from '../../../shared/ui/status-chip/status-chip';
import { confirmStatusChange, transitionLabel } from './status-actions';

/**
 * One lesson row with an expandable editor.
 *
 * Collapsed by default so a long section stays readable; the editor is only rendered while
 * open, which keeps a section of thirty lessons from mounting thirty forms.
 */
@Component({
  selector: 'app-admin-lesson-editor',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    StatusChip,
  ],
  template: `
    <div class="lesson" [attr.data-testid]="'lesson-' + lesson().slug">
      <div class="lesson__summary">
        <span class="lesson__grip" aria-hidden="true">⋮⋮</span>

        <div class="lesson__identity">
          <span class="lesson__title">{{ lesson().title }}</span>
          <span class="lesson__meta">
            {{ lesson().lessonType }} · {{ duration(lesson().estimatedDurationSeconds) }}
            @if (lesson().isPreview) {
              · Free preview
            }
          </span>
        </div>

        <app-status-chip
          [label]="lesson().status"
          [tone]="tone(lesson().status)"
          srPrefix="Lesson status"
        />

        <div class="lesson__controls">
          <button
            matIconButton
            type="button"
            [disabled]="isFirst()"
            [attr.aria-label]="'Move ' + lesson().title + ' up'"
            (click)="moveUp.emit()"
            [attr.data-testid]="'lesson-up-' + lesson().slug"
          >
            <span aria-hidden="true">↑</span>
          </button>
          <button
            matIconButton
            type="button"
            [disabled]="isLast()"
            [attr.aria-label]="'Move ' + lesson().title + ' down'"
            (click)="moveDown.emit()"
            [attr.data-testid]="'lesson-down-' + lesson().slug"
          >
            <span aria-hidden="true">↓</span>
          </button>
          <button
            matButton
            type="button"
            [attr.aria-expanded]="open()"
            (click)="toggle()"
            [attr.data-testid]="'lesson-edit-' + lesson().slug"
          >
            {{ open() ? 'Close' : 'Edit' }}
          </button>
        </div>
      </div>

      @if (open()) {
        <form class="lesson__form dd-stack" [formGroup]="form" (ngSubmit)="save()">
          @if (message(); as note) {
            <p class="lesson__message" role="alert">{{ note }}</p>
          }

          <div class="lesson__row">
            <mat-form-field appearance="outline" class="lesson__field">
              <mat-label>Title</mat-label>
              <input matInput formControlName="title" />
            </mat-form-field>

            <mat-form-field appearance="outline" class="lesson__field">
              <mat-label>Type</mat-label>
              <mat-select formControlName="lessonType">
                @for (option of lessonTypes; track option.value) {
                  <mat-option [value]="option.value">{{ option.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>

          @if (form.controls.lessonType.value === 'Article') {
            <mat-form-field appearance="outline">
              <mat-label>Body</mat-label>
              <textarea
                matInput
                rows="8"
                formControlName="bodyMarkdown"
                data-testid="lesson-body"
              ></textarea>
              <mat-hint>Required before an article lesson can be published.</mat-hint>
            </mat-form-field>
          } @else {
            <!--
              The media area. Uploading happens in the media workspace, which owns the
              authorise → write to storage → verify → process sequence; this panel exists so an
              author never has to know that workspace exists to find it.
            -->
            <section class="video" aria-labelledby="video-heading-{{ lesson().id }}">
              <div class="video__header">
                <h4 class="video__heading" [id]="'video-heading-' + lesson().id">Video</h4>
                <app-status-chip
                  [label]="videoLabel()"
                  [tone]="videoTone()"
                  srPrefix="Video status"
                />
              </div>

              <p class="video__detail">{{ videoDetail() }}</p>

              <div class="video__actions">
                <a
                  matButton="filled"
                  [routerLink]="['/admin/lessons', lesson().id, 'media']"
                  [attr.data-testid]="'lesson-video-' + lesson().slug"
                >
                  {{ hasVideo() ? 'Manage video' : 'Upload video' }}
                </a>
                @if (videoFailed()) {
                  <span class="video__hint">
                    The stored master is kept, so processing can be retried there.
                  </span>
                }
              </div>
            </section>
          }

          <mat-form-field appearance="outline">
            <mat-label>Summary</mat-label>
            <input matInput formControlName="summary" />
          </mat-form-field>

          <!--
            Everything below is machinery rather than authoring: the URL segment the server
            derived from the title, and the duration estimate. Collapsed so the common path
            stays short, but reachable because a live URL sometimes has to be corrected.
          -->
          <details class="lesson__advanced">
            <summary class="lesson__advanced-summary">Advanced</summary>

            <div class="lesson__row lesson__advanced-body">
              <mat-form-field appearance="outline" class="lesson__field">
                <mat-label>URL segment</mat-label>
                <input matInput formControlName="slug" [attr.data-testid]="'lesson-slug-input'" />
                @if (lesson().status === 'Published') {
                  <mat-hint>Return the lesson to draft before changing its address.</mat-hint>
                } @else {
                  <mat-hint
                    >Generated from the title. Changing it changes the lesson's link.</mat-hint
                  >
                }
              </mat-form-field>

              <mat-form-field appearance="outline" class="lesson__field">
                <mat-label>Estimated duration (seconds)</mat-label>
                <input matInput type="number" min="0" formControlName="estimatedDurationSeconds" />
              </mat-form-field>
            </div>
          </details>

          <mat-checkbox formControlName="isPreview">Offer as a free preview</mat-checkbox>

          <div class="lesson__actions">
            <button
              matButton="filled"
              type="submit"
              [disabled]="busy()"
              [attr.data-testid]="'lesson-save-' + lesson().slug"
            >
              Save lesson
            </button>

            @for (target of transitions(); track target) {
              <button
                matButton="outlined"
                type="button"
                [disabled]="busy()"
                (click)="changeStatus(target)"
                [attr.data-testid]="'lesson-' + target.toLowerCase() + '-' + lesson().slug"
              >
                {{ label(target) }}
              </button>
            }
          </div>
        </form>
      }
    </div>
  `,
  styles: `
    .lesson {
      border-top: 1px solid var(--dd-outline);
      padding: var(--dd-space-3) 0;
    }

    .lesson__summary {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .lesson__grip {
      color: var(--dd-on-surface-variant);
      cursor: grab;
    }

    .lesson__identity {
      flex: 1 1 12rem;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    .lesson__title {
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }

    .lesson__meta {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .lesson__controls {
      display: flex;
      align-items: center;
      gap: var(--dd-space-1);
    }

    .lesson__form {
      padding: var(--dd-space-4) 0 var(--dd-space-2);
    }

    .lesson__row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-4);
    }

    .lesson__field {
      flex: 1 1 14rem;
    }

    .lesson__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-2);
    }

    .lesson__message {
      color: var(--dd-danger);
    }

    .lesson__advanced {
      border-top: 1px solid var(--dd-outline);
      padding-top: var(--dd-space-3);
    }

    .lesson__advanced-summary {
      cursor: pointer;
      font-size: var(--dd-text-sm);
      font-weight: var(--dd-weight-medium);
      color: var(--dd-on-surface-variant);
      min-height: 2.25rem;
      display: flex;
      align-items: center;
    }

    .lesson__advanced-body {
      padding-top: var(--dd-space-3);
    }

    /* The media panel: the one place an author looks for their video. */
    .video {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      padding: var(--dd-space-4);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-lg);
      background: var(--dd-surface-variant);
    }

    .video__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .video__heading {
      margin: 0;
      font-size: var(--dd-text-title);
      font-weight: var(--dd-weight-semibold);
    }

    .video__detail {
      margin: 0;
      color: var(--dd-on-surface-variant);
    }

    .video__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .video__hint {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class AdminLessonEditor {
  private readonly api = inject(AdminCatalogApi);
  private readonly dialog = inject(MatDialog);

  /** Owning course. */
  readonly courseId = input.required<string>();

  /** The lesson being edited. */
  readonly lesson = input.required<AdminLesson>();

  /** Whether the lesson is already at the top of its section. */
  readonly isFirst = input(false);

  /** Whether the lesson is already at the bottom of its section. */
  readonly isLast = input(false);

  /**
   * Opens the editor as soon as the row appears. Set for a lesson the author just created, so
   * creating one lands them in its editor — where the video upload lives — rather than back
   * at an empty form.
   */
  readonly openOnArrival = input(false);

  /** Emits the refreshed course after any successful mutation. */
  readonly changed = output<AdminCourseDetail>();

  /** Requests a one-position move, handled by the owning section. */
  readonly moveUp = output<void>();
  readonly moveDown = output<void>();

  protected readonly lessonTypes = LESSON_TYPES;
  protected readonly tone = publicationTone;
  protected readonly duration = formatDuration;
  protected readonly label = transitionLabel;

  protected readonly open = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  protected readonly transitions = computed(() => allowedTransitions(this.lesson().status));

  /**
   * The lesson's video state, read from the course detail the catalog already returns — no
   * extra request per row, so a section of thirty lessons still loads in one call.
   */
  private readonly videoStatus = computed<LessonVideoStatus>(
    () => (this.lesson().videoStatus as LessonVideoStatus | null) ?? 'None',
  );

  protected readonly hasVideo = computed(() => this.videoStatus() !== 'None');
  protected readonly videoFailed = computed(() => this.videoStatus() === 'Failed');
  protected readonly videoTone = computed(() => videoStatusTone(this.videoStatus()));
  protected readonly videoDetail = computed(() => describeVideoStatus(this.videoStatus()));

  protected readonly videoLabel = computed(() =>
    this.videoStatus() === 'None' ? 'No video' : this.videoStatus(),
  );

  protected readonly form = new FormGroup({
    slug: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    summary: new FormControl<string>('', { nonNullable: true }),
    lessonType: new FormControl<LessonType>('Article', { nonNullable: true }),
    bodyMarkdown: new FormControl<string>('', { nonNullable: true }),
    isPreview: new FormControl(false, { nonNullable: true }),
    estimatedDurationSeconds: new FormControl<number | null>(null),
  });

  /** Guards the auto-open so a closed editor is never reopened by a later refresh. */
  private autoOpened = false;

  constructor() {
    effect(() => {
      if (this.openOnArrival() && !this.autoOpened) {
        this.autoOpened = true;
        this.openWith();
      }
    });
  }

  protected toggle(): void {
    if (this.open()) {
      this.open.set(false);
      this.message.set(null);
      return;
    }

    this.openWith();
  }

  /** Opens the editor with the form loaded from the current lesson. */
  private openWith(): void {
    const lesson = this.lesson();

    this.open.set(true);
    this.message.set(null);
    this.form.setValue({
      slug: lesson.slug,
      title: lesson.title,
      summary: lesson.summary ?? '',
      lessonType: lesson.lessonType,
      bodyMarkdown: lesson.bodyMarkdown ?? '',
      isPreview: lesson.isPreview,
      estimatedDurationSeconds: lesson.estimatedDurationSeconds,
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.run(
      this.api.updateLesson(this.courseId(), this.lesson().id, {
        slug: value.slug.trim(),
        title: value.title.trim(),
        summary: value.summary.trim() || null,
        lessonType: value.lessonType,
        bodyMarkdown: value.bodyMarkdown.trim() || null,
        isPreview: value.isPreview,
        estimatedDurationSeconds: value.estimatedDurationSeconds,
        rowVersion: this.lesson().rowVersion,
      }),
    );
  }

  protected changeStatus(target: PublicationStatus): void {
    confirmStatusChange(this.dialog, 'lesson', target).subscribe((result) => {
      if (!result) {
        return;
      }

      this.run(
        this.api.changeLessonStatus(this.courseId(), this.lesson().id, target, {
          reason: result.reason,
          rowVersion: this.lesson().rowVersion,
        }),
      );
    });
  }

  private run(request: ReturnType<AdminCatalogApi['updateLesson']>): void {
    this.busy.set(true);
    this.message.set(null);

    request.subscribe({
      next: (course) => {
        this.busy.set(false);
        this.changed.emit(course);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.message.set(toApiFailure(error, 'The lesson could not be saved.').message);
      },
    });
  }
}
