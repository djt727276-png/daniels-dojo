/** A page of results from a catalog endpoint. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
}

/**
 * A price exactly as the API returns it: integer minor units plus currency and interval.
 * The amount is never hard-coded in the client — see `formatPrice`.
 */
export interface PublicPrice {
  readonly offerId: string;
  readonly amountMinor: number;
  readonly currency: string;
  readonly interval: string;
}

/** Card-level course summary shown in the catalog list. */
export interface CourseCard {
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly level: string;
  readonly includedInMembership: boolean;
  readonly publishedAtUtc: string | null;
  readonly tags: readonly string[];
  readonly membershipPrice: PublicPrice | null;
  readonly lifetimePrice: PublicPrice | null;
}

/** One lesson in the public outline. Never carries body content. */
export interface CourseOutlineLesson {
  readonly slug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly lessonType: string;
  readonly isPreview: boolean;
  readonly estimatedDurationSeconds: number | null;
}

/** One section in the public outline. */
export interface CourseOutlineSection {
  readonly title: string;
  readonly description: string | null;
  readonly lessons: readonly CourseOutlineLesson[];
}

/** Full public course detail. */
export interface CourseDetail {
  readonly slug: string;
  readonly title: string;
  readonly summary: string;
  readonly description: string;
  readonly level: string;
  readonly includedInMembership: boolean;
  readonly publishedAtUtc: string | null;
  readonly imageAltText: string | null;
  readonly tags: readonly string[];
  readonly sections: readonly CourseOutlineSection[];
  readonly membershipPrice: PublicPrice | null;
  readonly lifetimePrice: PublicPrice | null;
}

/** A preview lesson's stored plain text. */
export interface LessonPreview {
  readonly courseSlug: string;
  readonly courseTitle: string;
  readonly lessonSlug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly body: string;
}

/** Filters the catalog list accepts. */
export interface CatalogFilters {
  readonly search: string;
  readonly level: string;
  readonly tag: string;
  readonly page: number;
  readonly pageSize: number;
}

/** Difficulty values the API accepts, used to build the filter control. */
export const COURSE_LEVELS = ['Beginner', 'Intermediate', 'Advanced', 'AllLevels'] as const;

/**
 * Formats a minor-unit price with the currency and interval the API returned.
 *
 * The amount, currency, and cadence all come from the response — nothing about pricing is
 * decided here, so a price change in the database needs no frontend change.
 */
export function formatPrice(price: PublicPrice | null): string {
  if (!price) {
    return '';
  }

  const amount = new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: price.currency,
  }).format(price.amountMinor / 100);

  return price.interval === 'Month' ? `${amount}/month` : amount;
}

/** Human-readable label for a difficulty value. */
export function formatLevel(level: string): string {
  return level === 'AllLevels' ? 'All levels' : level;
}
