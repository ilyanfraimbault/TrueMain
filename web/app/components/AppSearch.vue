<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { isNavigationFailure } from 'vue-router'
import { getPositionIconUrl, getProfileIconUrl } from '~~/shared/utils/ddragon'
import type { SearchResult } from '~~/shared/types/search'
import { POSITION_BY_VALUE } from '~/utils/positions'
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
  /**
   * Currently-applied champion filter id, or `null` for "all champions". Only
   * meaningful in `filter` mode: when set, the palette surfaces an "All
   * champions" reset row so the filter can be cleared from here (there's no
   * ChampionPicker on the leaderboard anymore).
   */
  activeChampionId?: number | null
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
  activeChampionId: null,
  placeholder: 'Search a champion or player…',
  shortcut: false,
  size: 'md',
})

// In filter mode the search both applies a champion to the leaderboard (a
// champion id) and clears the filter back to "all champions" (`null`) via the
// reset row surfaced when `activeChampionId` is set — the leaderboard has no
// ChampionPicker to own the clear anymore.
const emit = defineEmits<{
  filterChampion: [championId: number | null]
}>()

const open = ref(false)
const term = ref('')
const router = useRouter()

// Champions — shared static list, filtered locally by the palette's Fuse.
const { data: champions } = useChampionStaticList()

// The applied champion filter, resolved to its static entry so the field
// trigger can show *which* champion is filtering the leaderboard (icon +
// name) instead of the generic placeholder — otherwise nothing on the page
// says a filter is active.
const activeChampion = computed(() => {
  if (props.championMode !== 'filter' || props.activeChampionId == null) return null
  return champions.value?.find(c => c.championId === props.activeChampionId) ?? null
})

// Truemains — debounced server search, reusing the existing composable.
const { results, status, tooShort } = useTruemainSearch(term)

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

// championId → static entry, for the truemained-champion icons on each result
// row. Shares the `champion-static-list` cache with the palette's champion
// group, so this costs no extra request.
const championsById = useChampionsById(champions)

// Carries the full result through to the row's label/trailing slots (region
// under the name, lanes + mains + rank on the right) on top of the standard
// label/suffix/avatar fields.
type SearchItem = CommandPaletteItem & { truemain?: SearchResult }

// Primary / secondary lane icons for a result row — same visual contract as
// the leaderboard row (primary brighter than secondary, label tooltips from
// the canonical POSITION_BY_VALUE map).
function positionIconsFor(result: SearchResult | undefined) {
  const positions = result?.positions
  if (!positions) return []
  const label = (position: string) => POSITION_BY_VALUE.get(position)?.label ?? position
  const icons = [{
    position: positions.primary,
    iconUrl: getPositionIconUrl(positions.primary),
    title: `Primary: ${label(positions.primary)}`,
    primary: true,
  }]
  if (positions.secondary) {
    icons.push({
      position: positions.secondary,
      iconUrl: getPositionIconUrl(positions.secondary),
      title: `Secondary: ${label(positions.secondary)}`,
      primary: false,
    })
  }
  return icons
}

// Truemained-champion icons for a result row. Entries whose static lookup
// hasn't resolved yet (list still loading) are dropped rather than rendered
// as broken images.
function championIconsFor(result: SearchResult | undefined) {
  if (!result) return []
  return result.topChampionIds
    .map((id) => {
      const champion = championsById.value.get(id)
      return champion ? { id, name: champion.name, iconUrl: champion.iconUrl } : null
    })
    .filter(champion => champion !== null)
}

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

// Reset row — only in filter mode with a champion applied. Emits `null` to
// clear the leaderboard's champion filter, mirroring the region select's "All
// regions" entry. `ignoreFilter` keeps it reachable even while a search term is
// typed, so a mistyped name can still be undone in one step.
const clearFilterItem = computed<SearchItem | null>(() => {
  if (props.championMode !== 'filter' || props.activeChampionId == null) return null
  return {
    label: 'All champions',
    icon: 'i-lucide-x',
    onSelect: () => {
      emit('filterChampion', null)
      open.value = false
    },
  }
})

