import { AuthConfig } from '../app/core/configuration/auth-config';

/**
 * The real production environment, selected by the `production-prod` build configuration.
 *
 * Completely separate from `environment.production.ts`, which holds the *development
 * deployment's* cloud values: production has its own API, its own SPA/API registrations,
 * and its own origins. Every value is a public identifier or URL — the SPA is a public
 * client and holds no secret in any environment.
 *
 * The canonical origin is the apex. www redirects to it at the edge, so the apex is the
 * only origin a browser actually lands on and therefore the only redirect URI the flow
 * needs — the Entra registration also accepts the www form so a redirect that arrives
 * mid-flow cannot strand anyone.
 */
export const environment: {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly auth: AuthConfig;
} = {
  production: true,

  apiBaseUrl: 'https://api.daniels-dojo.com/api',

  auth: {
    mode: 'entra',

    // The shared External ID tenant with production-only registrations.
    authority: 'https://danielsdojodev.ciamlogin.com/58eb0628-e4d7-440a-834f-d8c473d80004/v2.0',
    clientId: 'b409da1f-6e0e-4391-b401-7750296fb74c',
    knownAuthorities: ['danielsdojodev.ciamlogin.com'],
    apiScope: 'api://d26462c9-130e-4136-8e7d-f2ea4002c564/access_as_user',

    redirectUri: 'https://daniels-dojo.com/',
    postLogoutRedirectUri: 'https://daniels-dojo.com/',

    apiBaseUrl: 'https://api.daniels-dojo.com/api',
  },
};
