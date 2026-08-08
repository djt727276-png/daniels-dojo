import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute } from '@angular/router';
import { switchMap } from 'rxjs';

import {
  ACCEPTED_VIDEO_TYPES,
  AdminMediaApi,
  LessonPlaybackGrant,
  LessonVideoView,
  describeVideoStatus,
  videoStatusTone,
} from '../../../core/admin/admin-media-api';
import { toApiFailure } from '../../../core/api/problem-details';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { ErrorState, LoadingState } from '../../../shared/ui/state-views/state-views';
import { StatusChip } from '../../../shared/ui/status-chip/status-chip';

type ScreenState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly video: LessonVideoView }
  | { readonly kind: 'error'; readonly message: string };

/** One line of the verification trail. */
interface VerificationStep {
  readonly label: string;
  readonly done: boolean;
  readonly detail: string;
}

/** Human-readable byte count. */
function formatBytes(bytes: number): string {
  const units = ['bytes', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}

/**
 * Uploading, verifying, and signing off one lesson's master video.
 *
 * The screen exists to answer one question honestly: is the copy in the cloud good enough
 * that the author can delete the copy on their own machine? Every check is listed
 * individually, because an author about to free up disk space needs to see which step has
 * not passed rather than a single reassuring tick.
 *
 * The file itself never travels through the API. The browser asks for an authorisation, then
 * writes straight to the storage service, so a multi-gigabyte master makes exactly one
 * journey and is never copied to a second local location.
 */
@Component({
  selector: 'app-admin-lesson-media',
  imports: [
    MatCardModule,
    MatButtonModule,
    MatProgressBarModule,
    PageHeader,
    StatusChip,
    LoadingState,
    ErrorState,
  ],
  templateUrl: './admin-lesson-media.html',
  styleUrl: './admin-lesson-media.scss',
})
export class AdminLessonMedia {
  private readonly api = inject(AdminMediaApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly tone = videoStatusTone;
  protected readonly bytes = formatBytes;
  protected readonly describe = describeVideoStatus;
  protected readonly acceptedTypes = ACCEPTED_VIDEO_TYPES.join(',');

  protected readonly lessonId = signal(this.route.snapshot.paramMap.get('lessonId') ?? '');
  protected readonly state = signal<ScreenState>({ kind: 'loading' });
  protected readonly busy = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);
  protected readonly preview = signal<LessonPlaybackGrant | null>(null);

  protected readonly video = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.video : null;
  });

  protected readonly stateMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  /**
   * The checks, in the order they can pass.
   *
   * Each one is here because skipping it has a specific failure mode, and the wording says
   * what was actually proven rather than restating the step name.
   */
  protected readonly steps = computed<readonly VerificationStep[]>(() => {
    const video = this.video();

    if (!video) {
      return [];
    }

    const evidence = video.verification;

    return [
      {
        label: 'Stored in the cloud',
        done: evidence.cloudPropertiesVerified,
        detail: video.currentSource
          ? `${this.bytes(video.currentSource.contentLength)} confirmed by the storage service.`
          : 'The storage service has not confirmed an object yet.',
      },
      {
        label: 'Reads back byte for byte',
        done: evidence.restoreVerified,
        detail: video.currentSource?.checksumSha256
          ? `SHA-256 ${video.currentSource.checksumSha256.slice(0, 16)}… computed from the bytes that came back.`
          : 'Run the full verification to read the whole file back and hash it.',
      },
      {
        label: 'Processed and playable',
        done: evidence.providerReady,
        detail: this.describe(video.status),
      },
      {
        label: 'You played it back',
        done: evidence.adminPlaybackVerifiedAtUtc !== null,
        detail: 'Proves the video reaches an administrator.',
      },
      {
        label: 'A student can play it',
        done: evidence.studentPlaybackVerifiedAtUtc !== null,
        detail: 'Proves the paid path issues a working token, not just the preview one.',
      },
      {
        label: 'You confirmed it is the right footage',
        done: evidence.humanSpotCheckAtUtc !== null,
        detail: 'Nothing automated can tell whether the video is the one you meant to upload.',
      },
    ];
  });

  protected readonly safeToDelete = computed(
    () => this.video()?.verification.safeToDeleteLocalOriginal === true,
  );

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getLessonVideo(this.lessonId()).subscribe({
      next: (video) => this.state.set({ kind: 'ready', video }),
      error: (error: unknown) =>
        this.state.set({ kind: 'error', message: toApiFailure(error).message }),
    });
  }

  /**
   * Authorises one upload, writes the file, then asks the server to verify what landed.
   *
   * The completion step is not a formality — the server goes and reads the object rather
   * than believing this browser, so a half-finished upload is caught here rather than
   * discovered after the local original has been deleted.
   */
  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0);

    input.value = '';

    if (!file) {
      return;
    }

    this.reset();
    this.busy.set('Uploading the master file…');

    this.api
      .requestVideoUpload(this.lessonId(), file)
      .pipe(
        switchMap((ticket) =>
          this.api.uploadTo(ticket, file).pipe(
            switchMap(() => {
              this.busy.set('Checking what actually arrived…');
              return this.api.completeUpload(ticket.sessionId);
            }),
          ),
        ),
      )
      .subscribe({
        next: (video) => {
          this.busy.set(null);
          this.notice.set('Upload stored and verified. Processing continues in the background.');
          this.state.set({ kind: 'ready', video });
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  protected verify(): void {
    this.run(
      'Reading the whole file back from storage…',
      this.api.verifyRestore(this.lessonId()),
      () => this.notice.set('The stored file read back completely and matched its checksum.'),
    );
  }

  protected spotCheck(): void {
    this.run('Recording your sign-off…', this.api.recordSpotCheck(this.lessonId()), () =>
      this.notice.set('Signed off. Every check has now passed.'),
    );
  }

  protected refreshFromProvider(): void {
    this.reset();
    this.busy.set('Asking the video provider for the current state…');

    this.api.reconcile(this.lessonId()).subscribe({
      next: (report) => {
        this.busy.set(null);
        this.notice.set(
          report.repaired > 0
            ? 'The provider had newer information and the lesson has been updated.'
            : 'The provider agrees with what is recorded here.',
        );
        this.load();
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected play(): void {
    this.reset();
    this.busy.set('Requesting a preview…');

    this.api.preview(this.lessonId()).subscribe({
      next: (grant) => {
        this.busy.set(null);
        this.preview.set(grant);
        this.load();
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  private run(
    message: string,
    request: ReturnType<AdminMediaApi['verifyRestore']>,
    onSuccess: () => void,
  ): void {
    this.reset();
    this.busy.set(message);

    request.subscribe({
      next: (video) => {
        this.busy.set(null);
        onSuccess();
        this.state.set({ kind: 'ready', video });
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.busy.set(null);
    this.failure.set(toApiFailure(error).message);
  }

  private reset(): void {
    this.notice.set(null);
    this.failure.set(null);
  }
}
