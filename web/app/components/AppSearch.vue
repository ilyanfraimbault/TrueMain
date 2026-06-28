<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { isNavigationFailure } from 'vue-router'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import type { SearchResult } from '~~/shared/types/search'
import { formatTier } from '~/utils/tiers'

// Unified search: one command palette over both the champion roster (filtered
// locally by Fuse) and the truemain database (server search, debounced). A
// trigger (a search-field look in a page hero, a compact icon in the header)
// opens a modal; the heavy part — the palette, the live player query — lives
// inside the modal so it never participates in SSR. Replaces the former split
// between ChampionSearch (champions) and TruemainSearch (players).
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
}>(), {
  variant: 'field',
  championMode: 'navigate',
  placeholder: 'Search a champion or player…',
  shortcut: false,
})

const emit = defineEmits<{
  filterChampion: [championId: number]
}>()

const open = ref(false)
const term = ref('')
const router = useRouter()

// Champions — shared static list, filtered locally by the palette's Fuse.
const { data: champions } = useChampionStaticList()

// Truemains — debounced server search, reusing the existing composable.
const { results, status } = useTruemainSearch(term)

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

const groups = computed<CommandPaletteGroup<SearchItem>[]>(() => {
  const list: CommandPaletteGroup<SearchItem>[] = [
    {
      id: 'champions',
      label: 'Champions',
      items: [...(champions.value ?? [])]
        .sort((a, b) => a.name.localeCompare(b.name, 'en'))
        .map(champion => ({
          label: champion.name,
          avatar: { src: champion.iconUrl, alt: champion.name },
          onSelect: () => onSelectChampion(champion.championId),
        })),
    },
  ]

  // Player group only once there's a query — an empty box shows champions +
  // browse, matching the former ChampionSearch's open state.
  if (term.value.trim().length > 0) {
    list.push({
      id: 'truemains',
      label: 'Truemains',
      // Already filtered by the backend — keep Fuse out of it.
      ignoreFilter: true,
      items: results.value.map(result => ({
        label: result.identity.gameName,
        suffix: result.identity.tagLine ? `#${result.identity.tagLine}` : undefined,
        avatar: { src: getProfileIconUrl(result.identity.profileIconId, latestPatch.value) ?? undefined, alt: result.identity.gameName },
        slot: 'truemain',
        truemain: result,
        onSelect: () => go(profilePath(result)),
      })),
    })
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

// Show the ⌘K hint on the field only when ⌘K does the same thing this field
// does (navigate). On the leaderboard field (filter mode) ⌘K opens the header's
// navigate-mode search, so advertising it here would be misleading.
const showKbd = computed(() => props.variant === 'field' && props.championMode === 'navigate')

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})

if (props.shortcut) {
  defineShortcuts({
    meta_k: () => {
      open.value = !open.value
    },
  })
}
</script>

<template>
  <div>
    <!-- Field trigger: looks like a search bar (page hero, leaderboard). -->
    <button
      v-if="props.variant === 'field'"
      type="button"
      class="group flex h-12 w-full items-center gap-3 rounded-xl border border-default bg-default/60 px-4 text-left backdrop-blur-md transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      aria-label="Search a champion or player"
      @click="open = true"
    >
      <UIcon
        name="i-lucide-search"
        class="size-5 shrink-0 text-dimmed transition-colors group-hover:text-primary"
      />
      <span class="flex-1 truncate text-sm text-dimmed">
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
