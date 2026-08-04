import { HttpErrorResponse } from '@angular/common/http';

import { FormErrorEntry } from '../../shared/ui/form-error-summary/form-error-summary';

/** RFC 7807 payload plus the stable `code` extension the API always sends on a refusal. */
export interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly code?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/** Stable error codes shared with the API. Branch on these, never on wording. */
export const ERROR_CODES = {
  concurrencyConflict: 'platform.concurrency_conflict',
  invalidRowVersion: 'platform.invalid_row_version',
  validationFailed: 'platform.validation_failed',
  invalidTransition: 'catalog.invalid_transition',
  publishPrerequisite: 'catalog.publish_prerequisite',
  slugLocked: 'catalog.slug_locked',
  reorderMismatch: 'catalog.reorder_mismatch',
  duplicateValue: 'platform.duplicate_value',
  priceImmutable: 'commerce.price_immutable',
  commerceRule: 'commerce.rule_violation',
  communitySetupRequired: 'community.setup_required',
  communityForbidden: 'community.forbidden',
  communityBlocked: 'community.blocked',
  rateLimited: 'platform.rate_limited',
} as const;

/** A refusal, reduced to what the UI is allowed to show. */
export interface ApiFailure {
  /** Stable machine-readable code, when the API supplied one. */
  readonly code: string | null;

  /** HTTP status, or 0 when the request never reached the server. */
  readonly status: number;

  /** One sentence safe to display. */
  readonly message: string;

  /** Field-level messages for a form summary. */
  readonly fieldErrors: readonly FormErrorEntry[];
}

/**
 * Turns an HTTP error into something a screen can render.
 *
 * Only the title, detail, code, and field messages are read. The raw body is never displayed
 * or logged wholesale, because an error payload from any layer can contain material — tokens,
 * claims, connection detail — that must not reach a browser or a console.
 */
export function toApiFailure(error: unknown, fallback = 'Something went wrong.'): ApiFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return { code: null, status: 0, message: fallback, fieldErrors: [] };
  }

  const problem = (error.error ?? {}) as ProblemDetails;
  const fieldErrors: FormErrorEntry[] = [];

  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    for (const message of messages) {
      fieldErrors.push({ field, message });
    }
  }

  return {
    code: problem.code ?? null,
    status: error.status,
    message: messageFor(error.status, problem, fallback),
    fieldErrors,
  };
}

/** Whether a failure means "reload and try again" rather than "fix your input". */
export function isConcurrencyConflict(failure: ApiFailure): boolean {
  return (
    failure.code === ERROR_CODES.concurrencyConflict ||
    failure.code === ERROR_CODES.invalidRowVersion
  );
}

function messageFor(status: number, problem: ProblemDetails, fallback: string): string {
  if (problem.detail) {
    return problem.detail;
  }

  switch (status) {
    case 0:
      return 'We could not reach the server. Check your connection and try again.';
    case 401:
      return 'Your session has ended. Sign in again to continue.';
    case 403:
      return 'You do not have access to this.';
    case 404:
      return 'That is no longer available.';
    case 409:
      return 'This record changed while you were editing it. Reload and reapply your change.';
    case 429:
      return 'That was a lot of requests in a short time. Wait a moment and try again.';
    default:
      return fallback;
  }
}
