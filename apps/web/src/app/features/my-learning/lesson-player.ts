import { HttpClient } from '@angular/common/http';
import { Component, Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { LessonPlaybackGrant } from '../../core/admin/admin-media-api';
import { toApiFailure } from '../../core/api/problem-details';
import { API_BASE_PATH } from '../../core/configuration/app-config';
import { LearningApi, LessonDetail } from '../../core/learning/learning-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
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
 * One lesson, opened for study.
 *
 * An article renders its body; a video lesson requests a short-lived playback authorisation
 * when the learner presses play. Progress is reported to the server, which owns the rules:
 * positions only move forward and completion never un-happens, so nothing here needs to
 * defend against its own stale state.
 */
@Component({
  selector: 'app-lesson-player',
  imports: [RouterLink, MatCardModule, MatButtonModule, PageHeader, LoadingState, ErrorState],
  template: `
    @switch (state().kind) {
      @case ('loading') {
        <app-loading-state message="Opening the lesson…" />
      }
      @case ('error') {
        <app-error-state [message]="message()" (retry)="load()" />
      }
      @default {
        @if (lesson(); as detail) {
          <div class="dd-page dd-stack">
            <app-page-header [title]="detail.title" [description]="'Lesson'">
              <a matButton [routerLink]="['/courses', detail.courseSlug]">Course outline</a>
            </app-page-header>

            @if (detail.lessonType === 'Video') {
              <mat-card appearance="outlined">
                <mat-card-content>
                  @if (playback(); as grant) {
                    <div
                      class="player__frame"
                      [style.aspect-ratio]="grant.aspectRatio ?? '16 / 9'"
                      data-testid="player-frame"
                    >
                      <p class="player__placeholder">
                        Playing <code>{{ grant.playbackId }}</code>
                        — authorisation expires {{ grant.expiresAtUtc }}.
                      </p>
                    </div>
                  } @else if (detail.isPlayable) {
                    <div class="player__frame" [style.aspect-ratio]="'16 / 9'">
                      <button matButton="filled" type="button" (click)="play()">
                        ▶ Play lesson
                      </button>
                    </div>
                  } @else {
                    <p class="player__unready">
                      This lesson's video is still being prepared. Check back shortly.
                    </p>
                  }

                  @if (playbackFailure(); as failure) {
                    <p class="player__unready" role="alert">{{ failure }}</p>
                  }
                </mat-card-content>
              </mat-card>
            }

            @if (detail.bodyMarkdown; as body) {
              <mat-card appearance="outlined">
                <mat-card-content>
                  <pre class="player__body">{{ body }}</pre>
                </mat-card-content>
              </mat-card>
            }

            @if (detail.resources.length > 0) {
              <mat-card appearance="outlined">
                <mat-card-header>
                  <mat-card-title>Lesson materials</mat-card-title>
                </mat-card-header>
                <mat-card-content>
                  <ul class="player__resources">
                    @for (resource of detail.resources; track resource.id) {
                      <li>{{ resource.displayName }} ({{ resource.mediaType }})</li>
                    }
                  </ul>
                </mat-card-content>
              </mat-card>
            }

            <div class="player__nav">
              @if (detail.previousLessonId; as previous) {
                <a matButton [routerLink]="['/learn/lessons', previous]">← Previous lesson</a>
              } @else {
                <span></span>
              }

              @if (detail.completedAtUtc) {
                <span class="player__done" role="status">Completed ✓</span>
              } @else {
                <button
                  matButton="filled"
                  type="button"
                  [disabled]="completing()"
                  (click)="complete()"
                >
                  Mark as complete
                </button>
              }

              @if (detail.nextLessonId; as next) {
                <a matButton [routerLink]="['/learn/lessons', next]">Next lesson →</a>
              }
            </div>
          </div>
        }
      }
    }
  `,
  styles: `
    .player__frame {
      display: grid;
      place-items: center;
      width: 100%;
      background: var(--dd-surface-variant);
      border-radius: var(--dd-radius-md);
    }

    .player__placeholder,
    .player__unready {
      padding: var(--dd-space-4);
      color: var(--dd-on-surface-variant);
      text-align: center;
    }

    .player__body {
      font-family: var(--dd-font-sans);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .player__resources {
      margin: 0;
      padding-left: var(--dd-space-5);
    }

    .player__nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-3);
      align-items: center;
      justify-content: space-between;
    }

    .player__done {
      font-weight: var(--dd-weight-medium);
      color: var(--dd-success);
    }
  `,
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

  constructor() {
    // Re-resolve on every navigation, including lesson-to-lesson moves that reuse the
    // component instance.
    this.route.paramMap.subscribe(() => this.load());
  }

  ngOnDestroy(): void {
    this.stopHeartbeat();
  }

  protected load(): void {
    const lessonId = this.route.snapshot.paramMap.get('lessonId') ?? '';

    this.stopHeartbeat();
    this.playback.set(null);
    this.playbackFailure.set(null);
    this.state.set({ kind: 'loading' });

    this.learning.getLesson(lessonId).subscribe({
      next: (lesson) => {
        this.watchedSeconds = lesson.lastPositionSeconds;
        this.state.set({ kind: 'ready', lesson });
      },
      error: (error: unknown) =>
        this.state.set({ kind: 'error', message: toApiFailure(error).message }),
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
      },
      error: (error: unknown) => this.playbackFailure.set(toApiFailure(error).message),
    });
  }

  protected complete(): void {
    const lesson = this.lesson();

    if (!lesson) {
      return;
    }

    this.completing.set(true);

    this.learning.recordProgress(lesson.id, this.watchedSeconds, true).subscribe({
      next: (recorded) => {
        this.completing.set(false);
        this.state.set({
          kind: 'ready',
          lesson: { ...lesson, completedAtUtc: recorded.completedAtUtc },
        });

        // Finishing the last lesson sends the learner back to their shelf; otherwise flow
        // straight into the next one.
        if (recorded.courseCompleted) {
          void this.router.navigate(['/my-learning']);
        } else if (lesson.nextLessonId) {
          void this.router.navigate(['/learn/lessons', lesson.nextLessonId]);
        }
      },
      error: () => this.completing.set(false),
    });
  }

  /**
   * Reports the watch position periodically while playback is open.
   *
   * The interval is deliberately coarse — the resume position is a convenience, and the
   * server ignores anything that would move it backwards.
   */
  private startHeartbeat(lessonId: string): void {
    this.stopHeartbeat();

    this.heartbeat = setInterval(() => {
      this.watchedSeconds += 15;
      this.learning.recordProgress(lessonId, this.watchedSeconds, false).subscribe({
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
