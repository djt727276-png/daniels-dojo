import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';

/** Lifecycle of an offer or price. */
export type CommerceStatus = 'Draft' | 'Active' | 'Retired';

/** What an offer sells. */
export type OfferKind = 'Membership' | 'CourseLifetime';

/** How often a price is charged. */
export type BillingInterval = 'OneTime' | 'Month';

/** A price as the pricing screen sees it. There is deliberately no provider identifier. */
export interface AdminPrice {
  readonly id: string;
  readonly amountMinor: number;
  readonly currency: string;
  readonly billingInterval: BillingInterval;
  readonly billingIntervalCount: number;
  readonly status: CommerceStatus;
  readonly effectiveFromUtc: string;
  readonly retiredAtUtc: string | null;
  readonly editable: boolean;
  readonly rowVersion: string;
}

/** An offer and every price published beneath it. */
export interface AdminOffer {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description: string;
  readonly kind: OfferKind;
  readonly courseId: string | null;
  readonly courseTitle: string | null;
  readonly status: CommerceStatus;
  readonly providerLinked: boolean;
  readonly commercialFieldsEditable: boolean;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly prices: readonly AdminPrice[];
  readonly rowVersion: string;
}

/** Creates a Draft offer. */
export interface CreateOfferRequest {
  readonly code: string;
  readonly name: string;
  readonly description: string;
  readonly kind: OfferKind;
  readonly courseId: string | null;
}

/** Updates an offer. Code and course are only accepted while it is a draft. */
export interface UpdateOfferRequest {
  readonly code: string;
  readonly name: string;
  readonly description: string;
  readonly courseId: string | null;
  readonly rowVersion: string;
}

/** Publishes a new Draft price. */
export interface CreatePriceRequest {
  readonly amountMinor: number;
  readonly currency: string;
  readonly billingInterval: BillingInterval;
  readonly effectiveFromUtc: string;
}

/** A commerce status change, with the reason the audit trail records. */
export interface CommerceStatusChangeRequest {
  readonly reason: string;
  readonly rowVersion: string;
}

/** Which commerce transitions the API will accept. Retirement is terminal. */
export function allowedCommerceTransitions(current: CommerceStatus): readonly CommerceStatus[] {
  switch (current) {
    case 'Draft':
      return ['Active', 'Retired'];
    case 'Active':
      return ['Retired'];
    case 'Retired':
      return [];
  }
}

/**
 * Formats a stored amount for display.
 *
 * The amount arrives as integer minor units plus its currency, and the browser's own
 * formatter decides the symbol and decimal placement. No amount, symbol, or exponent is
 * hard-coded here.
 */
export function formatMoney(amountMinor: number, currency: string): string {
  const formatter = new Intl.NumberFormat(undefined, { style: 'currency', currency });
  const digits = formatter.resolvedOptions().maximumFractionDigits ?? 2;

  return formatter.format(amountMinor / 10 ** digits);
}

/** Human-readable billing interval. */
export function formatInterval(interval: BillingInterval): string {
  return interval === 'Month' ? 'per month' : 'one time';
}

/** Typed client for the Admin pricing endpoints. Nothing here touches a payment provider. */
@Injectable({ providedIn: 'root' })
export class AdminPricingApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(API_BASE_PATH)}/v1/admin/pricing`;

  listOffers(): Observable<readonly AdminOffer[]> {
    return this.http.get<readonly AdminOffer[]>(`${this.root}/offers`);
  }

  createOffer(request: CreateOfferRequest): Observable<AdminOffer> {
    return this.http.post<AdminOffer>(`${this.root}/offers`, request);
  }

  updateOffer(offerId: string, request: UpdateOfferRequest): Observable<AdminOffer> {
    return this.http.put<AdminOffer>(`${this.root}/offers/${offerId}`, request);
  }

  changeOfferStatus(
    offerId: string,
    target: CommerceStatus,
    request: CommerceStatusChangeRequest,
  ): Observable<AdminOffer> {
    return this.http.post<AdminOffer>(`${this.root}/offers/${offerId}/status/${target}`, request);
  }

  createPrice(offerId: string, request: CreatePriceRequest): Observable<AdminOffer> {
    return this.http.post<AdminOffer>(`${this.root}/offers/${offerId}/prices`, request);
  }

  changePriceStatus(
    offerId: string,
    priceId: string,
    target: CommerceStatus,
    request: CommerceStatusChangeRequest,
  ): Observable<AdminOffer> {
    return this.http.post<AdminOffer>(
      `${this.root}/offers/${offerId}/prices/${priceId}/status/${target}`,
      request,
    );
  }
}
