import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';
import { SystemStatus } from './system-status.model';

/**
 * Typed client for the backend system-status endpoint. Uses the relative
 * `/api/v1/system/status` path via {@link API_BASE_PATH}.
 */
@Injectable({ providedIn: 'root' })
export class SystemStatusApi {
  private readonly http = inject(HttpClient);
  private readonly basePath = inject(API_BASE_PATH);

  getStatus(): Observable<SystemStatus> {
    return this.http.get<SystemStatus>(`${this.basePath}/v1/system/status`);
  }
}
