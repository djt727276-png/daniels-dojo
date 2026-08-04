import { EnvironmentProviders, Provider, inject, provideAppInitializer } from '@angular/core';
import { MSAL_INSTANCE, MsalBroadcastService, MsalService } from '@azure/msal-angular';
import {
  BrowserCacheLocation,
  IPublicClientApplication,
  LogLevel,
  PublicClientApplication,
} from '@azure/msal-browser';

import {
  AUTH_CONFIG,
  AuthConfig,
  isDevelopmentAuthAllowed,
  isEntraConfigured,
} from '../configuration/auth-config';
import { AuthService } from './auth.service';

/**
 * Builds the MSAL client.
 *
 * The cache lives in `sessionStorage`, so tokens are scoped to the tab and disappear when it
 * closes — `localStorage` would leave them readable for far longer than a session needs.
 * The application never persists a token itself; MSAL owns every credential.
 */
export function createMsalInstance(config: AuthConfig): IPublicClientApplication {
  return new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      knownAuthorities: [...config.knownAuthorities],
      redirectUri: config.redirectUri,
      postLogoutRedirectUri: config.postLogoutRedirectUri,
    },
    cache: {
      cacheLocation: BrowserCacheLocation.SessionStorage,
    },
    system: {
      loggerOptions: {
        // Personal data is never logged, and the log level stays at Error so token and claim
        // material cannot reach the console in normal operation.
        piiLoggingEnabled: false,
        logLevel: LogLevel.Error,
        loggerCallback: () => {
          /* Intentionally silent: MSAL diagnostics are not surfaced to users. */
        },
      },
    },
  });
}

/**
 * Registers MSAL and completes redirect handling before the app renders.
 *
 * When the configuration is still a placeholder the instance is created with those empty
 * values but no redirect is ever started, so the app runs and reports that authentication is
 * not configured instead of failing to boot.
 */
export function provideAuth(): (Provider | EnvironmentProviders)[] {
  return [
    {
      provide: MSAL_INSTANCE,
      useFactory: (config: AuthConfig) => createMsalInstance(config),
      deps: [AUTH_CONFIG],
    },
    MsalService,
    MsalBroadcastService,
    provideAppInitializer(async () => {
      const config = inject(AUTH_CONFIG);
      const msal = inject(MsalService);
      const auth = inject(AuthService);

      // Development mode never touches MSAL: no instance is initialised and no redirect
      // is handled, so the two token sources cannot interfere with each other.
      if (isDevelopmentAuthAllowed(config)) {
        auth.refreshSession();
        return;
      }

      if (!isEntraConfigured(config)) {
        return;
      }

      await msal.instance.initialize();

      // Completes the redirect leg of the flow. MSAL restores the originally requested path
      // itself; nothing here reads or stores the returned authorization code or token.
      await msal.instance.handleRedirectPromise();

      const accounts = msal.instance.getAllAccounts();
      if (accounts.length > 0) {
        msal.instance.setActiveAccount(accounts[0]);
      }

      auth.refreshSession();
    }),
  ];
}
