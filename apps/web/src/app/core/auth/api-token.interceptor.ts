import { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import { Observable, from, switchMap } from 'rxjs';

import { AUTH_CONFIG, AuthConfig, isAuthConfigured } from '../configuration/auth-config';

/**
 * Attaches an API access token to requests aimed at the configured API, and to nothing else.
 *
 * Sending a bearer token to the wrong host hands an attacker a working credential, so the
 * match is deliberately strict: the request URL must resolve to the exact configured origin
 * *and* sit under the configured base path. A lookalike host, a third-party URL, or a path
 * that merely starts with the same characters never receives a token.
 */
export const apiTokenInterceptor: HttpInterceptorFn = (
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const config = inject(AUTH_CONFIG);
  const msal = inject(MsalService);

  if (!isAuthConfigured(config) || !targetsConfiguredApi(request.url, config)) {
    return next(request);
  }

  const account = msal.instance.getAllAccounts()[0];
  if (!account) {
    return next(request);
  }

  // A silent acquisition can legitimately fail once the session has expired. The rejection
  // propagates as an ordinary HTTP error for the session state to recover from; the token
  // itself is never logged or stored by this application.
  return from(msal.instance.acquireTokenSilent({ scopes: [config.apiScope], account })).pipe(
    switchMap((result) =>
      next(
        request.clone({
          setHeaders: { Authorization: `Bearer ${result.accessToken}` },
        }),
      ),
    ),
  );
};

/**
 * Whether the request URL is exactly the configured API.
 *
 * Both the request and the configured base URL are resolved against the document origin
 * first, so a relative request such as `/api/v1/...` is compared as a fully qualified URL
 * rather than by string prefix.
 */
export function targetsConfiguredApi(requestUrl: string, config: AuthConfig): boolean {
  const base = safeUrl(config.apiBaseUrl);
  const target = safeUrl(requestUrl);

  if (!base || !target) {
    return false;
  }

  // Origin comparison covers scheme, host, and port, so http/https mismatches and lookalike
  // hosts such as "localhost.evil.test" are rejected.
  if (target.origin !== base.origin) {
    return false;
  }

  const basePath = normalizePath(base.pathname);
  const targetPath = normalizePath(target.pathname);

  // Segment-aware prefix: "/api" must not match "/apifoo".
  return targetPath === basePath || targetPath.startsWith(`${basePath}/`);
}

function safeUrl(value: string): URL | null {
  try {
    const origin = globalThis.location?.origin ?? 'http://localhost';
    return new URL(value, origin);
  } catch {
    return null;
  }
}

function normalizePath(pathname: string): string {
  return pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
}
