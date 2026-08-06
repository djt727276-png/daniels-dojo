import { HttpClient } from '@angular/common/http';
import {
  Component,
  ElementRef,
  Injectable,
  OnDestroy,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type Hls from 'hls.js';
import { Observable } from 'rxjs';

import { LessonPlaybackGrant } from '../../core/admin/admin-media-api';
import { toApiFailure } from '../../core/api/problem-details';
import { API_BASE_PATH } from '../../core/configuration/app-config';
import { CourseCurriculum, LearningApi, LessonDetail } from '../../core/learning/learning-api';
import { DdIcon } from '../../shared/ui/icon/dd-icon';
import { ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

type PlayerState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly lesson: LessonDetail }
  | { readonly kind: 'error'; readonly message: string };

/** Requests a viewing authorisation for one lesson. */
@Injectable({ providedIn: 'root' })
class LessonPlaybackApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);

  getPlayback(lessonId: string): Observable<LessonPlaybackGrant> {
    return this.http.get<LessonPlaybackGrant>(
      `${this.base}/v1/learning/lessons/${lessonId}/playback`,
    );
  }
}

/**
 * One lesson, opened for study: the immersive player with the course
 * curriculum alongside.
 *
 * A video lesson requests a short-lived playback authorisation when the
 * learner presses play, then streams the signed Mux HLS rendition through the
 * native video element (Safari) or hls.js (everything else) — loaded on
 * demand so the player chunk stays light. A deterministic grant (the
 * non-production provider) renders the authorisation proof panel instead of a
 * stream that cannot exist.
 *
 * Progress is reported to the server, which owns the rules: positions only
 * move forward and completion never un-happens, so nothing here defends
 * against its own stale state. Focus mode simply hides the curriculum rail.
 */
