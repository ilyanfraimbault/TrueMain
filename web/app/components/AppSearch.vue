<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { isNavigationFailure } from 'vue-router'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import type { SearchResult } from '~~/shared/types/search'
import { formatTier } from '~/utils/tiers'
// Explicit (over Nuxt auto-import) so the template's {{ SEARCH_MIN_LENGTH }} has
// a visible source.
import { SEARCH_MIN_LENGTH } from '~/composables/useTruemainSearch'

// Unified search: one command palette over both the champion roster (filtered
// locally by Fuse) and the truemain database (server search, debounced). A
// trigger (a search-field look in a page hero, a compact icon in the header)
// opens a modal holding the palette and the results list. `useTruemainSearch`
// is instantiated in setup — so it runs on every page, since the header mounts
// this component — but stays inert while the modal is closed: `term` is `''`, so
// its watcher returns early without firing a request or a debounce timer.
// Replaces the former split between ChampionSearch and TruemainSearch.
const props = withDefaults(defineProps<{
  /** `field` = full search-bar trigger (page hero); `button` = compact icon (header). */
  variant?: 'field' | 'button'
  /**
   * What selecting a champion does:
   * - `navigate` (default): go to `/champions/{id}`.
   * - `filter`: emit `filterChampion` and stay put (used on the leaderboard,
   *   where a champion narrows the list instead of leaving the page).
   */
  championMode?: 'navigate' | 'filter'
  placeholder?: string
  /**
   * Register the ⌘K shortcut on this instance. Set only on the header instance
   * (mounted on every page) so the shortcut isn't bound twice on pages that
   * also render a field instance (home, leaderboard).
   */
  shortcut?: boolean
  /**
   * Field trigger size: `md` (default, e.g. the leaderboard) or `lg` for the
   * homepage hero — taller, more rounded, larger text + a soft shadow. Ignored
   * for the `button` variant.
   */
  size?: 'md' | 'lg'
}>(), {
  variant: 'field',
  championMode: 'navigate',
  placeholder: 'Search a champion or player…',
  shortcut: false,
  size: 'md',
})

// Set-only by design: `filterChampion` always carries a champion id, never
// null. In filter mode the search applies a champion to the leaderboard;
// clearing back to "all champions" stays the ChampionPicker's job in
// LeaderboardFilters (intentionally kept on /truemains). Don't wire a "clear"
// here without revisiting that split.
const emit = defineEmits<{
  filterChampion: [championId: number]
}>()

const open = ref(false)
const term = ref('')
const router = useRouter()

// Champions — shared static list, filtered locally by the palette's Fuse.
const { data: champions } = useChampionStaticList()

// Truemains — debounced server search, reusing the existing composable.
const { results, status, tooShort } = useTruemainSearch(term)

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

// Carries the full result through to the trailing slot (rank + region) on top
// of the standard label/suffix/avatar fields.
type SearchItem = CommandPaletteItem & { truemain?: SearchResult }

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
  go(`/champions/${championId}`)
}

// `{gameName}-{tagLine}` (or just the name when untagged) — the same slug the
// leaderboard rows use to reach a profile.
function profilePath(result: SearchResult): string {
  const { gameName, tagLine } = result.identity
  const slug = tagLine ? `${gameName}-${tagLine}` : gameName
  return `/truemains/${encodeURIComponent(slug)}`
}

// Sorted + mapped once, memoised: `groups` re-runs on every keystroke (the
// truemains branch reads `term`), and re-sorting ~160 names through
// localeCompare each time would be needless work. This only recomputes when the
// champion list itself changes (i.e. once, after it loads).
const championItems = computed<SearchItem[]>(() =>
  [...(champions.value ?? [])]
    .sort((a, b) => a.name.localeCompare(b.name, 'en'))
    .map(champion => ({
      label: champion.name,
      avatar: { src: champion.iconUrl, alt: champion.name },
      onSelect: () => onSelectChampion(champion.championId),
    })),
)

