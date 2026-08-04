import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

/** Input for {@link ConfirmDialog}. */
export interface ConfirmDialogData {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel: string;
  readonly cancelLabel?: string;

  /** Styles the confirm button as destructive. */
  readonly destructive?: boolean;

  /**
   * When set, the operator must type a reason before confirming. Used for
   * publication and moderation actions, which the API also requires a reason
   * for — this is the UI half of that contract.
   */
  readonly requireReason?: boolean;

  /** Label for the reason field when {@link requireReason} is set. */
  readonly reasonLabel?: string;
}

/** Result of {@link ConfirmDialog}. Undefined when dismissed. */
export interface ConfirmDialogResult {
  readonly reason: string;
}

/**
 * Focus-managed confirmation dialog.
 *
 * Material's dialog traps focus, restores it to the trigger on close, and wires
 * `aria-labelledby`/`aria-describedby` from the title and content directives,
 * so keyboard and screen-reader behaviour comes from the framework rather than
 * being re-implemented here.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>

    <mat-dialog-content>
      <p class="dialog__message">{{ data.message }}</p>

      @if (data.requireReason) {
        <mat-form-field appearance="outline" class="dialog__reason">
          <mat-label>{{ data.reasonLabel ?? 'Reason' }}</mat-label>
          <textarea
            matInput
            [formControl]="reason"
            rows="3"
            required
            data-testid="confirm-reason"
          ></textarea>
          @if (reason.touched && reason.invalid) {
            <mat-error>A reason is required and is recorded in the audit trail.</mat-error>
          }
        </mat-form-field>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton type="button" (click)="cancel()">
        {{ data.cancelLabel ?? 'Cancel' }}
      </button>
      <button
        matButton="filled"
        type="button"
        [class.dialog__confirm--destructive]="data.destructive"
        [disabled]="data.requireReason && reason.invalid"
        (click)="confirm()"
        data-testid="confirm-accept"
      >
        {{ data.confirmLabel }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .dialog__message {
      max-width: 40rem;
      margin-bottom: var(--dd-space-4);
    }

    .dialog__reason {
      width: 100%;
    }

    .dialog__confirm--destructive {
      --mat-button-filled-container-color: var(--dd-danger);
      --mat-button-filled-label-text-color: var(--dd-on-primary);
    }
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);

  private readonly dialogRef =
    inject<MatDialogRef<ConfirmDialog, ConfirmDialogResult | undefined>>(MatDialogRef);

  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(3), Validators.maxLength(512)],
  });

  protected cancel(): void {
    this.dialogRef.close(undefined);
  }

  protected confirm(): void {
    if (this.data.requireReason && this.reason.invalid) {
      this.reason.markAsTouched();
      return;
    }

    this.dialogRef.close({ reason: this.reason.value.trim() });
  }
}
