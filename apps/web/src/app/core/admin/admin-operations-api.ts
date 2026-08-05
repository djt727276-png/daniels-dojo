import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../catalog/catalog.model';
import { API_BASE_PATH } from '../configuration/app-config';

/** One platform account as an operator sees it. */
export interface AdminUserView {
  readonly id: string;
  readonly displayName: string;
  readonly email: string;
  readonly status: string;
  readonly emailVerified: boolean;
  readonly roles: readonly string[];
  readonly entitlementCount: number;
  readonly createdAtUtc: string;
}

/** One issued certificate, for the admin listing. */
export interface AdminCertificateView {
  readonly id: string;
  readonly holderName: string;
  readonly courseTitle: string;
  readonly verificationCode: string;
  readonly issuedAtUtc: string;
  readonly revokedAtUtc: string | null;
  readonly revocationReason: string | null;
}

/** One order, for the admin listing. */
export interface AdminOrderView {
  readonly id: string;
  readonly customerEmail: string;
  readonly offerName: string;
  readonly status: string;
  readonly totalMinor: number;
  readonly currency: string;
  readonly createdAtUtc: string;
  readonly paidAtUtc: string | null;
}

/** One received provider webhook event. */
export interface AdminWebhookEventView {
  readonly id: string;
  readonly provider: string;
  readonly eventType: string;
  readonly status: string;
  readonly receivedAtUtc: string;
}

/** One audit row, for the viewer. */
export interface AdminAuditEntryView {
  readonly id: string;
  readonly action: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly actorName: string;
  readonly reason: string | null;
  readonly metadataJson: string | null;
  readonly occurredAtUtc: string;
}

/** One operator switch. */
export interface FeatureFlagView {
  readonly key: string;
  readonly enabled: boolean;
  readonly description: string;
  readonly updatedAtUtc: string;
}

/** What is actually running. */
export interface OpsSnapshot {
  readonly environmentName: string;
  readonly informationalVersion: string | null;
  readonly lastAppliedMigration: string;
  readonly pendingMigrationCount: number;
  readonly mediaStorageMode: string;
  readonly videoProviderMode: string;
  readonly paymentProviderMode: string;
  readonly databaseReachable: boolean;
}

/** What posting an announcement produced. */
export interface AnnouncementPosted {
  readonly threadId: string;
  readonly membersNotified: number;
}

/** Typed client for the operator's back office. */
@Injectable({ providedIn: 'root' })
export class AdminOperationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);
  private readonly root = `${this.base}/v1/admin`;

  searchUsers(search: string, page = 1): Observable<PagedResult<AdminUserView>> {
    let params = new HttpParams().set('page', page);

    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<PagedResult<AdminUserView>>(`${this.root}/users`, { params });
  }

  setAdminRole(userId: string, isAdmin: boolean, reason: string): Observable<AdminUserView> {
    return this.http.post<AdminUserView>(`${this.root}/users/${userId}/admin-role`, {
      isAdmin,
      reason,
    });
  }

  setUserStatus(userId: string, targetStatus: string, reason: string): Observable<AdminUserView> {
    return this.http.post<AdminUserView>(`${this.root}/users/${userId}/status`, {
      targetStatus,
      reason,
    });
  }

  grantCourse(userId: string, courseId: string, reason: string): Observable<AdminUserView> {
    return this.http.post<AdminUserView>(`${this.root}/users/${userId}/grants`, {
      courseId,
      reason,
    });
  }

  listCertificates(search: string, page = 1): Observable<PagedResult<AdminCertificateView>> {
    let params = new HttpParams().set('page', page);

    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<PagedResult<AdminCertificateView>>(`${this.root}/certificates`, {
      params,
    });
  }

  revokeCertificate(certificateId: string, reason: string): Observable<unknown> {
    return this.http.post(`${this.root}/certificates/${certificateId}/revoke`, { reason });
  }

  listOrders(page = 1): Observable<PagedResult<AdminOrderView>> {
    return this.http.get<PagedResult<AdminOrderView>>(`${this.root}/orders`, {
      params: new HttpParams().set('page', page),
    });
  }

  listWebhookEvents(page = 1): Observable<PagedResult<AdminWebhookEventView>> {
    return this.http.get<PagedResult<AdminWebhookEventView>>(`${this.root}/webhook-events`, {
      params: new HttpParams().set('page', page),
    });
  }

  listAudit(action: string, page = 1): Observable<PagedResult<AdminAuditEntryView>> {
    let params = new HttpParams().set('page', page);

    if (action) {
      params = params.set('action', action);
    }

    return this.http.get<PagedResult<AdminAuditEntryView>>(`${this.root}/audit`, { params });
  }

  listFlags(): Observable<readonly FeatureFlagView[]> {
    return this.http.get<readonly FeatureFlagView[]>(`${this.root}/flags`);
  }

  setFlag(key: string, enabled: boolean, reason: string): Observable<FeatureFlagView> {
    return this.http.put<FeatureFlagView>(`${this.root}/flags/${encodeURIComponent(key)}`, {
      enabled,
      reason,
    });
  }

  getOps(): Observable<OpsSnapshot> {
    return this.http.get<OpsSnapshot>(`${this.root}/ops`);
  }

  postAnnouncement(courseId: string, title: string, body: string): Observable<AnnouncementPosted> {
    return this.http.post<AnnouncementPosted>(
      `${this.root}/community/courses/${courseId}/announcements`,
      { title, body },
    );
  }
}
