import { Component, inject } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Account page. Renders the session the API reported and offers sign-in and sign-out.
 *
 * Every displayed value comes from `/api/v1/auth/session`; the access token is never decoded
 * in the browser.
 */
@Component({
  selector: 'app-account',
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  private readonly auth = inject(AuthService);

  protected readonly state = this.auth.sessionState;
  protected readonly session = this.auth.session;
  protected readonly isConfigured = this.auth.isConfigured;
  protected readonly isAdmin = this.auth.isAdmin;

  protected signIn(): void {
    this.auth.signIn();
  }

  protected signOut(): void {
    this.auth.signOut();
  }

  protected retry(): void {
    this.auth.refreshSession();
  }
}
