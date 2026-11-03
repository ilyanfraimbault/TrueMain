import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import type { PersonaListRow } from '~~/shared/types/ops'
import { formatRiotId, parseRiotIdOrSlug } from '~~/shared/utils/riot-id'

// What the ⌘K palette recognises in the typed term (#2290), on top of the pages:
// a Riot ID, a known player, a match ID, a champion. Each one becomes a result
// group placed above the pages, each item a deep link the target page reads from
// its query string. Pure, so the recognition rules are tested without the palette.

/** Below this many characters the palette lists pages only and sends no request. */
export const ENTITY_SEARCH_MIN_LENGTH = 2

/** Known players listed for one term: a jump list, not the directory. */
export const PERSONA_RESULT_LIMIT = 6

/** Champions listed for one term. */
export const CHAMPION_RESULT_LIMIT = 5

// Riot match IDs are `<platform>_<game id>`: `EUW1_7012345678`, `KR_7301234567`.
const MATCH_ID_PATTERN = /^([A-Z]{2,4}\d?)_(\d{6,})$/

/** The match ID the term spells, upper-cased, or null. */
export function parseMatchId(term: string): string | null {
  const candidate = term.trim().toUpperCase()
  return MATCH_ID_PATTERN.test(candidate) ? candidate : null
}

/** Whether the term is long enough for the entity groups (and their request). */
export function isEntityTerm(term: string): boolean {
  return term.trim().length >= ENTITY_SEARCH_MIN_LENGTH
}

export interface PaletteChampion {
  name: string
  iconUrl?: string | null
}

/**
 * Champions whose name contains the term, ignoring case, spaces and punctuation
 * (`kaisa` finds Kai'Sa, `lee sin` and `leesin` find Lee Sin). Names starting with
 * the term come first.
 */
export function matchChampions<T extends PaletteChampion>(term: string, champions: readonly T[], limit = CHAMPION_RESULT_LIMIT): T[] {
  const needle = foldName(term)
  if (needle.length < ENTITY_SEARCH_MIN_LENGTH) {
    return []
  }
  return champions
    .map(champion => ({ champion, at: foldName(champion.name).indexOf(needle) }))
    .filter(entry => entry.at >= 0)
    .sort((a, b) => Number(a.at > 0) - Number(b.at > 0) || a.champion.name.localeCompare(b.champion.name))
    .slice(0, limit)
    .map(entry => entry.champion)
}

function foldName(value: string): string {
  return value.toLowerCase().replaceAll(/[^\p{L}\p{N}]/gu, '')
}

export interface EntitySearchSources {
  /** Known players the ops API matched for the term; empty until it answered. */
  personas: readonly PersonaListRow[]
  champions: readonly PaletteChampion[]
}

/**
 * The entity groups for a term, in the order they should sit above the pages.
 * Every group sets `ignoreFilter`: its items were matched here (or by the API),
 * and the palette's fuzzy filter would drop a `Faker#KR1` trace item whose label
 * does not literally contain the term.
 */
export function entitySearchGroups(term: string, sources: EntitySearchSources): CommandPaletteGroup<CommandPaletteItem>[] {
  const trimmed = term.trim()
  if (!isEntityTerm(trimmed)) {
    return []
  }

  const riotId = riotIdGroup(trimmed)
  const groups = [
    personaGroup(sources.personas),
    matchGroup(trimmed),
    championGroup(trimmed, sources.champions),
  ]
  // A typed `Name#TAG` is unambiguous, so its group leads. The hyphen form also
  // reads as a known player's slug (`caps-g2`), whose own group is the likelier intent.
  groups.splice(trimmed.includes('#') ? 0 : 1, 0, riotId)
  return groups.filter((group): group is CommandPaletteGroup<CommandPaletteItem> => group !== null)
}

function group(id: string, label: string, items: CommandPaletteItem[]): CommandPaletteGroup<CommandPaletteItem> | null {
  return items.length ? { id: `entity-${id}`, label, ignoreFilter: true, items } : null
}

function riotIdGroup(term: string) {
  const parsed = parseRiotIdOrSlug(term)
  if (!parsed) {
    return null
  }
  const riotId = formatRiotId(parsed.gameName.trim(), parsed.tagLine.trim()) ?? term
  return group('riot-id', 'Riot ID', [
    {
      label: `Trace ${riotId}`,
      description: 'Every stage of the pipeline for this account',
      icon: 'i-lucide-user-search',
      to: { path: '/accounts', query: { view: 'trace', riotId } },
    },
    {
      label: `Add ${riotId} as a main`,
      description: 'The Add mains form, prefilled',
      icon: 'i-lucide-user-plus',
      to: { path: '/accounts', query: { view: 'seed', riotId } },
    },
  ])
}

function personaGroup(personas: readonly PersonaListRow[]) {
  return group('known-players', 'Known players', personas.slice(0, PERSONA_RESULT_LIMIT).map(persona => ({
    label: persona.displayName,
    description: persona.removedAtUtc ? `${persona.slug} · removed` : persona.slug,
    icon: 'i-lucide-badge-check',
    to: { path: '/known-players', query: { player: persona.id } },
  })))
}

function matchGroup(term: string) {
  const matchId = parseMatchId(term)
  return group('match', 'Match', matchId
    ? [{
        label: `Inspect ${matchId}`,
        description: 'Data Quality match detail',
        icon: 'i-lucide-shield-alert',
        to: { path: '/data-quality', query: { match: matchId } },
      }]
    : [])
}

function championGroup(term: string, champions: readonly PaletteChampion[]) {
  return group('champions', 'Champions', matchChampions(term, champions).map(champion => ({
    label: champion.name,
    description: 'Champions table, filtered',
    ...(champion.iconUrl ? { avatar: { src: champion.iconUrl, alt: '' } } : { icon: 'i-lucide-swords' }),
    to: { path: '/champions', query: { search: champion.name } },
  })))
}
