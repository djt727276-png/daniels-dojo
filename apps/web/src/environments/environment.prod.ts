import { AuthConfig } from '../app/core/configuration/auth-config';

/**
 * The real production environment, selected by the `production-prod` build configuration.
 *
 * Completely separate from `environment.production.ts`, which holds the *development
 * deployment's* cloud values: production has its own API, its own SPA/API registrations,
 * and its own origins. Every value is a public identifier or URL — the SPA is a public
 * client and holds no secret in any environment.
 *
 * At the custom-domain cutover these URLs change to https://daniels-dojo.com and
 * https://api.daniels-dojo.com in one commit, and the Entra SPA registration gains the
 * matching redirect URIs first.
 */
export const environment: {
  readonly production: boolean;
  readonly apiBaseUrl: string;
  readonly auth: AuthConfig;
} = {
  production: true,

  apiBaseUrl:
    'https://daniels-dojo-prod-api.livelyrock-d07adbec.centralus.azurecontainerapps.io/api',

  auth: {
    mode: 'entra',

    // The shared External ID tenant with production-only registrations.
    authority: 'https://danielsdojodev.ciamlogin.com/58eb0628-e4d7-440a-834f-d8c473d80004/v2.0',
    clientId: 'b409da1f-6e0e-4391-b401-7750296fb74c',
    knownAuthorities: ['danielsdojodev.ciamlogin.com'],
    apiScope: 'api://d26462c9-130e-4136-8e7d-f2ea4002c564/access_as_user',

    redirectUri: 'https://brave-flower-0f473690f.7.azurestaticapps.net/',
    postLogoutRedirectUri: 'https://brave-flower-0f473690f.7.azurestaticapps.net/',

    apiBaseUrl:
      'https://daniels-dojo-prod-api.livelyrock-d07adbec.centralus.azurecontainerapps.io/api',
  },
};
