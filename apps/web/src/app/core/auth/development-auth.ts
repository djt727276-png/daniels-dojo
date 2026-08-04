import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';

/** The only profile keys the API will issue a Development token for. */
export type DevelopmentProfileKey = 'admin' | 'student';

interface DevelopmentTokenResponse {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
}

/**
 * Client for the Development-only sign-in endpoint.
 *
 * The token is held in `sessionStorage` under its own key and is never written into
 * the MSAL cache, so the two token sources cannot be confused for one another. It is
 * cleared on sign-out and whenever the session is rejected.
 *
 * The endpoint does not exist outside Development — the API returns 404 rather than
 * 403 — so a production build calling it would simply fail.
 */
@Injectable({ providedIn: 'root' })
export class DevelopmentAuthClient {
  /** Deliberately distinct from any MSAL storage key. */
  static readonly StorageKey = 'dd.dev-auth.token';

  private readonly http = inject(HttpClient);
  private readonly basePath = inject(API_BASE_PATH);

  /**
   * Exchanges a seeded profile key for a short-lived bearer token.
   *
   * Only the fixed keys `admin` and `student` are accepted; there is no way to ask for
   * an arbitrary user, email, role, or claim.
   */
  signIn(profile: DevelopmentProfileKey): Observable<string> {
    return this.http
      .post<DevelopmentTokenResponse>(`${this.basePath}/v1/development/auth/token`, { profile })
      .pipe(
        map((response) => {
          this.store(response.accessToken);
          return response.accessToken;
        }),
      );
  }

  /** The stored token, or null. */
  read(): string | null {
    try {
      return globalThis.sessionStorage?.getItem(DevelopmentAuthClient.StorageKey) ?? null;
    } catch {
      // sessionStorage can be unavailable (private mode, sandboxed frame). Treat that as
      // "not signed in" rather than failing the application.
      return null;
    }
  }

  /** Removes the token. Safe to call when nothing is stored. */
  clear(): void {
    try {
      globalThis.sessionStorage?.removeItem(DevelopmentAuthClient.StorageKey);
    } catch {
      /* Nothing to clear if storage is unavailable. */
    }
  }

  private store(token: string): void {
    try {
      globalThis.sessionStorage?.setItem(DevelopmentAuthClient.StorageKey, token);
    } catch {
      /* A token that cannot be cached simply means the next request re-authenticates. */
    }
  }
}
