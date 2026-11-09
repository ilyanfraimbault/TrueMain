// Types for the admin Logs panel (`GET /api/ops/logs`), split out of `ops.ts`,
// which is over the file-size limit and may only shrink.

/**
 * .NET `LogLevel` names, ascending in severity. Used by `GET /api/ops/logs`
 * where the `level` filter is a MINIMUM threshold (e.g. `Warning` returns
 * Warning + Error + Critical).
 */
export type LogLevel
  = | 'Trace'
    | 'Debug'
    | 'Information'
    | 'Warning'
    | 'Error'
    | 'Critical'
