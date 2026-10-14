<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { isNavigationFailure } from 'vue-router'
import type { SearchPlayer, SearchResult } from '#shared/types/search'
import { playerPath } from '#shared/utils/player-path'
import { riotIdLabel, truemainNameTag, truemainProfilePath } from '#shared/utils/truemain-path'
import { describeFetchError } from '#common/utils/errors'
import { SEARCH_MAX_LIMIT } from '#common/composables/useTruemainSearch'
import type { TruemainSearchState } from '#common/utils/truemain-search-state'
import { TRUEMAIN_SEARCH_MESSAGES, truemainSearchAnnouncement, truemainSearchState } from '#common/utils/truemain-search-state'

// Unified search: one command palette over both the champion roster (filtered
// locally by Fuse) and the truemain database (server search, debounced). A
// trigger (`SearchTrigger`) opens a modal holding the palette. `useTruemainSearch`
// is instantiated in setup — so it runs on every page, since the header mounts
// this component — but stays inert while the modal is closed: `term` is `''`, so
// its watcher returns early without firing a request or a debounce timer. The
// one player search of both apps: the site's header and pages, and the app's
// truemains and build pages (#2027).
const props = withDefaults(defineProps<{
  /** `field` = full search-bar trigger (page hero); `button` = compact icon (header). */
  variant?: 'field' | 'button'
  /**
   * What selecting a champion does:
   * - `navigate` (default): go to `/champions/{slug}`.
   * - `filter`: emit `filterChampion` and stay put (used on the leaderboard,
   *   where a champion narrows the list instead of leaving the page).
   */
  championMode?: 'navigate' | 'filter'
  /**
   * Currently-applied champion filter id, or `null` for "all champions". Only
   * meaningful in `filter` mode: when set, the trigger shows that champion and
   * the palette surfaces an "All champions" reset row.
   */
  activeChampionId?: number | null
  /**
   * `players` searches truemains alone — no champion or browse group — and
   * hands the picked one to the host (`selectTruemain`) rather than opening
   * their profile: the app's build page, which shows that main's build.
   */
  scope?: 'all' | 'players'
  /** With `scope="players"`, keep only the mains of this champion. */
  mainsOf?: number | null
  placeholder?: string
  /**
   * Register the ⌘K shortcut on this instance. Set only on the header instance
   * (mounted on every page) so the shortcut isn't bound twice on pages that
   * also render a field instance (home, leaderboard).
   */
  shortcut?: boolean
  /** Field trigger height (`SearchTrigger`); ignored for the `button` variant. */
  size?: 'sm' | 'md' | 'lg'
}>(), {
  variant: 'field',
  championMode: 'navigate',
  activeChampionId: null,
  scope: 'all',
  mainsOf: null,
  placeholder: 'Search a champion or player…',
  shortcut: false,
  size: 'md',
})

const emit = defineEmits<{
  /** A champion id, or `null` from the reset row / the trigger's clear button. */
  filterChampion: [championId: number | null]
  /** `scope="players"` only. */
  selectTruemain: [result: SearchResult]
}>()

const open = ref(false)
const term = ref('')
const router = useRouter()
const { pathFor } = useChampionSlugs()
const playersOnly = computed(() => props.scope === 'players')
const label = computed(() => (playersOnly.value ? 'Search a truemain' : 'Search a champion or player'))

// Champions — shared static list, filtered locally by the palette's Fuse; by
// id for the mains on each truemain row. One cached read for both.
const { data: champions } = useChampionStaticList()
const championsById = useChampionsById(champions)

const activeChampion = computed(() => {
  if (props.championMode !== 'filter' || props.activeChampionId == null) return null
  return championsById.value.get(props.activeChampionId) ?? null
})

// Truemains — debounced server search. Narrowed to one champion's mains, it
// asks for the backend's largest page so the narrowing has rows to keep.
const { results, players, status, error: truemainSearchError, tooShort } = useTruemainSearch(term, { limit: props.mainsOf == null ? undefined : SEARCH_MAX_LIMIT })
const matches = computed(() => (props.mainsOf == null
  ? results.value
  : results.value.filter(result => result.topChampionIds.includes(props.mainsOf!))))

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

