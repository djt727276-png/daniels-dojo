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
export const environment: {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly auth: AuthConfig;
} = {
  production: true,

  // The deployed development API. A public URL, not a secret. At domain cutover this becomes
  // https://api.<domain>/api and production builds get their own value.
  apiBaseUrl: 'https://daniels-dojo-dev-api.bluesea-b5b5b44c.eastus2.azurecontainerapps.io/api',

  auth: {
    // Entra only. The Development harness is not a production token source, and
    // `isDevelopmentAuthAllowed` refuses it in a production bundle regardless of this value.
    mode: 'entra',

    // The Daniel's Dojo development customer tenant. Public identifiers, not secrets. A
    // real production deployment gets its own tenant and its own values here.
    authority: 'https://danielsdojodev.ciamlogin.com/58eb0628-e4d7-440a-834f-d8c473d80004/v2.0',
    clientId: 'd3529e4c-1544-4a3a-bb97-3f018e155446',
    knownAuthorities: ['danielsdojodev.ciamlogin.com'],
    apiScope: 'api://1495cace-2d44-4eda-b85b-7a27b561a0d6/access_as_user',

    // Must exactly match the redirect URIs registered on the SPA registration.
    redirectUri: 'https://yellow-wave-0ef59fd0f.7.azurestaticapps.net/',
    postLogoutRedirectUri: 'https://yellow-wave-0ef59fd0f.7.azurestaticapps.net/',

    // Exact API origin and base path. The interceptor attaches a token to this and nothing else.
    apiBaseUrl: 'https://daniels-dojo-dev-api.bluesea-b5b5b44c.eastus2.azurecontainerapps.io/api',
  },
};
