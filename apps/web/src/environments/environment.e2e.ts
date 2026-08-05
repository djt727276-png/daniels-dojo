import { AuthConfig } from '../app/core/configuration/auth-config';

/**
 * End-to-end test environment, selected by the `e2e` build configuration.
 *
 * Identical to development except that the seeded-profile Development harness is the
 * active token source, so Playwright can sign in as the seeded admin and student without
 * any real credential. `isDevelopmentAuthAllowed` still refuses this mode in a production
 * bundle, and the API's token endpoint only exists in its Development environment — two
 * independent locks this file cannot open in a deployed system.
 */
export const environment: {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly auth: AuthConfig;
} = {
  production: false,
  apiBaseUrl: '/api',

  auth: {
    mode: 'development',
    authority: '',
    clientId: '',
    knownAuthorities: [],
    apiScope: '',
    redirectUri: 'http://localhost:4200/',
    postLogoutRedirectUri: 'http://localhost:4200/',
    apiBaseUrl: '/api',
  },
};
