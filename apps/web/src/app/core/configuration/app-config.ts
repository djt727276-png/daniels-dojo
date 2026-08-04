import { InjectionToken } from '@angular/core';

import { environment } from '../../../environments/environment';

/**
 * Base path for API requests.
 *
 * Locally this stays the relative `/api`, forwarded by the dev-server proxy
 * (proxy.conf.json), so no localhost URL is embedded in application code. A hosted build
 * substitutes an absolute origin through the environment file, because Static Web Apps and
 * the Container Apps API live on different hosts. The value is a public URL, never a secret.
 */
export const API_BASE_PATH = new InjectionToken<string>('API_BASE_PATH', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});
