import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';

import { apiTokenInterceptor } from './core/auth/api-token.interceptor';
import { provideAuth } from './core/auth/msal-providers';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),

    // The interceptor attaches an access token to the configured API origin only.
    provideHttpClient(withFetch(), withInterceptors([apiTokenInterceptor])),

    ...provideAuth(),
  ],
};
