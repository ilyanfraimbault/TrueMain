<script setup lang="ts">
import type { LeaderboardRowResponse, RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { formatTier, isApexTier } from '~/utils/tiers'

// Homepage teaser of the truemains leaderboard: top rows with a region
// switch, linking through to /truemains. The page owns the fetch (the region
// pills just emit) so the leaderboard composable's SSR behaviour stays in
// one place.
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  /** championId → static name/icon, for rendering each player's top picks. */
  championsById: Map<number, ChampionStaticListItem>
  /** Static rune tree + item map, to draw each main champion's keystone + first item. */
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  /** Skeleton state — true until the very first page resolves. */
  initialLoading: boolean
  /** Refetch-in-flight state — dims the current rows during a region switch. */
  loading: boolean
  region: RegionSlug | null
  patch: string | null
}>()

const emit = defineEmits<{
  'update:region': [value: RegionSlug | null]
}>()

const ROW_COUNT = 5

// `label` doubles as the accessible name + tooltip; the flag SVG carries the
// visual identity, and the `null` "all regions" tab renders a globe glyph.
const REGION_TABS: Array<{ label: string, value: RegionSlug | null }> = [
  { label: 'All regions', value: null },
  { label: 'Europe', value: 'europe' },
  { label: 'Americas', value: 'americas' },
  { label: 'Korea', value: 'korea' },
]

function profileHref(row: LeaderboardRowResponse): string {
  const { gameName, tagLine } = row.identity
  return `/truemains/${encodeURIComponent(tagLine ? `${gameName}-${tagLine}` : gameName)}`
}

function winRateLabel(row: LeaderboardRowResponse): string | null {
  const wr = row.stats.winRate
  return wr === null ? null : `${Math.round(wr * 100)}%`
}

function iconUrl(row: LeaderboardRowResponse): string | null {
  return getProfileIconUrl(row.identity.profileIconId, props.patch)
}

function championName(id: number): string {
  return props.championsById.get(id)?.name ?? `#${id}`
}
function championIcon(id: number): string | null {
  return props.championsById.get(id)?.iconUrl ?? null
}

function perk(id: number | null | undefined) {
  return id ? props.runeTree?.perks?.[id] ?? null : null
}
function perkStyle(id: number | null | undefined) {
  return id ? props.runeTree?.perkStyles?.[id] ?? null : null
}
function buildItem(id: number | null | undefined) {
  return id ? props.itemsMap?.[id] ?? null : null
}
</script>

