import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';

/** Where to send the customer to pay. Used once, never logged. */
export interface CheckoutStarted {
  readonly checkoutUrl: string;
}

/** Where to send the customer to manage billing. Used once, never logged. */
export interface PortalStarted {
  readonly portalUrl: string;
}

/** The outcome of confirming a checkout after the browser returned. */
export interface CheckoutConfirmed {
  readonly confirmed: boolean;
  readonly entitlementGranted: boolean;
}

/** One order in the customer's history. */
export interface OrderSummary {
  readonly id: string;
  readonly status: string;
  readonly totalMinor: number;
  readonly currency: string;
  readonly offerName: string;
  readonly paidAtUtc: string | null;
}

/** The customer's membership, as verified provider state. */
export interface MembershipSummary {
  readonly status: string;
  readonly currentPeriodEndUtc: string;
  readonly cancelAtPeriodEnd: boolean;
}

/** The customer's commerce standing, for the account screen. */
export interface BillingOverview {
  readonly membership: MembershipSummary | null;
  readonly orders: readonly OrderSummary[];
}

/** Error codes the commerce surface reports. */
export const COMMERCE_PROVIDER_DISABLED = 'commerce.provider_disabled';
export const COMMERCE_ALREADY_OWNED = 'commerce.already_owned';

/**
 * Typed client for purchasing.
 *
 * The client never talks to the payment provider: it asks the API to start a hosted
 * checkout or portal session and follows the returned URL. Card details never pass
 * through this application in any mode.
 */
@Injectable({ providedIn: 'root' })
export class BillingApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(API_BASE_PATH)}/v1/billing`;

  startCheckout(offerId: string): Observable<CheckoutStarted> {
    return this.http.post<CheckoutStarted>(`${this.root}/checkout`, { offerId });
  }

  confirmCheckout(sessionId: string): Observable<CheckoutConfirmed> {
    return this.http.post<CheckoutConfirmed>(
      `${this.root}/checkout/${encodeURIComponent(sessionId)}/confirm`,
      {},
    );
  }

  startPortal(): Observable<PortalStarted> {
    return this.http.post<PortalStarted>(`${this.root}/portal`, {});
  }

  getOverview(): Observable<BillingOverview> {
    return this.http.get<BillingOverview>(this.root);
  }

  /** The stand-in "pay" of the deterministic provider. The route only exists in that mode. */
  payDeterministic(sessionId: string): Observable<void> {
    return this.http.post<void>(
      `${this.root}/deterministic/${encodeURIComponent(sessionId)}/pay`,
      {},
    );
  }
}
