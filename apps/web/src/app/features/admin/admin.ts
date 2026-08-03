import { Component, inject } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Administrator landing page.
 *
 * Reaching this route requires the Admin role the API reported. The route guard is convenience
 * only — every administrative API call is authorized again on the server against the local
 * database.
 */
@Component({
  selector: 'app-admin',
  template: `
    <section class="admin" aria-labelledby="admin-title">
      <h1 id="admin-title">Administration</h1>
      <p data-testid="admin-greeting">
        Signed in as {{ session()?.displayName }} with administrator access.
      </p>
    </section>
  `,
  styles: `
    .admin {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
  `,
})
export class Admin {
  private readonly auth = inject(AuthService);

  protected readonly session = this.auth.session;
}
