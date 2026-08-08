import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { StatusTone } from '../../shared/ui/status-chip/status-chip';
import { API_BASE_PATH } from '../configuration/app-config';

/** Where a lesson's video is in the pipeline. */
export type LessonVideoStatus =
  | 'None'
  | 'Requested'
  | 'Uploading'
  | 'AzureStored'
  | 'MuxIngesting'
  | 'Processing'
  | 'Ready'
  | 'Failed'
  | 'Replacing'
  | 'Archived';

/** Which adapter produced a piece of state. */
export type ProviderMode = 'Disabled' | 'Deterministic' | 'Real';

/** One stored master and what has been proven about it. */
export interface MediaSourceEvidence {
  readonly id: string;
  readonly containerName: string;
  readonly blobName: string;
  readonly contentLength: number;
  readonly contentType: string;
  readonly checksumSha256: string | null;
  readonly state: 'Pending' | 'Current' | 'Superseded' | 'Archived';
  readonly propertiesVerifiedAtUtc: string | null;
  readonly restoreVerifiedAtUtc: string | null;
  readonly restoreVerifiedLength: number | null;
}

/**
 * The evidence trail, and the single answer it exists to produce.
 *
 * `safeToDeleteLocalOriginal` is the only field an author should act on before removing a
 * file from their own machine, and nothing in the application ever acts on it — deleting the
 * original is always a human decision taken outside this system.
 */
export interface MediaVerificationEvidence {
  readonly cloudPropertiesVerified: boolean;
  readonly restoreVerified: boolean;
  readonly providerReady: boolean;
  readonly adminPlaybackVerifiedAtUtc: string | null;
  readonly studentPlaybackVerifiedAtUtc: string | null;
  readonly humanSpotCheckAtUtc: string | null;
  readonly safeToDeleteLocalOriginal: boolean;
}

/** A caption track on a lesson video. */
export interface CaptionTrackView {
  readonly id: string;
  readonly languageCode: string;
  readonly displayName: string;
  readonly isDefault: boolean;
  readonly status: LessonVideoStatus;
}

/** The Admin view of one lesson's video. */
export interface LessonVideoView {
  readonly lessonId: string;
  readonly videoId: string | null;
  readonly status: LessonVideoStatus;
  readonly providerMode: ProviderMode;
  readonly isPlayable: boolean;
  readonly durationSeconds: number | null;
  readonly aspectRatio: string | null;
  readonly failureCode: string | null;
  readonly currentSource: MediaSourceEvidence | null;
  readonly incomingSource: MediaSourceEvidence | null;
  readonly captions: readonly CaptionTrackView[];
  readonly verification: MediaVerificationEvidence;
  readonly rowVersion: string | null;
}

/**
 * An authorisation to write exactly one object.
 *
 * The URI is short-lived, write-only, and scoped to a single object chosen by the server.
 * It is used once and never stored or logged.
 */
export interface MediaUploadTicket {
  readonly sessionId: string;
  readonly uploadUri: string;
  readonly httpMethod: string;
  readonly requiredHeaders: Readonly<Record<string, string>>;
  readonly expiresAtUtc: string;
  readonly providerMode: ProviderMode;
}

/** A viewer's authorisation to play one lesson. */
export interface LessonPlaybackGrant {
  readonly lessonId: string;
  readonly playbackId: string;
  readonly token: string | null;
  readonly expiresAtUtc: string;
  readonly durationSeconds: number | null;
  readonly aspectRatio: string | null;
  readonly captions: readonly CaptionTrackView[];
  readonly accessReason: string;
}

/** What one reconciliation pass found. */
export interface MediaReconciliationReport {
  readonly examined: number;
  readonly repaired: number;
  readonly stillPending: number;
  readonly unreachable: number;
}

/** Video types the API accepts as a master. */
export const ACCEPTED_VIDEO_TYPES = [
  'video/mp4',
  'video/quicktime',
  'video/x-matroska',
  'video/webm',
] as const;

/** Caption types the API accepts. */
export const ACCEPTED_CAPTION_TYPES = ['text/vtt', 'application/x-subrip'] as const;

/**
 * Semantic tone for each pipeline state, so the lesson editor and the media workspace can
 * never disagree about whether a status reads as healthy, busy, or broken.
 */
