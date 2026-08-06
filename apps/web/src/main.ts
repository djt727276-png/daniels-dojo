import { bootstrapApplication } from '@angular/platform-browser';

import { App } from './app/app';
import { appConfig } from './app/app.config';
import { environment } from './environments/environment';

/**
 * One canonical origin.
 *
 * Static Web Apps serves every bound hostname identically and offers no host-based
 * redirect rule, so the redirect happens here — before the application boots, preserving
 * the path and query, and only ever from a `www.` host to the same domain without it.
 * That keeps sessions, the MSAL redirect URI, CORS, and SEO on a single origin instead of
 * splitting them across two.
 */
function redirectToCanonicalHost(): boolean {
  const { hostname, protocol } = window.location;

  if (!environment.production || !hostname.startsWith('www.')) {
    return false;
  }

  const canonical = hostname.slice('www.'.length);
  window.location.replace(
    `${protocol}//${canonical}${window.location.pathname}${window.location.search}${window.location.hash}`,
  );

  return true;
}

if (!redirectToCanonicalHost()) {
  bootstrapApplication(App, appConfig).catch((error: unknown) => console.error(error));
}