const groups = computed<CommandPaletteGroup<SearchItem>[]>(() => {
  const list: CommandPaletteGroup<SearchItem>[] = []

  if (clearFilterItem.value) {
    list.push({ id: 'filter', label: 'Filter', ignoreFilter: true, items: [clearFilterItem.value] })
  }

  list.push({ id: 'champions', label: 'Champions', items: championItems.value })

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
    <!-- Field trigger: looks like a search bar (page hero, leaderboard). When
         a champion filter is applied (filter mode), the trigger shows that
         champion (icon + name) with a primary-tinted border instead of the
         placeholder, and a clear button sits on the right — a sibling, not a
         nested button, which would be invalid HTML. -->
    <div v-if="props.variant === 'field'" class="relative">
      <button
        type="button"
        class="group flex w-full items-center gap-3 border bg-default/60 text-left backdrop-blur-md transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        :class="[fieldSizeClass, activeChampion ? 'border-primary/50 pr-12' : 'border-default']"
        :aria-label="activeChampion ? `Filtering by ${activeChampion.name} — search a champion or player` : 'Search a champion or player'"
        @click="open = true"
      >
        <UIcon
          name="i-lucide-search"
          class="size-5 shrink-0 text-dimmed transition-colors group-hover:text-primary"
        />
        <span v-if="activeChampion" class="flex min-w-0 flex-1 items-center gap-2">
          <UAvatar :src="activeChampion.iconUrl" :alt="''" size="2xs" />
          <span class="truncate font-medium text-highlighted">{{ activeChampion.name }}</span>
        </span>
        <span v-else class="flex-1 truncate text-dimmed">
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
      <UButton
        v-if="activeChampion"
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        size="xs"
        class="absolute right-2 top-1/2 -translate-y-1/2"
        :aria-label="`Clear champion filter (${activeChampion.name})`"
        @click="emit('filterChampion', null)"
      />
    </div>

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
          <!-- Result row: name#tag with the region flag underneath on the
               left; lanes, truemained champions and the rank emblem (icon
               only — the tier name stays in the tooltip, never spelled out)
               on the right. -->
          <template #truemain-label="{ item }">
            <div class="flex min-w-0 flex-col items-start gap-0.5">
              <span class="truncate">
                {{ item.truemain?.identity.gameName ?? item.label }}<span
                  v-if="item.truemain?.identity.tagLine"
                  class="text-dimmed"
                > #{{ item.truemain.identity.tagLine }}</span>
              </span>
              <LeaderboardRegionFlag
                v-if="item.truemain?.region"
                :region="item.truemain.region"
                :width="16"
              />
            </div>
          </template>
          <template #truemain-trailing="{ item }">
            <div class="flex items-center gap-3">
              <div v-if="positionIconsFor(item.truemain).length > 0" class="flex items-center gap-1">
                <img
                  v-for="role in positionIconsFor(item.truemain)"
                  :key="role.position"
                  :src="role.iconUrl"
                  :alt="role.title"
                  :title="role.title"
                  class="size-4 shrink-0"
                  :class="role.primary ? 'opacity-70' : 'opacity-40'"
                  width="16"
                  height="16"
                >
              </div>
              <div v-if="championIconsFor(item.truemain).length > 0" class="flex items-center gap-1">
                <img
                  v-for="champion in championIconsFor(item.truemain)"
                  :key="champion.id"
                  :src="champion.iconUrl"
                  :alt="champion.name"
                  :title="champion.name"
                  class="size-5 shrink-0 rounded"
                  width="20"
                  height="20"
                >
              </div>
              <span
                v-if="item.truemain?.ranked"
                :title="formatTier(item.truemain.ranked.tier, item.truemain.ranked.division)"
                class="shrink-0"
              >
                <RankIcon :tier="item.truemain.ranked.tier" :size="22" />
              </span>
            </div>
          </template>
        </UCommandPalette>
      </template>
    </UModal>
  </div>
</template>