export function videoStatusTone(status: LessonVideoStatus): StatusTone {
  switch (status) {
    case 'Ready':
      return 'success';
    case 'Failed':
      return 'danger';
    case 'Replacing':
    case 'Processing':
    case 'MuxIngesting':
      return 'info';
    case 'None':
    case 'Archived':
      return 'neutral';
    default:
      return 'warning';
  }
}

/** Plain-language wording for each pipeline state. */
export function describeVideoStatus(status: LessonVideoStatus): string {
  switch (status) {
    case 'None':
      return 'No video uploaded yet.';
    case 'Requested':
    case 'Uploading':
      return 'Waiting for the upload to finish.';
    case 'AzureStored':
      return 'Stored safely. Processing is switched off in this environment.';
    case 'MuxIngesting':
    case 'Processing':
      return 'Stored safely and being processed. This usually takes a few minutes.';
    case 'Ready':
      return 'Ready to play.';
    case 'Replacing':
      return 'A replacement is processing. Students keep watching the current video.';
    case 'Failed':
      return 'Processing failed. The uploaded master is still stored and can be retried.';
    case 'Archived':
      return 'Archived.';
    default:
      return status;
  }
}

/**
 * Typed client for the Admin media endpoints and the viewer playback route.
 *
 * The master itself never passes through here beyond {@link uploadTo}, which writes straight
 * to the storage service using the authorisation the API issued. That is what keeps a
 * multi-gigabyte file from travelling through the API and from being copied twice.
 */
@Injectable({ providedIn: 'root' })
export class AdminMediaApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);

  getLessonVideo(lessonId: string): Observable<LessonVideoView> {
    return this.http.get<LessonVideoView>(this.lessonRoot(lessonId));
  }

  requestVideoUpload(
    lessonId: string,
    file: { readonly name: string; readonly type: string; readonly size: number },
  ): Observable<MediaUploadTicket> {
    return this.http.post<MediaUploadTicket>(`${this.lessonRoot(lessonId)}/upload`, {
      fileName: file.name,
      contentType: file.type,
      sizeBytes: file.size,
    });
  }

  requestCaptionUpload(
    lessonId: string,
    languageCode: string,
    file: { readonly name: string; readonly type: string; readonly size: number },
  ): Observable<MediaUploadTicket> {
    return this.http.post<MediaUploadTicket>(`${this.lessonRoot(lessonId)}/captions/upload`, {
      languageCode,
      fileName: file.name,
      contentType: file.type,
      sizeBytes: file.size,
    });
  }

  /**
   * Sends the file to the authorised location.
   *
   * The ticket's own method, URI, and headers are used exactly as issued: a cloud signature
   * covers all three, so altering any of them invalidates it. The browser streams the body,
   * which is what keeps a multi-gigabyte master from being copied a second time on the way
   * out.
   */
  uploadTo(ticket: MediaUploadTicket, file: Blob): Observable<unknown> {
    let headers = new HttpHeaders();

    for (const [name, value] of Object.entries(ticket.requiredHeaders)) {
      headers = headers.set(name, value);
    }

    return this.http.request(ticket.httpMethod, ticket.uploadUri, { body: file, headers });
  }

  completeUpload(sessionId: string): Observable<LessonVideoView> {
    return this.http.post<LessonVideoView>(
      `${this.base}/v1/admin/media/upload-sessions/${sessionId}/complete`,
      null,
    );
  }

  verifyRestore(lessonId: string): Observable<LessonVideoView> {
    return this.http.post<LessonVideoView>(`${this.lessonRoot(lessonId)}/verify`, null);
  }

  preview(lessonId: string): Observable<LessonPlaybackGrant> {
    return this.http.post<LessonPlaybackGrant>(`${this.lessonRoot(lessonId)}/preview`, null);
  }

  recordSpotCheck(lessonId: string): Observable<LessonVideoView> {
    return this.http.post<LessonVideoView>(`${this.lessonRoot(lessonId)}/spot-check`, null);
  }

  reconcile(lessonId: string): Observable<MediaReconciliationReport> {
    return this.http.post<MediaReconciliationReport>(
      `${this.lessonRoot(lessonId)}/reconcile`,
      null,
    );
  }

  private lessonRoot(lessonId: string): string {
    return `${this.base}/v1/admin/lessons/${lessonId}/video`;
  }
}
