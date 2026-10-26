import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import type { ChampionStaticListItem } from '#site-shared/types/static-data'
import type { PersonaListRow } from '~~/shared/types/ops'
import { computed, ref, shallowRef, watch } from 'vue'
import { STATIC_CHAMPIONS_KEY } from '~/composables/useChampionStatic'
import { buildSearchGroups } from '~/utils/navigation'
import { opsCache } from '~/utils/ops-cache'
import { entitySearchGroups, isEntityTerm, PERSONA_RESULT_LIMIT } from '~/utils/palette-search'

/**
 * The ⌘K palette's groups for the typed term (#2290): the entity groups of
 * `utils/palette-search.ts` above the pages of `utils/navigation.ts`.
 *
 * Riot IDs, match IDs and champions are recognised on every keystroke; the known
 * players need the ops API, so that call waits for the term to settle and is never
 * sent under `ENTITY_SEARCH_MIN_LENGTH` characters. The champion list is loaded on
 * the first such term rather than with the layout, through the cache entry
 * `useChampionStatic` shares.
 */
export function usePaletteSearch(debounceMs = 250) {
  const term = ref('')
  const settled = refDebounced(term, debounceMs)
  const personas = shallowRef<readonly PersonaListRow[]>([])
  const personasTerm = ref('')
  const champions = shallowRef<readonly ChampionStaticListItem[]>([])
  const loading = ref(false)
  const pages = buildSearchGroups()

  let championsRequested = false
  async function loadChampions() {
    if (championsRequested) {
      return
    }
    championsRequested = true
    const cached = opsCache.get(STATIC_CHAMPIONS_KEY)?.value as ChampionStaticListItem[] | undefined
    if (cached) {
      champions.value = cached
      return
    }
    try {
      champions.value = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
      opsCache.set(STATIC_CHAMPIONS_KEY, champions.value)
    }
    catch {
      // No champion group this time; the next term retries.
      championsRequested = false
    }
  }

  // Answers can land out of order; only the one for the latest term is kept.
  let latest = 0
  watch(settled, async (value) => {
    const search = value.trim()
    const request = ++latest
    if (!isEntityTerm(search)) {
      personas.value = []
      loading.value = false
      return
    }
    void loadChampions()
    loading.value = true
    try {
      const page = await searchPersonas(search, PERSONA_RESULT_LIMIT)
      if (request === latest) {
        personas.value = page.personas
        personasTerm.value = search
      }
    }
    catch {
      if (request === latest) {
        personas.value = []
      }
    }
    finally {
      if (request === latest) {
        loading.value = false
      }
    }
  })

  const groups = computed<CommandPaletteGroup<CommandPaletteItem>[]>(() => [
    ...entitySearchGroups(term.value, {
      // While the term moves, the last answer stays up as long as one term extends
      // the other (`fak` → `faker`): blanking it on every keystroke would flicker.
      personas: related(term.value.trim(), personasTerm.value) ? personas.value : [],
      champions: champions.value,
    }),
    ...pages,
  ])

  return { term, groups, loading }
}

function related(current: string, answered: string): boolean {
  const a = current.toLowerCase()
  const b = answered.toLowerCase()
  return b !== '' && (a.startsWith(b) || b.startsWith(a))
}
