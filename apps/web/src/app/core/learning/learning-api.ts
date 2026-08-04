import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';

/** One lesson as the curriculum shows it. */
export interface CurriculumLesson {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly lessonType: 'Video' | 'Article';
  readonly sortOrder: number;
  readonly isPreview: boolean;
  readonly estimatedDurationSeconds: number | null;
  readonly isAccessible: boolean;
  readonly isPlayable: boolean;
  readonly startedAtUtc: string | null;
  readonly completedAtUtc: string | null;
  readonly lastPositionSeconds: number;
}

/** One published section. */
export interface CurriculumSection {
  readonly id: string;
  readonly title: string;
  readonly sortOrder: number;
  readonly lessons: readonly CurriculumLesson[];
}

/** A course outline with the viewer's own progress folded in. */
export interface CourseCurriculum {
  readonly courseId: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly sections: readonly CurriculumSection[];
  readonly accessGranted: boolean;
  readonly accessReason: string;
  readonly accessDenial: string;
  readonly accessCode: string;
  readonly accessEndsAtUtc: string | null;
  readonly isPreviewOnly: boolean;
  readonly totalLessons: number;
  readonly completedLessons: number;
  readonly resumeLessonId: string | null;
}

/** A downloadable file attached to a lesson. */
export interface LessonResourceLink {
  readonly id: string;
  readonly displayName: string;
  readonly mediaType: string;
  readonly sizeBytes: number | null;
}

/** One lesson opened for study. */
export interface LessonDetail {
  readonly id: string;
  readonly courseId: string;
  readonly courseSlug: string;
  readonly title: string;
  readonly lessonType: 'Video' | 'Article';
  readonly bodyMarkdown: string | null;
  readonly isPlayable: boolean;
  readonly resources: readonly LessonResourceLink[];
  readonly previousLessonId: string | null;
  readonly nextLessonId: string | null;
  readonly startedAtUtc: string | null;
  readonly completedAtUtc: string | null;
  readonly lastPositionSeconds: number;
  readonly accessReason: string;
}

/** One course on the My Learning shelf. */
export interface MyLearningCourse {
  readonly courseId: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly totalLessons: number;
  readonly completedLessons: number;
  readonly percentComplete: number;
  readonly resumeLessonId: string | null;
  readonly lastAccessedAtUtc: string | null;
  readonly accessReason: string;
  readonly accessEndsAtUtc: string | null;
}

/** The recorded outcome of a progress report. */
export interface ProgressRecorded {
  readonly lessonId: string;
  readonly startedAtUtc: string | null;
  readonly completedAtUtc: string | null;
  readonly lastPositionSeconds: number;
  readonly courseCompleted: boolean;
  readonly completedLessons: number;
  readonly totalLessons: number;
}

/**
 * Typed client for the learner-facing course experience.
 *
 * Nothing here decides access — the server does, per request. The client renders locks and
 * upgrade prompts from what comes back, and a tampered client gains nothing because every
 * lesson body, download, and playback token still comes from an authorised call.
 */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(API_BASE_PATH)}/v1/learning`;

  getCurriculum(courseSlug: string): Observable<CourseCurriculum> {
    return this.http.get<CourseCurriculum>(`${this.root}/courses/${courseSlug}`);
  }

  getLesson(lessonId: string): Observable<LessonDetail> {
    return this.http.get<LessonDetail>(`${this.root}/lessons/${lessonId}`);
  }

  recordProgress(
    lessonId: string,
    positionSeconds: number,
    completed: boolean,
  ): Observable<ProgressRecorded> {
    return this.http.post<ProgressRecorded>(`${this.root}/lessons/${lessonId}/progress`, {
      positionSeconds,
      completed,
    });
  }

  getMyLearning(): Observable<readonly MyLearningCourse[]> {
    return this.http.get<readonly MyLearningCourse[]>(`${this.root}/my-learning`);
  }
}