// Map a truemain result to a palette item. `stale` (used during a refetch)
// keeps the row visible but dimmed and non-selectable, so a pending list can't
// be clicked onto the wrong profile.
function toTruemainItem(result: SearchResult, stale = false): SearchItem {
  const item: SearchItem = {
    label: result.identity.gameName,
    suffix: result.identity.tagLine ? `#${result.identity.tagLine}` : undefined,
    avatar: { src: getProfileIconUrl(result.identity.profileIconId, latestPatch.value) ?? undefined, alt: result.identity.gameName },
    slot: 'truemain',
    truemain: result,
  }
  if (stale) {
    item.disabled = true
    item.class = 'opacity-50'
  }
  else {
    item.onSelect = () => go(profilePath(result))
  }
  return item
}

// Truemain-search state messages, shared between the palette's disabled items
// and the aria-live announcement so the two can't drift apart.
const TM_TOO_SHORT = `Type at least ${SEARCH_MIN_LENGTH} characters to search truemains.`
const TM_SEARCHING = 'Searching truemains…'
const TM_FAILED = 'Search failed — try again.'
const TM_NO_MATCH = 'No truemain matches that name.'

// One classification of the truemain-search state, derived once and consumed by
// both the palette group and the aria-live announcement — so a new state
// (rate-limit, timeout, …) is added in one place and the screen-reader text
// can't diverge from what's shown.
type TruemainState =
  | { kind: 'idle' }
  | { kind: 'tooShort' }
  | { kind: 'searching' }
  | { kind: 'error' }
  | { kind: 'empty' }
  | { kind: 'refetching', results: SearchResult[] }
  | { kind: 'results', results: SearchResult[] }

const truemainState = computed<TruemainState>(() => {
  if (term.value.trim().length === 0) return { kind: 'idle' }
  if (tooShort.value) return { kind: 'tooShort' }
  if (status.value === 'error') return { kind: 'error' }
  if (status.value === 'pending') {
    return results.value.length > 0
      ? { kind: 'refetching', results: results.value }
      : { kind: 'searching' }
  }
  if (results.value.length === 0) return { kind: 'empty' }
  return { kind: 'results', results: results.value }
})

// Palette items for the player group. Every non-idle state yields at least one
// item (a synthetic hint/searching/error/no-match row, or the result rows —
// dimmed + non-selectable during a refetch so a stale row can't be clicked onto
// the wrong profile and there's no flash on each keystroke).
function truemainItemsFor(state: Exclude<TruemainState, { kind: 'idle' }>): SearchItem[] {
  switch (state.kind) {
    case 'tooShort': return [{ label: TM_TOO_SHORT, icon: 'i-lucide-info', disabled: true }]
    case 'searching': return [{ label: TM_SEARCHING, icon: 'i-lucide-loader-circle', disabled: true }]
    case 'error': return [{ label: TM_FAILED, icon: 'i-lucide-alert-triangle', disabled: true }]
    case 'empty': return [{ label: TM_NO_MATCH, icon: 'i-lucide-search-x', disabled: true }]
    case 'refetching': return state.results.map(result => toTruemainItem(result, true))
    case 'results': return state.results.map(result => toTruemainItem(result))
  }
}

const groups = computed<CommandPaletteGroup<SearchItem>[]>(() => {
  const list: CommandPaletteGroup<SearchItem>[] = [
    { id: 'champions', label: 'Champions', items: championItems.value },
  ]

  // Player group — shown with any query; an empty box shows champions + browse,
  // matching the former ChampionSearch. Every non-idle state yields ≥1 item, so
  // it's pushed unconditionally (never a silent empty group).
  const state = truemainState.value
  if (state.kind !== 'idle') {
    list.push({ id: 'truemains', label: 'Truemains', ignoreFilter: true, items: truemainItemsFor(state) })
  }

  list.push({
    id: 'browse',
    label: 'Browse',
    items: [
      {
        label: 'Champion tier list',
        icon: 'i-lucide-swords',
        onSelect: () => go('/champions'),
      },
      {
        label: 'Truemains leaderboard',
        icon: 'i-lucide-trophy',
        onSelect: () => go('/truemains'),
      },
    ],
  })

  return list
})

