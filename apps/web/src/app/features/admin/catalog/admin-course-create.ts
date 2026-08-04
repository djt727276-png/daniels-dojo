import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';

import { AdminCatalogApi } from '../../../core/admin/admin-catalog-api';
import { COURSE_LEVELS, CourseLevel } from '../../../core/admin/admin-catalog.model';
import { toApiFailure } from '../../../core/api/problem-details';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../../shared/ui/page-header/page-header';

/**
 * Creates a Draft course.
 *
 * A new course is always a draft, so this form asks only for the metadata the catalog needs
 * and leaves publication to the workspace, where the prerequisites can actually be met.
 */
@Component({
  selector: 'app-admin-course-create',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    PageHeader,
    FormErrorSummary,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="New course"
        description="Courses start as drafts. Nothing here is visible to students yet."
      >
        <a matButton routerLink="/admin/catalog">Cancel</a>
      </app-page-header>

      <app-form-error-summary [errors]="errors()" />

      <mat-card appearance="outlined">
        <mat-card-content>
          <form class="form dd-stack" [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Title</mat-label>
              <input matInput id="field-title" formControlName="title" data-testid="course-title" />
              @if (form.controls.title.touched && form.controls.title.invalid) {
                <mat-error>A title is required.</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Slug</mat-label>
              <input matInput id="field-slug" formControlName="slug" data-testid="course-slug" />
              <mat-hint>
                Lowercase letters, numbers, and single hyphens. Fixed once the course is published.
              </mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Summary</mat-label>
              <textarea
                matInput
                id="field-summary"
                formControlName="summary"
                rows="2"
                data-testid="course-summary"
              ></textarea>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Description</mat-label>
              <textarea
                matInput
                id="field-description"
                formControlName="description"
                rows="6"
                data-testid="course-description"
              ></textarea>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Level</mat-label>
              <mat-select id="field-level" formControlName="level">
                @for (option of levels; track option.value) {
                  <mat-option [value]="option.value">{{ option.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>

            <mat-checkbox formControlName="includedInMembership">
              Included in membership
            </mat-checkbox>

            <div class="form__actions">
              <button
                matButton="filled"
                type="submit"
                [disabled]="saving()"
                data-testid="create-course"
              >
                {{ saving() ? 'Creating…' : 'Create draft' }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .form {
      max-width: 48rem;
    }

    .form__actions {
      display: flex;
      gap: var(--dd-space-3);
    }
  `,
})
export class AdminCourseCreate {
  private readonly api = inject(AdminCatalogApi);
  private readonly router = inject(Router);

  protected readonly levels = COURSE_LEVELS;
  protected readonly saving = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    slug: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    summary: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    level: new FormControl<CourseLevel>('AllLevels', { nonNullable: true }),
    includedInMembership: new FormControl(true, { nonNullable: true }),
  });

  protected submit(): void {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      this.errors.set([
        { field: 'title', message: 'Complete every required field before creating the course.' },
      ]);
      return;
    }

    this.saving.set(true);
    this.errors.set([]);

    this.api.createCourse(this.form.getRawValue()).subscribe({
      next: (course) => {
        this.saving.set(false);
        void this.router.navigate(['/admin/catalog/courses', course.id]);
      },
      error: (error: unknown) => {
        this.saving.set(false);

        const failure = toApiFailure(error, 'The course could not be created.');
        this.errors.set(
          failure.fieldErrors.length > 0
            ? failure.fieldErrors
            : [{ field: 'title', message: failure.message }],
        );
      },
    });
  }
}
