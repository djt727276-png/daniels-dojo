/** Publication status shared by courses, sections, and lessons. */
export type PublicationStatus = 'Draft' | 'Published' | 'Archived';

/** Difficulty banding shown on a course. */
export type CourseLevel = 'Beginner' | 'Intermediate' | 'Advanced' | 'AllLevels';

/** Kind of lesson content. */
export type LessonType = 'Video' | 'Article';

/** Selectable levels, paired with the label the operator reads. */
export const COURSE_LEVELS: readonly { readonly value: CourseLevel; readonly label: string }[] = [
  { value: 'Beginner', label: 'Beginner' },
  { value: 'Intermediate', label: 'Intermediate' },
  { value: 'Advanced', label: 'Advanced' },
  { value: 'AllLevels', label: 'All levels' },
];

/** Selectable lesson types. */
export const LESSON_TYPES: readonly { readonly value: LessonType; readonly label: string }[] = [
  { value: 'Article', label: 'Article' },
  { value: 'Video', label: 'Video' },
];

/**
 * Status changes the API will accept from a given status.
 *
 * This mirrors the server's graph so the UI can hide impossible commands. The server decides;
 * this only prevents offering an action that is certain to be refused.
 */
export function allowedTransitions(current: PublicationStatus): readonly PublicationStatus[] {
  switch (current) {
    case 'Draft':
      return ['Published', 'Archived'];
    case 'Published':
      return ['Draft', 'Archived'];
    case 'Archived':
      return ['Draft'];
  }
}

/** Course row in the Admin list. */
export interface AdminCourseListItem {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly status: PublicationStatus;
  readonly level: CourseLevel;
  readonly includedInMembership: boolean;
  readonly publishedAtUtc: string | null;
  readonly updatedAtUtc: string;
  readonly sectionCount: number;
  readonly lessonCount: number;
  readonly rowVersion: string;
}

/** A lesson as the editor sees it. */
export interface AdminLesson {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly lessonType: LessonType;
  readonly bodyMarkdown: string | null;
  readonly sortOrder: number;
  readonly isPreview: boolean;
  readonly status: PublicationStatus;
  readonly estimatedDurationSeconds: number | null;
  readonly videoStatus: string | null;
  readonly rowVersion: string;
}

/** A section as the editor sees it. */
export interface AdminSection {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly sortOrder: number;
  readonly status: PublicationStatus;
  readonly lessons: readonly AdminLesson[];
  readonly rowVersion: string;
}

/** A catalog tag. */
export interface AdminTag {
  readonly id: string;
  readonly name: string;
  readonly normalizedName: string;
}

/** Full editor detail for one course. */
export interface AdminCourseDetail {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly description: string;
  readonly level: CourseLevel;
  readonly status: PublicationStatus;
  readonly includedInMembership: boolean;
  readonly imageAltText: string | null;
  readonly publishedAtUtc: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly slugLocked: boolean;
  readonly sections: readonly AdminSection[];
  readonly tags: readonly AdminTag[];
  readonly rowVersion: string;
}

/** Creates a Draft course. */
export interface CreateCourseRequest {
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly description: string;
  readonly level: CourseLevel;
  readonly includedInMembership: boolean;
}

/** Updates a course's editable metadata. */
export interface UpdateCourseRequest extends CreateCourseRequest {
  readonly imageAltText: string | null;
  readonly rowVersion: string;
}

/** A status command, always carrying the reason the audit trail records. */
export interface StatusChangeRequest {
  readonly reason: string;
  readonly rowVersion: string;
}

/** One entry in an exact-set reorder payload. */
export interface ReorderItem {
  readonly id: string;
  readonly rowVersion: string;
}

/** Creates a section. */
export interface CreateSectionRequest {
  readonly title: string;
  readonly description: string | null;
}

/** Updates a section. */
export interface UpdateSectionRequest extends CreateSectionRequest {
  readonly rowVersion: string;
}

/** Creates a lesson. */
export interface CreateLessonRequest {
  readonly slug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly lessonType: LessonType;
  readonly bodyMarkdown: string | null;
  readonly isPreview: boolean;
  readonly estimatedDurationSeconds: number | null;
}

/** Updates a lesson. */
export interface UpdateLessonRequest extends CreateLessonRequest {
  readonly rowVersion: string;
}

/** Formats a duration in seconds as a short human-readable label. */
export function formatDuration(seconds: number | null): string {
  if (seconds === null || seconds <= 0) {
    return '—';
  }

  const minutes = Math.round(seconds / 60);

  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;

  return remainder === 0 ? `${hours} h` : `${hours} h ${remainder} min`;
}

/** Human-readable level label. */
export function formatLevel(level: CourseLevel): string {
  return COURSE_LEVELS.find((entry) => entry.value === level)?.label ?? level;
}