function go(path: string) {
  open.value = false
  // Swallow only aborted/redirected navigations; let real errors surface.
  router.push(path).catch((error) => {
    if (!isNavigationFailure(error)) throw error
  })
}

function onSelectChampion(championId: number) {
  if (props.championMode === 'filter') {
    emit('filterChampion', championId)
    open.value = false
    return
  }
  go(pathFor(championId))
}

function onSelectTruemain(result: SearchResult) {
  if (!playersOnly.value) return go(truemainProfilePath(truemainNameTag(result.identity.gameName, result.identity.tagLine)))
  emit('selectTruemain', result)
  open.value = false
}

// `truemain` / `player` feed the row components; `iconUrl` the champion rows' picture.
type SearchItem = CommandPaletteItem & { truemain?: SearchResult, player?: SearchPlayer, iconUrl?: string | null }

// Known players (#2018) — the person rather than one of their accounts, so they
// come before the truemains. Shown only once a search has answered: their group
// has no hint or empty row of its own, the truemains group carries those.
const playerItems = computed<SearchItem[]>(() => (status.value === 'idle' || tooShort.value
  ? []
  : players.value.map(player => ({
      label: player.persona.displayName,
      slot: 'player',
      player,
      ...(status.value === 'pending' ? { disabled: true, class: 'opacity-50' } : { onSelect: () => go(playerPath(player.persona.slug)) }),
    }))))

// Sorted + mapped once, memoised: `groups` re-runs on every keystroke, and
// re-sorting ~170 names through localeCompare each time would be needless work.
const championItems = computed<SearchItem[]>(() =>
  [...(champions.value ?? [])]
    .sort((a, b) => a.name.localeCompare(b.name, 'en'))
    .map(champion => ({
      label: champion.name,
      slot: 'champion',
      iconUrl: champion.iconUrl,
      onSelect: () => onSelectChampion(champion.championId),
    })),
)

// `stale` (during a refetch) keeps the row visible but dimmed and
// non-selectable, so a pending list can't be clicked onto the wrong profile.
function toTruemainItem(result: SearchResult, stale = false): SearchItem {
  return {
    label: riotIdLabel(result.identity.gameName, result.identity.tagLine),
    slot: 'truemain',
    truemain: result,
    ...(stale ? { disabled: true, class: 'opacity-50' } : { onSelect: () => onSelectTruemain(result) }),
  }
}

const truemainState = computed(() => truemainSearchState({
  term: term.value,
  tooShort: tooShort.value,
  status: status.value,
  results: matches.value,
}))
const failedMessage = computed(() => describeFetchError(truemainSearchError.value))
const emptyMessage = computed(() => (props.mainsOf == null ? TRUEMAIN_SEARCH_MESSAGES.empty : TRUEMAIN_SEARCH_MESSAGES.emptyOnChampion))

// Every non-idle state yields at least one item: a hint/searching/error/
// no-match row, or the result rows.
function truemainItemsFor(state: Exclude<TruemainSearchState, { kind: 'idle' }>): SearchItem[] {
  switch (state.kind) {
    case 'tooShort': return [{ label: TRUEMAIN_SEARCH_MESSAGES.tooShort, icon: 'i-lucide-info', disabled: true }]
    case 'searching': return [{ label: TRUEMAIN_SEARCH_MESSAGES.searching, icon: 'i-lucide-loader-circle', disabled: true }]
    case 'error': return [{ label: failedMessage.value, icon: 'i-lucide-triangle-alert', disabled: true }]
    case 'empty': return [{ label: emptyMessage.value, icon: 'i-lucide-search-x', disabled: true }]
    case 'refetching': return state.results.map(result => toTruemainItem(result, true))
    case 'results': return state.results.map(result => toTruemainItem(result))
  }
}

