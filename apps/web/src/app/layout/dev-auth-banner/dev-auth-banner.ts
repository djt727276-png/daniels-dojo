import { Component, inject } from '@angular/core';

import { AUTH_CONFIG, isDevelopmentAuthAllowed } from '../../core/configuration/auth-config';

/**
 * Persistent banner shown whenever the Development authentication harness is active.
 *
 * It is deliberately not dismissible: the whole point is that nobody mistakes a
 * locally signed seeded session for a real one. It renders nothing at all in a
 * production bundle, because `isDevelopmentAuthAllowed` is false there.
 */
@Component({
  selector: 'app-dev-auth-banner',
  template: `
    @if (visible) {
      <div class="dev-banner" role="status" data-testid="dev-auth-banner">
        <strong class="dev-banner__label">Development sign-in</strong>
        <span>
          This session uses a locally signed token for a seeded profile. It is not a real Entra
          account and is unavailable outside Development.
        </span>
      </div>
    }
  `,
  styles: `
    .dev-banner {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-2);
      padding: var(--dd-space-2) var(--dd-space-4);
      background: var(--dd-warning-container);
      color: var(--dd-warning);
      font-size: var(--dd-text-sm);
      border-bottom: 1px solid var(--dd-warning);
    }

    .dev-banner__label {
      font-weight: var(--dd-weight-bold);
    }
  `,
})
export class DevAuthBanner {
  protected readonly visible = isDevelopmentAuthAllowed(inject(AUTH_CONFIG));
}
