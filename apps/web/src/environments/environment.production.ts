import { AuthConfig } from '../app/core/configuration/auth-config';

/**
 * Production environment settings, substituted for `environment.ts` by the `production`
 * build configuration in `angular.json`.
 *
 * Fill these in for the deployment. Every value is a public identifier or URL — the SPA is a
 * public client. **No secret, password, token, private key, or production credential may ever
 * be placed in this file**, and nothing here is a substitute for the API's own validation.
 *
 * Leaving these empty is safe: the app builds and runs, the account page reports that sign-in
 * is not configured, and no redirect is attempted against a meaningless authority.
 */
export const environment: { readonly production: boolean; readonly auth: AuthConfig } = {
  production: true,

  auth: {
    // e.g. 'https://<subdomain>.ciamlogin.com/<tenant-id>/v2.0'
    authority: '',

    // SPA app registration (client) ID.
    clientId: '',

    // e.g. ['<subdomain>.ciamlogin.com'] — authorities outside this list are refused by MSAL.
    knownAuthorities: [],

    // e.g. 'api://<api-client-id>/access_as_user'
    apiScope: '',

    // Must exactly match the redirect URIs registered in the portal.
    redirectUri: '',
    postLogoutRedirectUri: '',

    // Exact API origin and base path. The interceptor attaches a token to this and nothing else.
    apiBaseUrl: '',
  },
};