const BROWSE: SearchItem[] = [
  { label: 'Champion tier list', icon: 'i-lucide-swords', onSelect: () => go('/champions') },
  { label: 'Truemains leaderboard', icon: 'i-lucide-trophy', onSelect: () => go('/truemains') },
]

const groups = computed<CommandPaletteGroup<SearchItem>[]>(() => {
  const state = truemainState.value
  const truemains = state.kind === 'idle'
    ? []
    : [{ id: 'truemains', label: 'Truemains', ignoreFilter: true, items: truemainItemsFor(state) }]
  if (playersOnly.value) return truemains

  // The reset row mirrors the region select's "All regions"; `ignoreFilter`
  // keeps it reachable while a term is typed, so a mistyped name is one step
  // from undone.
  const filter = props.championMode === 'filter' && props.activeChampionId != null
    ? [{ id: 'filter', label: 'Filter', ignoreFilter: true, items: [{ label: 'All champions', icon: 'i-lucide-x', onSelect: () => clearFilter() }] }]
    : []
  const knownPlayers = playerItems.value.length
    ? [{ id: 'players', label: 'Players', ignoreFilter: true, items: playerItems.value }]
    : []
  return [
    ...filter,
    { id: 'champions', label: 'Champions', items: championItems.value },
    ...knownPlayers,
    ...truemains,
    { id: 'browse', label: 'Browse', items: BROWSE },
  ]
})

function clearFilter() {
  emit('filterChampion', null)
  open.value = false
}

// UCommandPalette has no aria-live region of its own: without this the
// searching / no-match / error / results transitions are silent.
const announcement = computed(() => truemainSearchAnnouncement(truemainState.value, failedMessage.value, emptyMessage.value))

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})

// Registered unconditionally — composables must not be called inside an `if`.
// `false` leaves ⌘K to the header instance, the one that sets `shortcut`.
defineShortcuts(computed(() => ({
  meta_k: props.shortcut ? () => { open.value = !open.value } : false,
})))
</script>

<template>
  <div>
    <SearchTrigger
      :variant="variant"
      :size="size"
      :placeholder="placeholder"
      :label="label"
      :active-champion="activeChampion"
      @open="open = true"
      @clear="emit('filterChampion', null)"
    />

    <UModal
      v-model:open="open"
      title="Search"
      :description="playersOnly ? 'Search a truemain' : 'Search a champion or a player'"
      :ui="{ content: 'sm:max-w-xl' }"
    >
      <template #content>
        <div class="sr-only" role="status" aria-live="polite" aria-atomic="true">
          {{ announcement }}
        </div>
        <!-- Lazy on purpose: the header mounts this component on every page, so a
             static `UCommandPalette` reference would ship the palette chunk (its
             render logic + Fuse) into the site-wide initial JS (#832). The Lazy
             variant sits inside UModal's #content (only rendered when `open`),
             so the chunk loads on the first search open, not on page load.

             `ui.item` overrides the palette's own `items-start`, which would pin
             the truemain rows' avatar and trailing glyphs to their first line.
             Centering costs the single-line rows nothing. -->
        <LazyUCommandPalette
          v-model:search-term="term"
          :groups="groups"
          :placeholder="placeholder"
          :loading="status === 'pending'"
          icon="i-lucide-search"
          class="h-96"
          :close="{ onClick: () => { open = false } }"
          :ui="{ item: 'items-center' }"
        >
          <template #champion-leading="{ item }">
            <SearchItemAvatar :src="item.iconUrl" />
          </template>
          <template #player="{ item }">
            <SearchPlayerItem
              v-if="item.player"
              :player="item.player"
              :patch="latestPatch"
            />
          </template>
          <template #truemain="{ item }">
            <SearchTruemainItem
              v-if="item.truemain"
              :result="item.truemain"
              :patch="latestPatch"
              :champions-by-id="championsById"
            />
          </template>
        </LazyUCommandPalette>
      </template>
    </UModal>
  </div>
</template>
