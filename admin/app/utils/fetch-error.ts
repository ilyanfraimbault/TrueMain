// Pull a human message out of an ofetch error (400 body, then statusMessage,
// then the generic error message). `fallback` is the last resort when the
// error carries no usable text.
export function extractFetchError(err: unknown, fallback = 'Unexpected error'): string {
  const e = err as {
    data?: { message?: string, statusMessage?: string }
    statusMessage?: string
    message?: string
  }
  return (
    e?.data?.message
    ?? e?.data?.statusMessage
    ?? e?.statusMessage
    ?? e?.message
    ?? fallback
  )
}
