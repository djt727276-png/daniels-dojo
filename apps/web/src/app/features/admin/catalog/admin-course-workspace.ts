import { CdkDrag, CdkDragDrop, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import {
  AdminCourseDetail,
  AdminTag,
  COURSE_LEVELS,
  CourseLevel,
  PublicationStatus,
  allowedTransitions,
} from '../../../core/admin/admin-catalog.model';
import { isConcurrencyConflict, toApiFailure } from '../../../core/api/problem-details';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip, publicationTone } from '../../../shared/ui/status-chip/status-chip';
import { AdminSectionEditor } from './admin-section-editor';
import { confirmStatusChange, transitionLabel } from './status-actions';

type WorkspaceState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly course: AdminCourseDetail }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The authoring workspace for one course: metadata, tags, publication, and the curriculum.
 *
 * Every mutation returns the whole course, and this component replaces its state with that
 * response. Row versions therefore stay current across a long editing session, and a stale
 * write surfaces as a clear "reload" prompt rather than a silent overwrite.
 */
@Component({
  selector: 'app-admin-course-workspace',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CdkDropList,
    CdkDrag,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatChipsModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
    FormErrorSummary,
    AdminSectionEditor,
  ],
  templateUrl: './admin-course-workspace.html',
  styleUrl: './admin-course-workspace.scss',
})
export class AdminCourseWorkspace {
  private readonly api = inject(AdminCatalogApi);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);

  protected readonly levels = COURSE_LEVELS;
  protected readonly tone = publicationTone;
  protected readonly label = transitionLabel;

  protected readonly courseId = this.route.snapshot.paramMap.get('courseId') ?? '';
  protected readonly state = signal<WorkspaceState>({ kind: 'loading' });
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);
  protected readonly notice = signal<string | null>(null);
  protected readonly staleWarning = signal(false);
  protected readonly tags = signal<readonly AdminTag[]>([]);

  protected readonly course = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.course : null;
  });

  protected readonly sections = computed(() =>
    [...(this.course()?.sections ?? [])].sort((left, right) => left.sortOrder - right.sortOrder),
  );

  protected readonly transitions = computed(() => {
    const course = this.course();
    return course ? allowedTransitions(course.status) : [];
  });

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    slug: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    summary: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    level: new FormControl<CourseLevel>('AllLevels', { nonNullable: true }),
    includedInMembership: new FormControl(true, { nonNullable: true }),
    imageAltText: new FormControl('', { nonNullable: true }),
  });

  protected readonly sectionForm = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly tagForm = new FormGroup({
    tagIds: new FormControl<readonly string[]>([], { nonNullable: true }),
    newTag: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    this.load();
    this.loadTags();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getCourse(this.courseId).subscribe({
      next: (course) => this.apply(course),
      error: (error: unknown) => {
        const failure = toApiFailure(error, 'We could not load this course.');
        this.state.set(
          failure.status === 404
            ? { kind: 'missing' }
            : { kind: 'error', message: failure.message },
        );
      },
    });
  }

  private loadTags(): void {
    this.api.listTags().subscribe({
      next: (tags) => this.tags.set(tags),
      error: () => this.tags.set([]),
    });
  }

  /** Replaces local state with the server's view and refreshes the form's row version. */
  protected apply(course: AdminCourseDetail): void {
    this.state.set({ kind: 'ready', course });
    this.staleWarning.set(false);
    this.errors.set([]);

    this.form.setValue({
      title: course.title,
      slug: course.slug,
      summary: course.summary,
      description: course.description,
      level: course.level,
      includedInMembership: course.includedInMembership,
      imageAltText: course.imageAltText ?? '',
    });

    if (course.slugLocked) {
      this.form.controls.slug.disable({ emitEvent: false });
    } else {
      this.form.controls.slug.enable({ emitEvent: false });
    }

    this.tagForm.controls.tagIds.setValue(course.tags.map((tag) => tag.id));
  }

  protected saveMetadata(): void {
    const course = this.course();

    if (!course) {
      return;
    }

    this.form.markAllAsTouched();

    if (this.form.invalid) {
      this.errors.set([
        { field: 'title', message: 'Complete every required field before saving.' },
      ]);
      return;
    }

    const value = this.form.getRawValue();

    this.run(
      this.api.updateCourse(course.id, {
        title: value.title.trim(),
        slug: value.slug.trim(),
        summary: value.summary.trim(),
        description: value.description.trim(),
        level: value.level,
        includedInMembership: value.includedInMembership,
        imageAltText: value.imageAltText.trim() || null,
        rowVersion: course.rowVersion,
      }),
      'Course saved.',
    );
  }

  protected changeStatus(target: PublicationStatus): void {
    const course = this.course();

    if (!course) {
      return;
    }

    confirmStatusChange(this.dialog, 'course', target).subscribe((result) => {
      if (!result) {
        return;
      }

      this.run(
        this.api.changeCourseStatus(course.id, target, {
          reason: result.reason,
          rowVersion: course.rowVersion,
        }),
        `Course moved to ${target}.`,
      );
    });
  }

  protected saveTags(): void {
    const course = this.course();

    if (!course) {
      return;
    }

    this.run(
      this.api.setCourseTags(course.id, this.tagForm.controls.tagIds.value, course.rowVersion),
      'Tags updated.',
    );
  }

  protected createTag(): void {
    const name = this.tagForm.controls.newTag.value.trim();

    if (name.length === 0) {
      return;
    }

    this.busy.set(true);

    this.api.createTag(name).subscribe({
      next: (tag) => {
        this.busy.set(false);
        this.tags.update((existing) => [...existing, tag]);
        this.tagForm.controls.newTag.setValue('');
        this.tagForm.controls.tagIds.setValue([...this.tagForm.controls.tagIds.value, tag.id]);
        this.notice.set(`Tag "${tag.name}" created.`);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errors.set([
          {
            field: 'newTag',
            message: toApiFailure(error, 'The tag could not be created.').message,
          },
        ]);
      },
    });
  }

  protected addSection(): void {
    const course = this.course();

    if (!course || this.sectionForm.invalid) {
      this.sectionForm.markAllAsTouched();
      return;
    }

    this.run(
      this.api.createSection(course.id, {
        title: this.sectionForm.controls.title.value.trim(),
        description: null,
      }),
      'Section added.',
      () => this.sectionForm.reset({ title: '' }),
    );
  }

  protected dropSection(event: CdkDragDrop<unknown>): void {
    this.moveSection(event.previousIndex, event.currentIndex);
  }

  /** Applies a one-position move and sends the complete new order. */
  protected moveSection(from: number, to: number): void {
    const course = this.course();
    const sections = [...this.sections()];

    if (!course || to < 0 || to >= sections.length || from === to) {
      return;
    }

    moveItemInArray(sections, from, to);

    this.run(
      this.api.reorderSections(
        course.id,
        sections.map((section) => ({ id: section.id, rowVersion: section.rowVersion })),
      ),
      'Order saved.',
    );
  }

  private run(
    request: Observable<AdminCourseDetail>,
    successMessage?: string,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);
    this.notice.set(null);
    this.errors.set([]);

    request.subscribe({
      next: (course) => {
        this.busy.set(false);
        onSuccess?.();
        this.apply(course);

        if (successMessage) {
          this.notice.set(successMessage);
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);

        const failure = toApiFailure(error, 'That change could not be saved.');

        // A lost race is not a form problem: the operator needs the current record, not a
        // field-level message, so the UI offers a reload instead of a retry that would fail.
        this.staleWarning.set(isConcurrencyConflict(failure));
        this.errors.set(
          failure.fieldErrors.length > 0
            ? failure.fieldErrors
            : [{ field: 'title', message: failure.message }],
        );
      },
    });
  }
}