// Screen-reader announcement for the async truemain search: UCommandPalette
// emits no aria-live region of its own, so without this the searching / no-match
// / error / results transitions are silent (the former TruemainSearch wrapped
// them in an aria-live="polite" block). Derives from the same `truemainState`
// as the palette group, so the announcement can't diverge from what's shown.
const truemainAnnouncement = computed(() => {
  const state = truemainState.value
  switch (state.kind) {
    case 'idle': return ''
    case 'tooShort': return TM_TOO_SHORT
    case 'searching':
    case 'refetching': return TM_SEARCHING
    case 'error': return TM_FAILED
    case 'empty': return TM_NO_MATCH
    case 'results': return `${state.results.length} truemain${state.results.length > 1 ? 's' : ''} found.`
  }
})

// Field trigger sizing: `lg` restores the homepage hero's larger, more rounded,
// shadowed look (matching the former ChampionSearch); `md` is the compact
// default used elsewhere (e.g. the leaderboard).
const fieldSizeClass = computed(() => props.size === 'lg'
  ? 'h-14 rounded-2xl px-5 text-base shadow-lg shadow-black/5 dark:shadow-black/20'
  : 'h-12 rounded-xl px-4 text-sm')

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})

// Registered unconditionally — composables must not be called inside an `if`.
// `false` disables the binding on field instances, so ⌘K is owned only by the
// header instance (which sets `shortcut`); the computed keeps it reactive if the
// prop ever becomes dynamic.
defineShortcuts(computed(() => ({
  meta_k: props.shortcut ? () => { open.value = !open.value } : false,
})))
</script>

<template>
  <div>
    <!-- Field trigger: looks like a search bar (page hero, leaderboard). -->
    <button
      v-if="props.variant === 'field'"
      type="button"
      class="group flex w-full items-center gap-3 border border-default bg-default/60 text-left backdrop-blur-md transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      :class="fieldSizeClass"
      aria-label="Search a champion or player"
      @click="open = true"
    >
      <UIcon
        name="i-lucide-search"
        class="size-5 shrink-0 text-dimmed transition-colors group-hover:text-primary"
      />
      <span class="flex-1 truncate text-dimmed">
        {{ props.placeholder }}
      </span>
      <!-- ⌘K hint on the prominent hero bar only. The shortcut itself is owned
           by the always-mounted header instance; pressing ⌘K opens the
           (identical) unified search, and `usingInput` blocks it while a modal
           input is focused, so it can't stack a second modal. -->
      <span v-if="props.size === 'lg'" class="hidden items-center gap-0.5 sm:flex">
        <UKbd value="meta" />
        <UKbd value="K" />
      </span>
    </button>

    <!-- Compact trigger: icon button (header). -->
    <UButton
      v-else
      icon="i-lucide-search"
      color="neutral"
      variant="ghost"
      aria-label="Search a champion or player"
      @click="open = true"
    />

    <UModal
      v-model:open="open"
      title="Search"
      description="Search a champion or a player"
      :ui="{ content: 'sm:max-w-xl' }"
    >
      <template #content>
        <!-- Announces the async truemain search state — UCommandPalette has no
             live region of its own. -->
        <div class="sr-only" role="status" aria-live="polite" aria-atomic="true">
          {{ truemainAnnouncement }}
        </div>
        <UCommandPalette
          v-model:search-term="term"
          :groups="groups"
          :placeholder="props.placeholder"
          :loading="status === 'pending'"
          icon="i-lucide-search"
          class="h-96"
          :close="{ onClick: () => { open = false } }"
        >
          <template #truemain-trailing="{ item }">
            <div class="flex items-center gap-2">
              <LeaderboardRegionFlag
                v-if="item.truemain?.region"
                :region="item.truemain.region"
                :width="16"
              />
              <div
                v-if="item.truemain?.ranked"
                class="flex items-center gap-1.5 text-sm text-muted"
              >
                <RankIcon :tier="item.truemain.ranked.tier" :size="20" />
                <span class="tabular-nums">{{ formatTier(item.truemain.ranked.tier, item.truemain.ranked.division) }}</span>
              </div>
            </div>
          </template>
        </UCommandPalette>
      </template>
    </UModal>
  </div>
</template>