<template>
  <section
    class="relative flex flex-col overflow-hidden rounded-2xl border border-default/60 bg-elevated/30 p-4 backdrop-blur-sm sm:p-5"
    aria-labelledby="home-truemains-title"
  >
    <!-- Subtle emerald sheen so the panel reads as a lit surface, not a flat
         grey slab. -->
    <div
      aria-hidden="true"
      class="pointer-events-none absolute inset-x-0 top-0 h-32"
      style="background: radial-gradient(80% 100% at 50% 0%, color-mix(in oklch, var(--ui-color-primary-500) 8%, transparent), transparent 70%);"
    />

    <header class="relative flex flex-wrap items-center justify-between gap-3 pb-3">
      <h2
        id="home-truemains-title"
        class="text-sm font-semibold uppercase tracking-[0.12em] text-default"
      >
        Top truemains
      </h2>

      <div
        class="flex items-center gap-1"
        role="group"
        aria-label="Filter leaderboard by region"
      >
        <button
          v-for="tab in REGION_TABS"
          :key="tab.label"
          type="button"
          :aria-label="tab.label"
          :aria-pressed="region === tab.value"
          :title="tab.label"
          class="inline-flex h-7 w-9 items-center justify-center rounded-md ring-1 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          :class="region === tab.value
            ? 'bg-primary/10 ring-primary/50'
            : 'opacity-55 ring-transparent hover:bg-elevated hover:opacity-100'"
          @click="emit('update:region', tab.value)"
        >
          <LeaderboardRegionFlag
            v-if="tab.value"
            :region="tab.value"
            :width="22"
          />
          <UIcon
            v-else
            name="i-lucide-globe"
            class="size-[18px]"
          />
        </button>
      </div>
    </header>

    <div
      v-if="initialLoading"
      class="relative space-y-1"
      aria-hidden="true"
    >
      <div
        v-for="i in ROW_COUNT"
        :key="i"
        class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2"
      >
        <USkeleton class="size-9 rounded-lg" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-16" />
      </div>
    </div>

    <ul
      v-else-if="rows.length > 0"
      class="relative flex flex-1 flex-col justify-evenly gap-1 transition-opacity"
      :class="loading ? 'opacity-50' : 'opacity-100'"
    >
      <li
        v-for="row in rows"
        :key="`${row.identity.gameName}-${row.identity.tagLine}`"
      >
        <!-- `-mx-2 px-2`: hover background bleeds into the panel padding while
             the rank stays flush with the section header (no row indent). -->
        <NuxtLink
          :to="profileHref(row)"
          class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2 transition-colors hover:bg-elevated/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <span
            class="w-4 shrink-0 text-center font-mono text-xs tabular-nums"
            :class="row.rank <= 3 ? 'text-primary' : 'text-dimmed'"
          >
            {{ row.rank }}
          </span>

          <SkeletonImage
            v-if="iconUrl(row)"
            :src="iconUrl(row)!"
            :alt="row.identity.gameName"
            width="36"
            height="36"
            class="size-9 shrink-0 rounded-lg"
          />
          <div
            v-else
            class="size-9 shrink-0 rounded-lg bg-elevated/60"
            aria-hidden="true"
          />

          <div class="min-w-0 flex-1">
            <div class="flex items-baseline gap-1 truncate">
              <span class="truncate text-sm font-semibold">{{ row.identity.gameName }}</span>
              <span
                v-if="row.identity.tagLine"
                class="shrink-0 text-xs text-muted"
              >#{{ row.identity.tagLine }}</span>
            </div>
            <LeaderboardRegionFlag
              :region="row.region"
              :width="16"
              class="mt-0.5"
            />
          </div>

          <!-- Main champion: icon + play rate + keystone + first item. Plain
               (non-link) icon — the whole row already navigates to the
               profile. Hidden on the narrowest widths. -->
          <LeaderboardChampionBuild
            v-if="row.topChampions[0]"
            compact
            class="hidden shrink-0 sm:flex"
            :champion="row.topChampions[0]"
            :name="championName(row.topChampions[0].championId)"
            :icon-url="championIcon(row.topChampions[0].championId)"
            :keystone="perk(row.topChampions[0].primaryKeystoneId)"
            :secondary-style="perkStyle(row.topChampions[0].secondaryStyleId)"
            :first-item="buildItem(row.topChampions[0].firstItemId)"
          />

          <div
            v-if="row.ranked"
            class="flex shrink-0 items-center gap-1.5"
            :title="formatTier(row.ranked.tier, row.ranked.division)"
          >
            <RankIcon
              :tier="row.ranked.tier"
              :size="26"
            />
            <!-- Default text colour on purpose: the tier palette in
                 utils/tiers.ts is tuned for dark surfaces and washes out in
                 light mode — the crest already carries the tier identity. -->
            <span class="text-sm font-semibold tabular-nums">
              {{ isApexTier(row.ranked.tier)
                ? `${row.ranked.leaguePoints.toLocaleString('en-US')} LP`
                : row.ranked.division }}
            </span>
          </div>

          <span
            v-if="winRateLabel(row)"
            class="w-10 shrink-0 text-right text-sm font-semibold tabular-nums"
          >
            {{ winRateLabel(row) }}
            <span class="block text-[10px] font-normal uppercase tracking-wide text-muted">WR</span>
          </span>
        </NuxtLink>
      </li>
    </ul>

    <p
      v-else
      class="relative px-3 py-8 text-center text-sm text-muted"
    >
      No ranked truemains for this region yet.
    </p>

    <footer class="relative mt-auto flex justify-end pt-2">
      <UButton
        to="/truemains"
        color="neutral"
        variant="ghost"
        size="sm"
        trailing-icon="i-lucide-arrow-right"
        label="Full leaderboard"
      />
    </footer>
  </section>
</template>
