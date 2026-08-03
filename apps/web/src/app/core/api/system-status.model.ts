/**
 * Strongly-typed shape of the `GET /api/v1/system/status` response. Mirrors the
 * backend contract exactly and exposes only safe, non-sensitive fields.
 */
export interface SystemStatus {
  readonly status: string;
  readonly service: string;
  readonly environment: string;
  readonly utcTimestamp: string;
}
