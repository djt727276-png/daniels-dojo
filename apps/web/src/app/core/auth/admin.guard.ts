import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, filter, map, take } from 'rxjs';

import { AuthService } from './auth.service';
import { SessionState } from './session.model';

/**
 * Waits until the session question is answered, then decides.
 *
 * The session loads asynchronously at application start, so a guard that reads it
 * synchronously would bounce a signed-in member to the account page on every hard
 * navigation — a refresh on /dashboard, a bookmarked /admin. Waiting for the first
 * settled state fixes deep links without weakening anything: the API re-checks every
 * call regardless of what the router shows.
 */
function whenSettled(): Observable<SessionState> {
  const auth = inject(AuthService);

  return toObservable(auth.sessionState).pipe(
    filter((state) => state.kind !== 'loading'),
    take(1),
  );
}

/**
 * Hides the admin routes from users the API did not report as administrators.
 *
 * This is user experience only. The role it reads came from `/api/v1/auth/session`, and the
 * API re-checks the local Admin role on every protected call — so bypassing this guard in the
 * browser gains nothing.
 */
export const adminGuard: CanActivateFn = (): Observable<boolean | UrlTree> => {
  const router = inject(Router);

  return whenSettled().pipe(
    map((state) =>
      state.kind === 'signedIn' && state.session.roles.includes('Admin')
        ? true
        : router.createUrlTree(['/account']),
    ),
  );
};

/** Sends signed-out visitors to the account page, which offers the sign-in action. */
export const authenticatedGuard: CanActivateFn = (): Observable<boolean | UrlTree> => {
  const router = inject(Router);

  return whenSettled().pipe(
    map((state) => (state.kind === 'signedIn' ? true : router.createUrlTree(['/account']))),
  );
};
