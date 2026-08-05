import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';

/** Why the community is closed to a member, when it is. */
export type CommunityDenial = 'SetupRequired' | 'Suspended' | 'Deactivated' | 'AccountDisabled';

/** Who may send a friend request. */
export type FriendRequestPolicy = 'NoOne' | 'Everyone';

/** Who may send a direct message. There is deliberately no "Everyone". */
export type MessagePolicy = 'NoOne' | 'FriendsOnly';

/** Community readiness for the signed-in member. */
export interface CommunityStatus {
  readonly granted: boolean;
  readonly denial: CommunityDenial | null;
  readonly message: string | null;
  readonly profileExists: boolean;
  readonly handle: string | null;
  readonly guidelinesVersion: string;
}

/** The member's own community profile, including settings only they see. */
export interface MyCommunityProfile {
  readonly handle: string;
  readonly bio: string | null;
  readonly hasAvatar: boolean;
  readonly isDiscoverable: boolean;
  readonly friendRequestPolicy: FriendRequestPolicy;
  readonly messagePolicy: MessagePolicy;
  readonly status: string;
  readonly guidelinesVersion: string | null;
  readonly guidelinesAcceptedAtUtc: string | null;
  readonly eligibilityAttested: boolean;
  readonly participationReady: boolean;
  readonly rowVersion: string;
}

/** A course the member is enrolled in. */
export interface MyCourse {
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly level: string;
  readonly enrolledAtUtc: string;
  readonly lastAccessedAtUtc: string | null;
}

/** The signed-in member's landing view. */
export interface Dashboard {
  readonly displayName: string;
  readonly roles: readonly string[];
  readonly enrolledCourseCount: number;
  readonly publishedCourseCount: number;
  readonly unreadNotificationCount: number;
  readonly pendingFriendRequestCount: number;
  readonly unreadConversationCount: number;
  readonly community: CommunityStatus;
  readonly purchasingAvailable: boolean;
}

/** Completes community setup. No date of birth is collected or sent. */
export interface CompleteCommunitySetupRequest {
  readonly handle: string;
  readonly bio: string | null;
  readonly acceptGuidelines: boolean;
  readonly attestEligibility: boolean;
}

/** Updates bio and privacy settings. */
export interface UpdateCommunityProfileRequest {
  readonly bio: string | null;
  readonly isDiscoverable: boolean;
  readonly friendRequestPolicy: FriendRequestPolicy;
  readonly messagePolicy: MessagePolicy;
  readonly rowVersion: string;
}

/** Typed client for the signed-in member's own endpoints. */
@Injectable({ providedIn: 'root' })
export class MemberApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(API_BASE_PATH)}/v1/me`;

  getDashboard(): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.root}/dashboard`);
  }

  getMyCourses(): Observable<readonly MyCourse[]> {
    return this.http.get<readonly MyCourse[]>(`${this.root}/courses`);
  }

  getCommunityStatus(): Observable<CommunityStatus> {
    return this.http.get<CommunityStatus>(`${this.root}/community/status`);
  }

  getCommunityProfile(): Observable<MyCommunityProfile> {
    return this.http.get<MyCommunityProfile>(`${this.root}/community/profile`);
  }

  completeCommunitySetup(request: CompleteCommunitySetupRequest): Observable<MyCommunityProfile> {
    return this.http.post<MyCommunityProfile>(`${this.root}/community/profile`, request);
  }

  updateCommunityProfile(request: UpdateCommunityProfileRequest): Observable<MyCommunityProfile> {
    return this.http.put<MyCommunityProfile>(`${this.root}/community/profile`, request);
  }

  uploadAvatar(file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file);

    return this.http.put<void>(`${this.root}/community/profile/avatar`, form);
  }

  removeAvatar(): Observable<void> {
    return this.http.delete<void>(`${this.root}/community/profile/avatar`);
  }

  /** Everything the platform holds about the member, as a downloadable JSON document. */
  exportMyData(): Observable<Blob> {
    return this.http.get(`${this.root}/privacy/export`, { responseType: 'blob' });
  }

  /** Irreversible. The server requires the typed confirmation phrase. */
  deleteMyAccount(confirmation: string): Observable<void> {
    return this.http.post<void>(`${this.root}/privacy/delete-account`, { confirmation });
  }
}
