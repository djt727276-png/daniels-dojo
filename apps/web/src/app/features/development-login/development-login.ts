import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';

import { AuthService } from '../../core/auth/auth.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * Development-only sign-in screen.
 *
 * There are no credential inputs of any kind — the two buttons ask the API for a token for a
 * seeded profile, and the API accepts only those two fixed keys. Nothing here can request an
 * arbitrary user, role, or claim.
 *
 * The page renders a plain explanation when the harness is not the active mode, which is what
 * a production bundle always sees.
 */
@Component({
  selector: 'app-development-login',
  imports: [MatButtonModule, MatCardModule, PageHeader],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Development sign-in"
        description="Continue as one of the seeded local profiles. No password is involved: Daniel's Dojo never stores credentials."
      />

      @if (!isDevelopmentMode) {
        <p data-testid="dev-login-unavailable">
          Development sign-in is not available in this build. Use the account page to sign in with
          your real account.
        </p>
      } @else {
        <mat-card appearance="outlined">
          <mat-card-content class="choices">
            <button
              matButton="filled"
              type="button"
              (click)="continueAs('admin')"
              data-testid="continue-as-admin"
            >
              Continue as Admin
            </button>
            <button
              matButton="outlined"
              type="button"
              (click)="continueAs('student')"
              data-testid="continue-as-student"
            >
              Continue as Student
            </button>
          </mat-card-content>
        </mat-card>

        @if (state().kind === 'error') {
          <p role="alert" data-testid="dev-login-error">
            That sign-in did not complete. Check that the API is running in Development with the
            harness enabled, then try again.
          </p>
        }
      }
    </div>
  `,
  styles: `
    .choices {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
    }
  `,
})
export class DevelopmentLogin {
  private readonly auth = inject(AuthService);

  protected readonly isDevelopmentMode = this.auth.isDevelopmentMode;
  protected readonly state = this.auth.sessionState;

  protected continueAs(profile: 'admin' | 'student'): void {
    this.auth.signInAsDevelopmentProfile(profile);
  }
}
