/**
 * The one definition of "well-formed Riot ID" for the admin portal.
 *
 * Mirrors `NameTagParser` on the backend (`backend/Api/Services/Truemains/
 * NameTagParser.cs`), which is what the ops endpoints validate against: a
 * length cap, the FIRST `#` splitting the two halves, both halves non-empty,
 * and a second `#` making the whole thing junk. Before this module the admin
 * had its own hand-rolled `indexOf('#')` inside `pages/seed.vue` that enforced
 * neither the cap nor the second-`#` rule, so the bulk-seed preview counted as
 * valid lines that `GET /ops/accounts/{nameTag}` answers with a 400.
 *
 * Deliberately a copy of `web/app/utils/riot-id.ts` rather than a shared
 * package: `web/` and `admin/` are two standalone Nuxt apps with no workspace
 * between them, and that separation is intentional. What this file removes is
 * the duplication that actually hurt — the third copy living *inside* admin.
 * Kept under `shared/` so it is importable without the Nuxt runtime, which is
 * also the only way it can be tested (the admin suite has no page harness).
 */

/** Both halves of a Riot ID, already trimmed. */
export interface ParsedRiotId {
  gameName: string
  tagLine: string
}

/**
 * Upper bound on an accepted Riot ID. Keep in sync with
 * `NameTagParser.MaxRiotIdLength` on the backend — no shared contract enforces
 * it. The database caps GameName at 32 and TagLine at 8, so a real Riot ID is
 * at most 41 characters and nothing legitimate sits near this bound; anything
 * past it is junk or abuse, and the ops endpoints reject it with a 400.
 */
export const RIOT_ID_MAX_LENGTH = 64

/**
 * Best-effort split of the typed form into its two halves, whether or not the
 * result is valid. The preview table still shows the pieces of a malformed
 * line, so the split and the verdict have to be available separately —
 * `riotIdError` is the verdict over exactly this split.
 */
export function splitRiotId(input: string): ParsedRiotId {
  const trimmed = input.trim()
  const hash = trimmed.indexOf('#')
  if (hash < 0) {
    return { gameName: trimmed, tagLine: '' }
  }
  return {
    gameName: trimmed.slice(0, hash).trim(),
    tagLine: trimmed.slice(hash + 1).trim(),
  }
}

/**
 * Why `input` is not a well-formed typed Riot ID, or null when it is. The
 * wording is operator-facing: it lands in the bulk-seed preview's "reason"
 * column, so each rule says what to fix rather than that something is wrong.
 */
export function riotIdError(input: string): string | null {
  // Length is measured before trimming, exactly as the backend does, so the
  // two never disagree on a padded line that straddles the bound.
  if (input.length > RIOT_ID_MAX_LENGTH) {
    return `Longer than ${RIOT_ID_MAX_LENGTH} characters`
  }

  const trimmed = input.trim()
  if (!trimmed.includes('#')) {
    return 'Missing "#tagLine"'
  }

  const { gameName, tagLine } = splitRiotId(trimmed)
  if (!gameName) {
    return 'Missing game name'
  }
  if (!tagLine) {
    return 'Missing tag line'
  }
  // Riot game names cannot contain a '#', so the first one is always the
  // separator and a second one is part of a junk tag.
  if (tagLine.includes('#')) {
    return 'Tag line cannot contain a second "#"'
  }

  return null
}

/** Parses the typed form `Name#TAG`; null when it isn't a well-formed Riot ID. */
export function parseRiotId(input: string | null | undefined): ParsedRiotId | null {
  if (input == null || riotIdError(input) !== null) {
    return null
  }
  return splitRiotId(input)
}

/**
 * Parses a Riot ID in either form the ops endpoints accept: typed
 * (`Name#TAG`), or the hyphen slug the public routes use (`Name-TAG`, split on
 * the LAST `-` so game names may contain hyphens). This is the full contract of
 * `NameTagParser.TryParseRiotId`, which is what `GET /ops/accounts/{nameTag}`
 * validates against — the account explorer must not refuse a spelling the
 * endpoint would have answered.
 */
export function parseRiotIdOrSlug(input: string | null | undefined): ParsedRiotId | null {
  if (input == null || input.length > RIOT_ID_MAX_LENGTH) {
    return null
  }

  const trimmed = input.trim()
  if (!trimmed) {
    return null
  }
  if (trimmed.includes('#')) {
    return parseRiotId(trimmed)
  }

  const idx = trimmed.lastIndexOf('-')
  if (idx <= 0 || idx === trimmed.length - 1) {
    return null
  }

  const gameName = trimmed.slice(0, idx)
  const tagLine = trimmed.slice(idx + 1)
  if (!gameName.trim() || !tagLine.trim()) {
    return null
  }

  return { gameName, tagLine }
}

/** Whether the ops endpoints will accept `input` as a Riot ID (either form). */
export function isRiotIdOrSlug(input: string | null | undefined): boolean {
  return parseRiotIdOrSlug(input) !== null
}

/**
 * Renders an identity back as the typed form. Both halves are required: a row
 * missing one cannot be addressed as a Riot ID at all, so it yields null rather
 * than a half-formed `Name#`.
 */
export function formatRiotId(
  gameName: string | null | undefined,
  tagLine: string | null | undefined,
): string | null {
  if (!gameName || !tagLine) {
    return null
  }
  return `${gameName}#${tagLine}`
}
