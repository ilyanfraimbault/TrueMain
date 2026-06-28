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

const groups = computed<CommandPaletteGroup<SearchItem>[]>(() => {
  const list: CommandPaletteGroup<SearchItem>[] = [
    { id: 'champions', label: 'Champions', items: championItems.value },
  ]

  // Player group — shown with any query; an empty box shows champions + browse,
  // matching the former ChampionSearch. Each non-result state is a disabled,
  // non-selectable synthetic item (too-short hint / error / no match) so the
  // group never reads as a silent failure. While a newer query is in flight
  // (`pending`) the previous results stay visible but dimmed + non-selectable —
  // no flash on each keystroke, and a stale row still can't be clicked onto the
  // wrong profile; the input's loading spinner signals the refetch.
  const query = term.value.trim()
  if (query.length > 0) {
    let truemainItems: SearchItem[]
    if (tooShort.value) {
      truemainItems = [{ label: `Type at least ${SEARCH_MIN_LENGTH} characters to search truemains.`, icon: 'i-lucide-info', disabled: true }]
    }
    else if (status.value === 'error') {
      truemainItems = [{ label: 'Search failed — try again.', icon: 'i-lucide-alert-triangle', disabled: true }]
    }
    else if (status.value === 'pending') {
      truemainItems = results.value.map(result => toTruemainItem(result, true))
    }
    else if (results.value.length === 0) {
      truemainItems = [{ label: 'No truemain matches that name.', icon: 'i-lucide-search-x', disabled: true }]
    }
    else {
      truemainItems = results.value.map(result => toTruemainItem(result))
    }

    // Already filtered by the backend — keep Fuse out of it.
    if (truemainItems.length > 0) {
      list.push({ id: 'truemains', label: 'Truemains', ignoreFilter: true, items: truemainItems })
    }
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

// The ⌘K hint only belongs on a field that actually owns the shortcut
// (`shortcut`). Advertising it on a field that doesn't own ⌘K is a false
// promise: ⌘K toggles the header instance's own modal, not this one, and
// pressing it while this modal is open would stack a second one. (No field sets
// `shortcut` today, so the hint stays hidden — ⌘K still works via the header.)
const showKbd = computed(() => props.shortcut && props.variant === 'field')

// Screen-reader announcement for the async truemain search: UCommandPalette
// emits no aria-live region of its own, so without this the loading / no-match
// / error / results transitions are silent (the former TruemainSearch wrapped
// them in an aria-live="polite" block). Champions filter locally and need no
// announcement.
const truemainAnnouncement = computed(() => {
  if (term.value.trim().length === 0) return ''
  if (tooShort.value) return `Type at least ${SEARCH_MIN_LENGTH} characters to search truemains.`
  if (status.value === 'pending') return 'Searching truemains…'
  if (status.value === 'error') return 'Truemain search failed. Try again.'
  if (status.value === 'success') {
    const n = results.value.length
    return n === 0 ? 'No truemain matches that name.' : `${n} truemain${n > 1 ? 's' : ''} found.`
  }
  return ''
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
      <span v-if="showKbd" class="hidden items-center gap-0.5 sm:flex">
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
