/**
 * Shape of `GET /api/v1/auth/session`. Mirrors the backend contract exactly.
 *
 * Roles come from this response — the local database's decision — and never from decoding the
 * access token in the browser.
 */
export interface Session {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly roles: readonly string[];
}

/** What the account UI is currently showing. */
export type SessionState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'signedOut' }
  | { readonly kind: 'signedIn'; readonly session: Session }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'error' };

/** Application role names, matching the seeded local roles. */
export const ROLE_STUDENT = 'Student';

/** Administrator role name. */
export const ROLE_ADMIN = 'Admin';
