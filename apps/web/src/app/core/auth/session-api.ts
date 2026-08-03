import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';
import { Session } from './session.model';

/** Typed client for the authenticated session endpoint. */
@Injectable({ providedIn: 'root' })
export class SessionApi {
  private readonly http = inject(HttpClient);
  private readonly basePath = inject(API_BASE_PATH);

  /**
   * Reads the signed-in session. The API resolves the caller from the validated token and
   * returns the local user record, so this response is the authoritative source of identity
   * and roles for the UI.
   */
  getSession(): Observable<Session> {
    return this.http.get<Session>(`${this.basePath}/v1/auth/session`);
  }
}
