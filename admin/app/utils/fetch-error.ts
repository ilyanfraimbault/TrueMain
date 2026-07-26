interface FetchErrorLike {
  data?: {
    // RFC 7807 ProblemDetails, as returned by the backend (and injected with a
    // traceId — see Program.cs's CustomizeProblemDetails).
    detail?: string
    title?: string
    traceId?: string
    // Local Nuxt `createError({ statusMessage })` bodies (e.g. admin's own
    // login route) don't go through ProblemDetails at all.
    message?: string
    statusMessage?: string
  }
  statusMessage?: string
  message?: string
}

// Pull a human message out of an ofetch error. Backend failures arrive as
// ProblemDetails (`data.detail`, falling back to `data.title` for a bare
// `NotFound()` with no detail text); local `createError` bodies carry
// `data.message`/`data.statusMessage` instead. `e.statusMessage`/`e.message`
// (ofetch's own generic HTTP-status text) are the last resort before
// `fallback`, so a backend detail/title is always preferred over a bare
// "Not Found" when both are present.
export function extractFetchError(err: unknown, fallback = 'Unexpected error'): string {
  const e = err as FetchErrorLike
  return (
    e?.data?.detail
    ?? e?.data?.title
    ?? e?.data?.message
    ?? e?.data?.statusMessage
    ?? e?.statusMessage
    ?? e?.message
    ?? fallback
  )
}

// The backend's per-request traceId (see Program.cs's CustomizeProblemDetails),
// shown to operators so a reported error can be matched to server logs.
// Absent for errors that never reached the backend (network drop, admin's own
// local createError calls).
export function extractFetchErrorTraceId(err: unknown): string | undefined {
  return (err as FetchErrorLike)?.data?.traceId
}
