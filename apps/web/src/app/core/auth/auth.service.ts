import { Injectable, computed, inject, signal } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import { RedirectRequest } from '@azure/msal-browser';

import { AUTH_CONFIG, isAuthConfigured } from '../configuration/auth-config';
import { SessionApi } from './session-api';
import { ROLE_ADMIN, Session, SessionState } from './session.model';

/**
 * Owns sign-in, sign-out, and the session the UI renders.
 *
 * Authorization decisions are never made here from token contents. The service asks the API
 * who the caller is and what roles they hold; the token is only ever a bearer credential
 * handled by MSAL and the interceptor.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MsalService);
  private readonly sessionApi = inject(SessionApi);
  private readonly config = inject(AUTH_CONFIG);

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

  /** Whether enough public configuration exists to attempt a real sign-in. */
  readonly isConfigured = computed(() => isAuthConfigured(this.config));

  /** Whether MSAL currently holds an account. */
  hasAccount(): boolean {
    return this.msal.instance.getAllAccounts().length > 0;
  }

  /** Starts the redirect sign-up/sign-in flow against the configured user flow. */
  signIn(): void {
    if (!this.isConfigured()) {
      this.state.set({ kind: 'error' });
      return;
    }

    const request: RedirectRequest = { scopes: [this.config.apiScope] };
    this.msal.loginRedirect(request);
  }

  /** Signs out and returns the browser to the configured post-logout URI. */
  signOut(): void {
    this.state.set({ kind: 'signedOut' });
    this.msal.logoutRedirect({
      postLogoutRedirectUri: this.config.postLogoutRedirectUri,
    });
  }

  /**
   * Loads the session from the API. Called after redirect handling and on app start.
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
      return { kind: 'signedOut' };
    }

    if (status === 403) {
      return { kind: 'forbidden' };
    }

    return { kind: 'error' };
  }
}
