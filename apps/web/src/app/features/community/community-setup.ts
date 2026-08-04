import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { MemberApi } from '../../core/community/member-api';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * One-time community profile setup.
 *
 * Collects a handle, an optional bio, guidelines acceptance, and an age attestation — and
 * nothing else. No date of birth is asked for anywhere, because the attestation is all the
 * policy needs and a birth date would be personal data with no further use.
 *
 * Discovery, friend requests, and messages all stay switched off after setup. Opening them up
 * is a separate, deliberate choice on the account page.
 */
@Component({
  selector: 'app-community-setup',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    PageHeader,
    FormErrorSummary,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Set up your community profile"
        description="Choose a handle and accept the guidelines. You can take part once this is done."
      >
        <a matButton routerLink="/dashboard">Back</a>
      </app-page-header>

      <app-form-error-summary [errors]="errors()" />

      <mat-card appearance="outlined">
        <mat-card-content>
          <form class="setup dd-stack" [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Handle</mat-label>
              <input matInput id="field-handle" formControlName="handle" data-testid="handle" />
              <mat-hint>
                3 to 32 characters: letters, numbers, and single hyphens or underscores. Other
                members will see this.
              </mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Bio (optional)</mat-label>
              <textarea matInput id="field-bio" rows="3" formControlName="bio"></textarea>
            </mat-form-field>

            <mat-checkbox formControlName="acceptGuidelines" data-testid="accept-guidelines">
              I have read and accept the community guidelines.
            </mat-checkbox>

            <mat-checkbox formControlName="attestEligibility" data-testid="attest-eligibility">
              I confirm I meet the minimum age for this community.
            </mat-checkbox>

            <p class="setup__privacy" data-testid="privacy-note">
              Your profile starts private. You will not appear in member search, and nobody can send
              you a friend request or a message until you change that on your account page. We do
              not ask for or store your date of birth.
            </p>

            <div>
              <button
                matButton="filled"
                type="submit"
                [disabled]="saving()"
                data-testid="complete-setup"
              >
                {{ saving() ? 'Setting up…' : 'Finish setup' }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .setup {
      max-width: 40rem;
    }

    .setup__privacy {
      max-width: var(--dd-reading-max);
      color: var(--dd-on-surface-variant);
    }
  `,
})
export class CommunitySetup {
  private readonly api = inject(MemberApi);
  private readonly router = inject(Router);

  protected readonly saving = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);

  protected readonly form = new FormGroup({
    handle: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    bio: new FormControl('', { nonNullable: true }),
    acceptGuidelines: new FormControl(false, { nonNullable: true }),
    attestEligibility: new FormControl(false, { nonNullable: true }),
  });

  protected submit(): void {
    this.form.markAllAsTouched();
    const value = this.form.getRawValue();

    if (this.form.invalid) {
      this.errors.set([{ field: 'handle', message: 'Choose a handle to continue.' }]);
      return;
    }

    this.saving.set(true);
    this.errors.set([]);

    this.api
      .completeCommunitySetup({
        handle: value.handle.trim(),
        bio: value.bio.trim() || null,
        acceptGuidelines: value.acceptGuidelines,
        attestEligibility: value.attestEligibility,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          void this.router.navigate(['/community']);
        },
        error: (error: unknown) => {
          this.saving.set(false);

          const failure = toApiFailure(error, 'We could not finish setting up your profile.');
          this.errors.set(
            failure.fieldErrors.length > 0
              ? failure.fieldErrors
              : [{ field: 'handle', message: failure.message }],
          );
        },
      });
  }
}
