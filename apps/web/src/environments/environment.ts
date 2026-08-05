import { AuthConfig } from '../app/core/configuration/auth-config';

/**
 * Development environment settings.
 *
 * This file is the development half of the Angular `fileReplacements` mechanism: the
 * production build swaps it for `environment.production.ts`. Application code never reads
 * either file directly — it injects `AUTH_CONFIG` — so deployment values are supplied here
 * rather than by editing shared source.
 *
 * Every value is a public identifier or URL. A SPA is a public client: **no secret, password,
 * token, or private key may ever be placed in this file.**
 */
export const environment: {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly auth: AuthConfig;
} = {
  production: false,

  // Relative: the dev-server proxy forwards /api to the local API on 127.0.0.1:5000.
  apiBaseUrl: '/api',

  auth: {
    // Real customer sign-up/sign-in against the Daniel's Dojo development External ID
    // tenant. The seeded-profile harness remains reachable at /development-login as a
    // fallback, but the account page uses the genuine hosted flow.
    mode: 'entra',

    // Public identifiers for the development customer tenant. Not secrets.
    authority: 'https://danielsdojodev.ciamlogin.com/58eb0628-e4d7-440a-834f-d8c473d80004/v2.0',
    clientId: 'd3529e4c-1544-4a3a-bb97-3f018e155446',
    knownAuthorities: ['danielsdojodev.ciamlogin.com'],
    apiScope: 'api://1495cace-2d44-4eda-b85b-7a27b561a0d6/access_as_user',

    redirectUri: 'http://localhost:4200/',
    postLogoutRedirectUri: 'http://localhost:4200/',

    // Relative in development so the Angular dev-server proxy forwards to the local API.
    apiBaseUrl: '/api',
  },
};