@Component({
  selector: 'app-lesson-player',
  imports: [
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    MatTooltipModule,
    DdIcon,
    LoadingState,
    ErrorState,
  ],
  templateUrl: './lesson-player.html',
  styleUrl: './lesson-player.scss',
})
export class LessonPlayer implements OnDestroy {
  private readonly learning = inject(LearningApi);
  private readonly playbackApi = inject(LessonPlaybackApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly state = signal<PlayerState>({ kind: 'loading' });
  protected readonly playback = signal<LessonPlaybackGrant | null>(null);
  protected readonly playbackFailure = signal<string | null>(null);
  protected readonly completing = signal(false);
  protected readonly curriculum = signal<CourseCurriculum | null>(null);

  /** Focus mode hides the curriculum; the drawer signal drives small screens. */
  protected readonly focusMode = signal(false);
  protected readonly drawerOpen = signal(false);

  private readonly videoRef = viewChild<ElementRef<HTMLVideoElement>>('lessonVideo');

  private hls: Hls | null = null;
  private heartbeat: ReturnType<typeof setInterval> | null = null;
  private watchedSeconds = 0;

  protected readonly lesson = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.lesson : null;
  });

  protected readonly message = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  /** True when the grant names a real provider stream rather than the deterministic stand-in. */
  protected readonly realStream = computed(() => {
    const grant = this.playback();
    return grant !== null && !grant.playbackId.startsWith('playback-');
  });

  constructor() {
    // Re-resolve on every navigation, including lesson-to-lesson moves that
    // reuse the component instance.
    this.route.paramMap.subscribe(() => this.load());
  }

  ngOnDestroy(): void {
    this.stopHeartbeat();
    this.detachStream();
  }

  protected load(): void {
    const lessonId = this.route.snapshot.paramMap.get('lessonId') ?? '';

    this.stopHeartbeat();
    this.detachStream();
    this.playback.set(null);
    this.playbackFailure.set(null);
    this.state.set({ kind: 'loading' });

    this.learning.getLesson(lessonId).subscribe({
      next: (lesson) => {
        this.watchedSeconds = lesson.lastPositionSeconds;
        this.state.set({ kind: 'ready', lesson });
        this.loadCurriculum(lesson.courseSlug);
      },
      error: (error: unknown) =>
        this.state.set({ kind: 'error', message: toApiFailure(error).message }),
    });
  }

  /** The curriculum rail is an enhancement; the lesson stands without it. */
  private loadCurriculum(courseSlug: string): void {
    if (this.curriculum()?.slug === courseSlug) {
      return;
    }

    this.learning.getCurriculum(courseSlug).subscribe({
      next: (curriculum) => this.curriculum.set(curriculum),
      error: () => this.curriculum.set(null),
    });
  }

  protected play(): void {
    const lesson = this.lesson();

    if (!lesson) {
      return;
    }

    this.playbackFailure.set(null);

    this.playbackApi.getPlayback(lesson.id).subscribe({
      next: (grant) => {
        this.playback.set(grant);
        this.startHeartbeat(lesson.id);

        if (!grant.playbackId.startsWith('playback-')) {
          // Wait a tick for the <video> element to render, then attach.
          setTimeout(() => void this.attachStream(grant), 0);
        }
      },
      error: (error: unknown) => this.playbackFailure.set(toApiFailure(error).message),
    });
  }

  /**
   * Streams the signed HLS rendition. Safari plays HLS natively; elsewhere
   * hls.js is imported on demand and attached to the same element.
   */
  private async attachStream(grant: LessonPlaybackGrant): Promise<void> {
    const video = this.videoRef()?.nativeElement;

    if (!video) {
      return;
    }

    const src = `https://stream.mux.com/${grant.playbackId}.m3u8${
      grant.token ? `?token=${encodeURIComponent(grant.token)}` : ''
    }`;

    if (video.canPlayType('application/vnd.apple.mpegurl')) {
      video.src = src;
    } else {
      const { default: HlsCtor } = await import('hls.js');

      if (!HlsCtor.isSupported()) {
        this.playbackFailure.set('This browser cannot play the lesson video.');
        return;
      }

      this.hls = new HlsCtor();
      this.hls.loadSource(src);
      this.hls.attachMedia(video);
    }

    video.play().catch(() => undefined);
  }

  private detachStream(): void {
    this.hls?.destroy();
    this.hls = null;
  }

  protected complete(): void {
    const lesson = this.lesson();

    if (!lesson) {
      return;
    }

    this.completing.set(true);

    this.learning.recordProgress(lesson.id, this.currentPosition(), true).subscribe({
      next: (recorded) => {
        this.completing.set(false);
        this.state.set({
          kind: 'ready',
          lesson: { ...lesson, completedAtUtc: recorded.completedAtUtc },
        });

        // Finishing the last lesson sends the learner back to their shelf;
        // otherwise flow straight into the next one.
        if (recorded.courseCompleted) {
          void this.router.navigate(['/my-learning']);
        } else if (lesson.nextLessonId) {
          void this.router.navigate(['/learn/lessons', lesson.nextLessonId]);
        }
      },
      error: () => this.completing.set(false),
    });
  }

  protected toggleFocus(): void {
    this.focusMode.update((focused) => !focused);
  }

  protected toggleDrawer(): void {
    this.drawerOpen.update((open) => !open);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  /** Formats an estimated duration as "12 min". */
  protected minutes(seconds: number | null): string {
    return seconds === null ? '' : `${Math.max(1, Math.round(seconds / 60))} min`;
  }

  /**
   * The truest position available: the playing video's clock when a real
   * stream is attached, the coarse heartbeat counter otherwise.
   */
  private currentPosition(): number {
    const video = this.videoRef()?.nativeElement;

    return video && video.currentTime > 0 ? Math.floor(video.currentTime) : this.watchedSeconds;
  }

  /**
   * Reports the watch position periodically while playback is open. The
   * interval is deliberately coarse — the resume position is a convenience,
   * and the server ignores anything that would move it backwards.
   */
  private startHeartbeat(lessonId: string): void {
    this.stopHeartbeat();

    this.heartbeat = setInterval(() => {
      this.watchedSeconds += 15;
      this.learning.recordProgress(lessonId, this.currentPosition(), false).subscribe({
        error: () => undefined,
      });
    }, 15_000);
  }

  private stopHeartbeat(): void {
    if (this.heartbeat !== null) {
      clearInterval(this.heartbeat);
      this.heartbeat = null;
    }
  }
}
