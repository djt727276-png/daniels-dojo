import { InjectionToken } from '@angular/core';

/**
 * Base path for API requests. Kept relative so the Angular dev-server proxy
 * (proxy.conf.json) forwards `/api/*` to the local ASP.NET Core API and no
 * absolute localhost URL is embedded in application code. Production API routing
 * is deliberately out of scope for Phase 1.
 */
export const API_BASE_PATH = new InjectionToken<string>('API_BASE_PATH', {
  providedIn: 'root',
  factory: () => '/api',
});
