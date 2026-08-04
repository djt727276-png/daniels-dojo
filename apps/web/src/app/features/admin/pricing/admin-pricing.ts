import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Observable } from 'rxjs';

import {
  AdminOffer,
  AdminPricingApi,
  BillingInterval,
  CommerceStatus,
  OfferKind,
  allowedCommerceTransitions,
  formatInterval,
  formatMoney,
} from '../../../core/admin/admin-pricing-api';
import { toApiFailure } from '../../../core/api/problem-details';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog/confirm-dialog';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip, StatusTone } from '../../../shared/ui/status-chip/status-chip';

type PricingState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly offers: readonly AdminOffer[] }
  | { readonly kind: 'error'; readonly message: string };

/** Maps a commerce status to a chip tone. */
function commerceTone(status: CommerceStatus): StatusTone {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Draft':
      return 'warning';
    case 'Retired':
      return 'neutral';
  }
}

/**
 * Offers and prices held in this database.
 *
 * Nothing here talks to a payment provider. Amounts are entered and stored as integer minor
 * units, and an active price is never edited in place — a change publishes a new price and
 * retires the old one, so a past order still resolves to what was actually charged.
 */
@Component({
  selector: 'app-admin-pricing',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
    FormErrorSummary,
  ],
  templateUrl: './admin-pricing.html',
  styleUrl: './admin-pricing.scss',
})
export class AdminPricing {
  private readonly api = inject(AdminPricingApi);
  private readonly dialog = inject(MatDialog);

  protected readonly tone = commerceTone;
  protected readonly money = formatMoney;
  protected readonly interval = formatInterval;
  protected readonly transitions = allowedCommerceTransitions;

  protected readonly state = signal<PricingState>({ kind: 'loading' });
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);
  protected readonly notice = signal<string | null>(null);
  protected readonly addingPriceTo = signal<string | null>(null);

  protected readonly offers = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.offers : [];
  });

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  protected readonly offerForm = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    kind: new FormControl<OfferKind>('Membership', { nonNullable: true }),
    courseId: new FormControl('', { nonNullable: true }),
  });

  protected readonly priceForm = new FormGroup({
    amountMinor: new FormControl<number | null>(null, { validators: [Validators.required] }),
    currency: new FormControl('USD', { nonNullable: true, validators: [Validators.required] }),
    billingInterval: new FormControl<BillingInterval>('Month', { nonNullable: true }),
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.listOffers().subscribe({
      next: (offers) => this.state.set({ kind: 'ready', offers }),
      error: (error: unknown) =>
        this.state.set({
          kind: 'error',
          message: toApiFailure(error, 'We could not load pricing just now.').message,
        }),
    });
  }

  protected createOffer(): void {
    this.offerForm.markAllAsTouched();

    if (this.offerForm.invalid) {
      this.errors.set([{ field: 'code', message: 'Complete every required field.' }]);
      return;
    }

    const value = this.offerForm.getRawValue();

    this.run(
      this.api.createOffer({
        code: value.code.trim(),
        name: value.name.trim(),
        description: value.description.trim(),
        kind: value.kind,
        courseId: value.kind === 'CourseLifetime' ? value.courseId.trim() || null : null,
      }),
      'Offer created as a draft.',
      () =>
        this.offerForm.reset({
          code: '',
          name: '',
          description: '',
          kind: 'Membership',
          courseId: '',
        }),
    );
  }

  protected startAddingPrice(offerId: string): void {
    this.addingPriceTo.set(this.addingPriceTo() === offerId ? null : offerId);
    this.priceForm.reset({ amountMinor: null, currency: 'USD', billingInterval: 'Month' });
  }

  protected addPrice(offer: AdminOffer): void {
    this.priceForm.markAllAsTouched();

    const value = this.priceForm.getRawValue();

    if (this.priceForm.invalid || value.amountMinor === null) {
      this.errors.set([
        { field: 'amountMinor', message: 'Enter an amount in minor units, for example 999.' },
      ]);
      return;
    }

    this.run(
      this.api.createPrice(offer.id, {
        amountMinor: value.amountMinor,
        currency: value.currency.trim().toUpperCase(),
        billingInterval: value.billingInterval,
        effectiveFromUtc: new Date().toISOString(),
      }),
      'Price added as a draft.',
      () => this.addingPriceTo.set(null),
    );
  }

  protected changeOfferStatus(offer: AdminOffer, target: CommerceStatus): void {
    this.confirm('offer', target).subscribe((reason) => {
      if (reason === null) {
        return;
      }

      this.run(
        this.api.changeOfferStatus(offer.id, target, { reason, rowVersion: offer.rowVersion }),
        `Offer moved to ${target}.`,
      );
    });
  }

  protected changePriceStatus(
    offer: AdminOffer,
    priceId: string,
    rowVersion: string,
    target: CommerceStatus,
  ): void {
    this.confirm('price', target).subscribe((reason) => {
      if (reason === null) {
        return;
      }

      this.run(
        this.api.changePriceStatus(offer.id, priceId, target, { reason, rowVersion }),
        `Price moved to ${target}.`,
      );
    });
  }

  private confirm(entity: string, target: CommerceStatus): Observable<string | null> {
    return new Observable<string | null>((subscriber) => {
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: target === 'Active' ? `Activate this ${entity}?` : `Retire this ${entity}?`,
            message:
              target === 'Active'
                ? `Activating makes this ${entity} purchasable.`
                : `Retiring is permanent. A retired ${entity} cannot be reactivated — publish a new one instead.`,
            confirmLabel: target === 'Active' ? 'Activate' : 'Retire',
            destructive: target === 'Retired',
            requireReason: true,
            reasonLabel: 'Reason (recorded in the audit trail)',
          },
          width: '32rem',
        })
        .afterClosed()
        .subscribe((result) => {
          subscriber.next(result ? result.reason : null);
          subscriber.complete();
        });
    });
  }

  private run(
    request: Observable<AdminOffer>,
    successMessage: string,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);
    this.errors.set([]);
    this.notice.set(null);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess?.();
        this.notice.set(successMessage);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);

        const failure = toApiFailure(error, 'That change could not be saved.');
        this.errors.set(
          failure.fieldErrors.length > 0
            ? failure.fieldErrors
            : [{ field: 'code', message: failure.message }],
        );
      },
    });
  }
}
