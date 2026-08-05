import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { BillingApi } from '../../core/commerce/billing-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The deterministic provider's stand-in for a hosted checkout page.
 *
 * In Stripe mode the customer never sees this — they are on Stripe's own page. Here the
 * "Pay" button hits the API route that only exists while the deterministic adapter is the
 * configured provider, then returns through the same confirm path a real payment would.
 * No card fields exist because no card is ever entered into this application in any mode.
 */
@Component({
  selector: 'app-deterministic-checkout',
  imports: [RouterLink, MatCardModule, MatButtonModule, PageHeader],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Development checkout"
        description="The deterministic payment stand-in. In production this page is Stripe's."
      />

      <mat-card appearance="outlined" class="checkout">
        <mat-card-content class="dd-stack">
          <p>
            This environment uses the deterministic payment adapter, so there is nothing to actually
            pay. Choosing "Pay" marks this checkout session as paid and returns you to My Learning,
            exactly as a completed card payment would.
          </p>

          @if (failure(); as message) {
            <p class="checkout__error" role="alert" data-testid="pay-error">{{ message }}</p>
          }

          <div class="checkout__actions">
            <button
              matButton="filled"
              type="button"
              [disabled]="busy()"
              (click)="pay()"
              data-testid="deterministic-pay"
            >
              {{ busy() ? 'Paying…' : 'Pay' }}
            </button>
            <a matButton routerLink="/courses">Cancel</a>
          </div>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .checkout {
      max-width: 40rem;
    }

    .checkout__error {
      color: var(--dd-danger);
    }

    .checkout__actions {
      display: flex;
      gap: var(--dd-space-3);
    }
  `,
})
export class DeterministicCheckout {
  private readonly billing = inject(BillingApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  private readonly sessionId = this.route.snapshot.paramMap.get('sessionId') ?? '';

  protected pay(): void {
    this.busy.set(true);
    this.failure.set(null);

    this.billing.payDeterministic(this.sessionId).subscribe({
      next: () => {
        // Mimic the provider's return: land on My Learning with the session to confirm.
        void this.router.navigate(['/my-learning'], {
          queryParams: { checkout: 'success', session_id: this.sessionId },
        });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(toApiFailure(error, 'This checkout could not be completed here.').message);
      },
    });
  }
}
