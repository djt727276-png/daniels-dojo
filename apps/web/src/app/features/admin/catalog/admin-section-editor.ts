import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import {
  AdminCourseDetail,
  AdminSection,
  LESSON_TYPES,
  LessonType,
  PublicationStatus,
  allowedTransitions,
} from '../../../core/admin/admin-catalog.model';
import { toApiFailure } from '../../../core/api/problem-details';
import { StatusChip, publicationTone } from '../../../shared/ui/status-chip/status-chip';
import { AdminLessonEditor } from './admin-lesson-editor';
import { confirmStatusChange, transitionLabel } from './status-actions';

/**
 * One section card: its own metadata and status, plus the ordered lessons inside it.
 *
 * Lessons can be reordered by dragging or with the Move up / Move down buttons. Both paths
 * send the same exact-set payload, so keyboard-only operators are not second-class and the
 * server sees one kind of request either way.
 */
@Component({
  selector: 'app-admin-section-editor',
  imports: [
    ReactiveFormsModule,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    StatusChip,
    AdminLessonEditor,
  ],
  template: `
    <mat-card appearance="outlined" [attr.data-testid]="'section-' + section().id">
      <mat-card-content class="section dd-stack">
        <div class="section__header">
          <button
            matIconButton
            type="button"
            cdkDragHandle
            class="section__grip"
            aria-label="Drag to reorder this section"
          >
            <span aria-hidden="true">⋮⋮</span>
          </button>

          <h3 class="section__title">{{ section().title }}</h3>

          <app-status-chip
            [label]="section().status"
            [tone]="tone(section().status)"
            srPrefix="Section status"
          />

          <div class="section__controls">
            <button
              matIconButton
              type="button"
              [disabled]="isFirst()"
              [attr.aria-label]="'Move section ' + section().title + ' up'"
              (click)="moveUp.emit()"
              [attr.data-testid]="'section-up-' + section().id"
            >
              <span aria-hidden="true">↑</span>
            </button>
            <button
              matIconButton
              type="button"
              [disabled]="isLast()"
              [attr.aria-label]="'Move section ' + section().title + ' down'"
              (click)="moveDown.emit()"
              [attr.data-testid]="'section-down-' + section().id"
            >
              <span aria-hidden="true">↓</span>
            </button>
            <button matButton type="button" [attr.aria-expanded]="editing()" (click)="toggleEdit()">
              {{ editing() ? 'Close' : 'Edit section' }}
            </button>
          </div>
        </div>

        @if (message(); as note) {
          <p class="section__message" role="alert">{{ note }}</p>
        }

        @if (editing()) {
          <form class="dd-stack" [formGroup]="form" (ngSubmit)="save()">
            <mat-form-field appearance="outline">
              <mat-label>Title</mat-label>
              <input matInput formControlName="title" data-testid="section-title-input" />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Description</mat-label>
              <textarea matInput rows="2" formControlName="description"></textarea>
            </mat-form-field>

            <div class="section__actions">
              <button matButton="filled" type="submit" [disabled]="busy()">Save section</button>

              @for (target of transitions(); track target) {
                <button
                  matButton="outlined"
                  type="button"
                  [disabled]="busy()"
                  (click)="changeStatus(target)"
                  [attr.data-testid]="'section-' + target.toLowerCase() + '-' + section().id"
                >
                  {{ label(target) }}
                </button>
              }
            </div>
          </form>
        }

        @if (section().lessons.length === 0) {
          <p class="section__empty">No lessons yet.</p>
        } @else {
          <div cdkDropList (cdkDropListDropped)="dropLesson($event)" class="section__lessons">
            @for (lesson of orderedLessons(); track lesson.id; let index = $index) {
              <div cdkDrag>
                <app-admin-lesson-editor
                  [courseId]="courseId()"
                  [lesson]="lesson"
                  [isFirst]="index === 0"
                  [isLast]="index === orderedLessons().length - 1"
                  (changed)="changed.emit($event)"
                  (moveUp)="moveLesson(index, index - 1)"
                  (moveDown)="moveLesson(index, index + 1)"
                />
              </div>
            }
          </div>
        }

        <form class="section__new-lesson" [formGroup]="lessonForm" (ngSubmit)="addLesson()">
          <mat-form-field appearance="outline" class="section__new-field">
            <mat-label>New lesson title</mat-label>
            <input matInput formControlName="title" [attr.data-testid]="'new-lesson-title'" />
          </mat-form-field>

          <mat-form-field appearance="outline" class="section__new-field">
            <mat-label>Slug</mat-label>
            <input matInput formControlName="slug" [attr.data-testid]="'new-lesson-slug'" />
          </mat-form-field>

          <mat-form-field appearance="outline" class="section__new-field">
            <mat-label>Type</mat-label>
            <mat-select formControlName="lessonType">
              @for (option of lessonTypes; track option.value) {
                <mat-option [value]="option.value">{{ option.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <button
            matButton="filled"
            type="submit"
            [disabled]="busy() || lessonForm.invalid"
            [attr.data-testid]="'add-lesson-' + section().id"
          >
            Add lesson
          </button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .section__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .section__title {
      flex: 1 1 12rem;
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }

    .section__grip {
      cursor: grab;
    }

    .section__controls,
    .section__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
    }

    .section__empty {
      color: var(--dd-on-surface-variant);
    }

    .section__message {
      color: var(--dd-danger);
    }

    .section__new-lesson {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: var(--dd-space-3);
      padding-top: var(--dd-space-3);
      border-top: 1px solid var(--dd-outline);
    }

    .section__new-field {
      flex: 1 1 12rem;
    }

    .cdk-drag-preview {
      box-shadow: var(--dd-elevation-3);
      border-radius: var(--dd-radius-lg);
      background: var(--dd-surface);
    }

    .cdk-drag-placeholder {
      opacity: 0.4;
    }
  `,
})
export class AdminSectionEditor {
  private readonly api = inject(AdminCatalogApi);
  private readonly dialog = inject(MatDialog);

