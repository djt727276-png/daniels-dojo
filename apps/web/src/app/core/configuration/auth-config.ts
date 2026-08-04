import { InjectionToken } from '@angular/core';

import { environment } from '../../../environments/environment';

/**
 * How the browser obtains an access token.
 *
 * `entra` is the only mode a production build may use. `development` selects the
 * local seeded-profile harness, which exists so the product can be exercised end to
 * end before the external tenant is configured.
 */
export type AuthMode = 'entra' | 'development';

/**
 * Public Entra External ID settings for the browser.
 *
 * Every value here is a public identifier or URL. A SPA is a public client: it holds no
 * secret, and none may ever be added to this file or to any build configuration.
 */
export interface AuthConfig {
  /**
   * Selects the token source. The production environment file pins this to `entra`;
   * see {@link isDevelopmentAuthAllowed}, which additionally refuses the development
   * harness whenever the bundle was built for production.
   */
  readonly mode: AuthMode;

  /** External ID authority for the customer user flow. */
  readonly authority: string;

  /** Application (client) ID of this SPA registration. */
  readonly clientId: string;

  /** Where External ID returns the browser after sign-in. */
  readonly redirectUri: string;

  /** Where External ID returns the browser after sign-out. */
  readonly postLogoutRedirectUri: string;

  /**
   * Hosts the authority is allowed to redirect through. MSAL refuses authorities outside
   * this list, which stops a mistyped or attacker-supplied authority being trusted.
   */
  readonly knownAuthorities: readonly string[];

  /** Exact API origin. The interceptor attaches a token to this origin and nothing else. */
  readonly apiBaseUrl: string;

  /** Full delegated scope requested for the API, `api://<API_CLIENT_ID>/access_as_user`. */
  readonly apiScope: string;
}

/**
 * Injected auth configuration.
 *
 * The value comes from `src/environments/environment.ts`, which the production build swaps
 * for `environment.production.ts` through `fileReplacements` in `angular.json`. Deployment
 * values are therefore supplied by the environment file for that deployment — this shared
 * source file never needs editing, and development and production settings stay separate.
 *
 * Tests override the token directly.
 */
export const AUTH_CONFIG = new InjectionToken<AuthConfig>('AUTH_CONFIG', {
  providedIn: 'root',
  factory: () => environment.auth,
});

/**
 * Whether the Development authentication harness may run.
 *
 * Two independent conditions, both required: the environment must request the mode,
 * **and** the bundle must not be a production build. `environment.production` is
 * compiled in through `fileReplacements`, so a production bundle cannot activate the
 * harness even if someone edits the mode — the second condition is constant-folded
 * to false at build time.
 */
export function isDevelopmentAuthAllowed(config: AuthConfig): boolean {
  return config.mode === 'development' && !environment.production;
}

/** Whether enough configuration is present to attempt a real Entra sign-in. */
export function isEntraConfigured(config: AuthConfig): boolean {
  return config.authority.length > 0 && config.clientId.length > 0 && config.apiScope.length > 0;
}

/**
 * Whether the app can authenticate at all in its current configuration.
 *
 * Kept as the single question the UI asks, so screens do not branch on mode.
 */
export function isAuthConfigured(config: AuthConfig): boolean {
  return isDevelopmentAuthAllowed(config) || isEntraConfigured(config);
}
