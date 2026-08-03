import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

/**
 * Hides the admin route from users the API did not report as administrators.
 *
 * This is user experience only. The role it reads came from `/api/v1/auth/session`, and the
 * API re-checks the local Admin role on every protected call — so bypassing this guard in the
 * browser gains nothing.
 */
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAdmin() ? true : router.createUrlTree(['/account']);
};

/** Sends signed-out visitors to the account page, which offers the sign-in action. */
export const authenticatedGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.session() !== null ? true : router.createUrlTree(['/account']);
};
