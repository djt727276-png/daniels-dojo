import { Injectable, computed, inject, signal } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import { RedirectRequest } from '@azure/msal-browser';

import {
  AUTH_CONFIG,
  isAuthConfigured,
  isDevelopmentAuthAllowed,
  isEntraConfigured,
} from '../configuration/auth-config';
import { DevelopmentAuthClient, DevelopmentProfileKey } from './development-auth';
import { SessionApi } from './session-api';
import { ROLE_ADMIN, Session, SessionState } from './session.model';

/**
 * Owns sign-in, sign-out, and the session the UI renders.
 *
 * Both authentication modes are hidden behind this one service, and both produce the
 * same `/api/v1/auth/session` contract, so no screen branches on how the token was
 * obtained. Authorization decisions are never made here from token contents — the
 * service asks the API who the caller is and what roles they hold.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MsalService);
  private readonly sessionApi = inject(SessionApi);
  private readonly config = inject(AUTH_CONFIG);
  private readonly developmentAuth = inject(DevelopmentAuthClient);

  private readonly state = signal<SessionState>({ kind: 'loading' });

  /** Current session state for the UI. */
  readonly sessionState = this.state.asReadonly();

  /** The signed-in session, or null. */
  readonly session = computed<Session | null>(() => {
    const current = this.state();
    return current.kind === 'signedIn' ? current.session : null;
  });

  /** Whether the API reported the Admin role for this user. */
  readonly isAdmin = computed(() => this.session()?.roles.includes(ROLE_ADMIN) ?? false);

  /** Whether the app can authenticate at all in its current configuration. */
  readonly isConfigured = computed(() => isAuthConfigured(this.config));

  /** Whether the Development harness is the active mode. */
  readonly isDevelopmentMode = isDevelopmentAuthAllowed(this.config);

  /** Whether MSAL or the Development harness currently holds a credential. */
  hasAccount(): boolean {
    return this.isDevelopmentMode
      ? this.developmentAuth.read() !== null
      : this.msal.instance.getAllAccounts().length > 0;
  }

  /**
   * Starts the Entra redirect flow. Not used in Development mode, where the caller
   * uses {@link signInAsDevelopmentProfile} instead.
   */
  signIn(): void {
    if (!isEntraConfigured(this.config)) {
      this.state.set({ kind: 'error' });
      return;
    }

    const request: RedirectRequest = { scopes: [this.config.apiScope] };
    this.msal.loginRedirect(request);
  }

  /**
   * Starts the same hosted flow opened directly on its registration page.
   *
   * `prompt=create` is the External ID signal for "this person is new" — the customer lands
   * on account creation with email verification instead of a sign-in form that would tell a
   * newcomer their account does not exist.
   */
  createAccount(): void {
    if (!isEntraConfigured(this.config)) {
      this.state.set({ kind: 'error' });
      return;
    }

    const request: RedirectRequest = { scopes: [this.config.apiScope], prompt: 'create' };
    this.msal.loginRedirect(request);
  }

  /** Signs in as one of the two seeded Development profiles. */
  signInAsDevelopmentProfile(profile: DevelopmentProfileKey): void {
    if (!this.isDevelopmentMode) {
      this.state.set({ kind: 'error' });
      return;
    }

    this.state.set({ kind: 'loading' });

    this.developmentAuth.signIn(profile).subscribe({
      next: () => this.refreshSession(),
      error: () => this.state.set({ kind: 'error' }),
    });
  }

  /** Signs out of whichever mode is active. */
  signOut(): void {
    this.state.set({ kind: 'signedOut' });

    // Always cleared, in either mode, so a stale local token can never outlive a session.
    this.developmentAuth.clear();

    if (!this.isDevelopmentMode) {
      this.msal.logoutRedirect({
        postLogoutRedirectUri: this.config.postLogoutRedirectUri,
      });
    }
  }

  /**
   * Loads the session from the API.
   *
   * A 401 or 403 is an ordinary signed-out or refused state, not a crash: the UI shows a
   * recoverable message and no error body is surfaced to the user.
   */
  refreshSession(): void {
    if (!this.hasAccount()) {
      this.state.set({ kind: 'signedOut' });
      return;
    }

    this.state.set({ kind: 'loading' });

    this.sessionApi.getSession().subscribe({
      next: (session) => this.state.set({ kind: 'signedIn', session }),
      error: (error: unknown) => this.state.set(this.toFailureState(error)),
    });
  }

  /**
   * Maps a failed session call to a state. Nothing from the error is stored or rendered —
   * an error body could contain a token, an authorization code, or claim material.
   */
  private toFailureState(error: unknown): SessionState {
    const status = (error as { status?: number } | null)?.status;

    if (status === 401) {
      // The credential is no longer accepted; drop it rather than retrying with it.
      this.developmentAuth.clear();
      return { kind: 'signedOut' };
    }

    if (status === 403) {
      return { kind: 'forbidden' };
    }

    return { kind: 'error' };
  }
}
