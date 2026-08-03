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
export const environment: { readonly production: boolean; readonly auth: AuthConfig } = {
  production: false,

  auth: {
    // Empty until a real external tenant is configured. No tenant or client ID is invented.
    authority: '',
    clientId: '',
    knownAuthorities: [],
    apiScope: '',

    redirectUri: 'http://localhost:4200/',
    postLogoutRedirectUri: 'http://localhost:4200/',

    // Relative in development so the Angular dev-server proxy forwards to the local API.
    apiBaseUrl: '/api',
  },
};
