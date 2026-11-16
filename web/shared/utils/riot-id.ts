/**
 * The one definition of a well-formed Riot ID on the front ends — the site, the
 * desktop app and the admin all read it from here (`#shared` / `#site-shared`).
 *
 * Mirrors `NameTagParser` on the backend (`backend/Api/Services/Truemains/Identity/
 * NameTagParser.cs`), which every endpoint taking a Riot ID validates against:
 *
 * - the typed form `Name#TAG` splits on the FIRST `#`, both halves trimmed and
 *   non-empty, and a second `#` makes the whole thing junk (`TryParseRiotId`);
 * - the URL slug `Name-TAG` splits on the LAST `-`, so a game name keeps its own
 *   hyphens (`TryParse`, #948);
 * - an endpoint accepts either form, at most {@link RIOT_ID_MAX_LENGTH} characters.
 *
 * A front end that disagreed would submit what the API answers with a 400, or
 * refuse a spelling the API would have answered. `tests/riot-id` holds the cases.
 */

/** Both halves of a Riot ID, already trimmed. */
export interface ParsedRiotId {
  gameName: string
  tagLine: string
}

/**
 * `NameTagParser.MaxRiotIdLength`. The database caps GameName at 32 and TagLine
 * at 8, so a real Riot ID is at most 41 characters; anything past this is junk.
 * Measured before trimming, as the backend does.
 */
export const RIOT_ID_MAX_LENGTH = 64

/**
 * Best-effort split of the typed form into its two halves, whether or not the
 * result is valid: the admin's bulk-seed preview shows the pieces of a
 * malformed line, so the split and the verdict ({@link riotIdError}) are separate.
 */
export function splitRiotId(input: string): ParsedRiotId {
  const trimmed = input.trim()
  const hash = trimmed.indexOf('#')
  if (hash < 0) return { gameName: trimmed, tagLine: '' }
  return {
    gameName: trimmed.slice(0, hash).trim(),
    tagLine: trimmed.slice(hash + 1).trim(),
  }
}

/** Why `input` is not a well-formed typed Riot ID, worded as what to fix; null when it is. */
export function riotIdError(input: string): string | null {
  if (input.length > RIOT_ID_MAX_LENGTH) return `Longer than ${RIOT_ID_MAX_LENGTH} characters`
  const trimmed = input.trim()
  if (!trimmed.includes('#')) return 'Missing "#tagLine"'
  const { gameName, tagLine } = splitRiotId(trimmed)
  if (!gameName) return 'Missing game name'
  if (!tagLine) return 'Missing tag line'
  // Riot game names cannot contain a '#', so a second one is part of a junk tag.
  if (tagLine.includes('#')) return 'Tag line cannot contain a second "#"'
  return null
}

/** Parses the typed form `Name#TAG`; null when it isn't a well-formed Riot ID. */
export function parseRiotId(input: string | null | undefined): ParsedRiotId | null {
  if (input == null || riotIdError(input) !== null) return null
  return splitRiotId(input)
}

/** Whether the input is a typed Riot ID the API will accept. */
export function isValidRiotId(input: string | null | undefined): boolean {
  return parseRiotId(input) !== null
}

/** Parses the URL slug `{gameName}-{tagLine}` on its last `-`; null when either half is blank. */
export function parseRiotIdSlug(slug: string): ParsedRiotId | null {
  if (!slug.trim()) return null
  const idx = slug.lastIndexOf('-')
  if (idx <= 0 || idx === slug.length - 1) return null
  const gameName = slug.slice(0, idx)
  const tagLine = slug.slice(idx + 1)
  if (!gameName.trim() || !tagLine.trim()) return null
  return { gameName, tagLine }
}

/** Parses either form an endpoint accepts: typed `Name#TAG`, or the slug `Name-TAG`. */
export function parseRiotIdOrSlug(input: string | null | undefined): ParsedRiotId | null {
  if (input == null || input.length > RIOT_ID_MAX_LENGTH) return null
  const trimmed = input.trim()
  if (!trimmed) return null
  return trimmed.includes('#') ? parseRiotId(trimmed) : parseRiotIdSlug(trimmed)
}

/** Whether an endpoint will accept `input` as a Riot ID (either form). */
export function isRiotIdOrSlug(input: string | null | undefined): boolean {
  return parseRiotIdOrSlug(input) !== null
}

/**
 * The typed form back from an identity. Both halves are required: a row ingested
 * before tag lines were stored cannot be addressed as a Riot ID, so it yields null.
 */
export function formatRiotId(gameName: string | null | undefined, tagLine: string | null | undefined): string | null {
  if (!gameName || !tagLine) return null
  return `${gameName}#${tagLine}`
}
