import { HttpErrorResponse } from '@angular/common/http';

import { ERROR_CODES, isConcurrencyConflict, toApiFailure } from './problem-details';

function problem(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body, statusText: 'Error' });
}

describe('toApiFailure', () => {
  it('reads the stable code and flattens field errors', () => {
    const failure = toApiFailure(
      problem(400, {
        detail: 'The request could not be accepted.',
        code: ERROR_CODES.validationFailed,
        errors: { slug: ['Too short.', 'Wrong shape.'], title: ['Required.'] },
      }),
    );

    expect(failure.code).toBe(ERROR_CODES.validationFailed);
    expect(failure.status).toBe(400);
    expect(failure.fieldErrors).toHaveLength(3);
    expect(failure.fieldErrors.map((entry) => entry.field)).toContain('title');
  });

  it('recognises a lost race as something to reload, not to retry', () => {
    const failure = toApiFailure(problem(409, { code: ERROR_CODES.concurrencyConflict }));

    expect(isConcurrencyConflict(failure)).toBe(true);
  });

  it('does not treat a validation failure as a lost race', () => {
    const failure = toApiFailure(problem(400, { code: ERROR_CODES.validationFailed }));

    expect(isConcurrencyConflict(failure)).toBe(false);
  });

  it('falls back to a status-appropriate sentence when the API sends no detail', () => {
    expect(toApiFailure(problem(403, {})).message).toContain('do not have access');
    expect(toApiFailure(problem(429, {})).message).toContain('Wait a moment');
    expect(toApiFailure(problem(0, null)).message).toContain('could not reach the server');
  });

  it('never surfaces the raw error body', () => {
    const failure = toApiFailure(
      problem(500, { detail: 'A safe sentence.', secret: 'eyJhbGciOi.token.value' }),
    );

    expect(failure.message).toBe('A safe sentence.');
    expect(JSON.stringify(failure)).not.toContain('eyJhbGciOi');
  });

  it('handles a non-HTTP failure without throwing', () => {
    const failure = toApiFailure(new Error('boom'), 'Fallback sentence.');

    expect(failure.status).toBe(0);
    expect(failure.message).toBe('Fallback sentence.');
    expect(failure.fieldErrors).toEqual([]);
  });
});
