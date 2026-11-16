import type { ApiError } from '~/generated/shell'

/**
 * An error status TrueMain answered, shaped the way the shared pages read one
 * from ofetch: `statusCode` on an `Error` (`fetchErrorStatus`). That keeps a
 * 404 "not found" in the app too (#2476), and the error copy names the right
 * cause. It prints as the shell words it, like the other refusals.
 */
class TrueMainAnswer extends Error {
  constructor(message: string, readonly statusCode: number) {
    super(message)
  }

  override toString() {
    return this.message
  }
}

const isApiError = (error: unknown): error is ApiError =>
  typeof error === 'object' && error !== null && typeof (error as { kind?: unknown }).kind === 'string'

/** A read a newer one under the same `replaces` key took the place of. */
export const isSuperseded = (error: unknown) => isApiError(error) && error.kind === 'superseded'

/**
 * A read the shell refused (`api/mod.rs`'s `ApiError`), as the app's callers
 * read it: a status as `TrueMainAnswer`, a superseded read as itself, anything
 * else — "could not reach TrueMain" included — as the shell's words, so an
 * error, never "not found".
 */
export function asFetchError(error: unknown): unknown {
  if (!isApiError(error)) return error
  switch (error.kind) {
    case 'answered': return new TrueMainAnswer(error.message, error.status)
    case 'superseded': return error
    default: return error.message
  }
}
