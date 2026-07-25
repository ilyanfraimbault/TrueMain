/**
 * Riot ID handling for typed input (`Name#TAG`) — the form a player pastes
 * from the client, as opposed to the `Name-TAG` slug used in our URLs.
 *
 * Mirrors `NameTagParser.TryParseRiotId` on the backend: the first `#` splits
 * the two halves, both must be non-empty, and a second `#` makes the whole
 * thing junk. Keeping the guard here means the comparison panel can disable its
 * submit button instead of firing a request the API answers with a 400.
 */
export interface ParsedRiotId {
  gameName: string
  tagLine: string
}

/**
 * Upper bound on an accepted Riot ID. Keep in sync with
 * `NameTagParser.MaxRiotIdLength` on the backend — no shared contract enforces
 * it, and a drift here would let the panel fire a request the API answers with
 * a 400 (which the composable swallows, leaving the user with no feedback).
 */
export const RIOT_ID_MAX_LENGTH = 64

/** Parses `Name#TAG`; null when the input isn't a well-formed Riot ID. */
export function parseRiotId(input: string | null | undefined): ParsedRiotId | null {
  if (input != null && input.length > RIOT_ID_MAX_LENGTH) return null

  const trimmed = input?.trim()
  if (!trimmed) return null

  const hash = trimmed.indexOf('#')
  if (hash < 0) return null

  const gameName = trimmed.slice(0, hash).trim()
  const tagLine = trimmed.slice(hash + 1).trim()
  if (!gameName || !tagLine || tagLine.includes('#')) return null

  return { gameName, tagLine }
}

/** Whether the input is a Riot ID the API will accept. */
export function isValidRiotId(input: string | null | undefined): boolean {
  return parseRiotId(input) !== null
}

/**
 * Renders an identity back as the typed form. `tagLine` is nullable on rows
 * ingested before tag lines were stored — those can't be addressed as a Riot
 * ID at all, so they yield null rather than a half-formed `Name#`.
 */
export function formatRiotId(gameName: string, tagLine: string | null): string | null {
  if (!gameName || !tagLine) return null
  return `${gameName}#${tagLine}`
}