  readonly courseId = input.required<string>();
  readonly section = input.required<AdminSection>();
  readonly isFirst = input(false);
  readonly isLast = input(false);

  /** Emits the refreshed course after any successful mutation. */
  readonly changed = output<AdminCourseDetail>();

  readonly moveUp = output<void>();
  readonly moveDown = output<void>();

  protected readonly lessonTypes = LESSON_TYPES;
  protected readonly tone = publicationTone;
  protected readonly label = transitionLabel;

  protected readonly editing = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  protected readonly transitions = computed(() => allowedTransitions(this.section().status));

  protected readonly orderedLessons = computed(() =>
    [...this.section().lessons].sort((left, right) => left.sortOrder - right.sortOrder),
  );

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true }),
  });

  protected readonly lessonForm = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    slug: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    lessonType: new FormControl<LessonType>('Article', { nonNullable: true }),
  });

  protected toggleEdit(): void {
    const next = !this.editing();
    this.editing.set(next);

    if (next) {
      this.form.setValue({
        title: this.section().title,
        description: this.section().description ?? '',
      });
    }
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.run(
      this.api.updateSection(this.courseId(), this.section().id, {
        title: value.title.trim(),
        description: value.description.trim() || null,
        rowVersion: this.section().rowVersion,
      }),
    );
  }

  protected changeStatus(target: PublicationStatus): void {
    confirmStatusChange(this.dialog, 'section', target).subscribe((result) => {
      if (!result) {
        return;
      }

      this.run(
        this.api.changeSectionStatus(this.courseId(), this.section().id, target, {
          reason: result.reason,
          rowVersion: this.section().rowVersion,
        }),
      );
    });
  }

  protected addLesson(): void {
    if (this.lessonForm.invalid) {
      this.lessonForm.markAllAsTouched();
      return;
    }

    const value = this.lessonForm.getRawValue();

    this.run(
      this.api.createLesson(this.courseId(), this.section().id, {
        slug: value.slug.trim(),
        title: value.title.trim(),
        summary: null,
        lessonType: value.lessonType,
        bodyMarkdown: null,
        isPreview: false,
        estimatedDurationSeconds: null,
      }),
      () => this.lessonForm.reset({ title: '', slug: '', lessonType: 'Article' }),
    );
  }

  protected dropLesson(event: CdkDragDrop<unknown>): void {
    this.moveLesson(event.previousIndex, event.currentIndex);
  }

  /** Applies a one-position move and sends the complete new order. */
  protected moveLesson(from: number, to: number): void {
    const lessons = [...this.orderedLessons()];

    if (to < 0 || to >= lessons.length || from === to) {
      return;
    }

    moveItemInArray(lessons, from, to);

    this.run(
      this.api.reorderLessons(
        this.courseId(),
        this.section().id,
        lessons.map((lesson) => ({ id: lesson.id, rowVersion: lesson.rowVersion })),
      ),
    );
  }

  private run(request: ReturnType<AdminCatalogApi['updateSection']>, onSuccess?: () => void): void {
    this.busy.set(true);
    this.message.set(null);

    request.subscribe({
      next: (course) => {
        this.busy.set(false);
        onSuccess?.();
        this.changed.emit(course);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.message.set(toApiFailure(error, 'The section could not be updated.').message);
      },
    });
  }
}
