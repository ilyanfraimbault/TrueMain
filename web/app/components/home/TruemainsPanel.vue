<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { isApexTier } from '~/utils/tiers'

// Homepage teaser of the truemains leaderboard: the global top rows, linking
// through to /truemains. The page owns the fetch so the leaderboard
// composable's SSR behaviour stays in one place.
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  /** championId → static name/icon, for rendering each player's top picks. */
  championsById: Map<number, ChampionStaticListItem>
  /** Skeleton state — true until the very first page resolves. */
  initialLoading: boolean
  patch: string | null
}>()

// ─── Build-icon assets (client-only enrichment) ───────────────────────────
// Each main champion's keystone + first item needs the static rune tree and
// item map. The rune tree is tiny, but the item map is ~373 KiB — dead weight
// on the homepage's initial payload for a panel that sits below the fold. Gate
// the *item-map* fetch behind visibility: the fetch key stays `null` (so the
// `immediate: false` source never fires) until `useVisibleOnce` flips
// `buildAssetsVisible`, at which point the resolved patch drives the fetch.
//
// Mismatch-safe: at SSR and at the initial client hydration the map is still
// empty (identical to today, where it resolved post-hydration), so the build
// icons render null on both sides and only fill in later as a reactive update —
// never a hydration reconciliation.
const rootEl = ref<HTMLElement | null>(null)
const buildAssetsVisible = useVisibleOnce(rootEl)
const { data: runeTreeData } = useStaticRuneTree(() => props.patch)
const { data: itemsData } = useStaticItems(
  () => (buildAssetsVisible.value ? props.patch : null),
  { immediate: false },
)
const runeTree = computed(() => runeTreeData.value ?? null)
const itemsMap = computed(() => itemsData.value ?? {})

const ROW_COUNT = TRUEMAINS_TEASER_ROWS

function profileHref(row: LeaderboardRowResponse): string {
  const { gameName, tagLine } = row.identity
  return `/truemains/${encodeURIComponent(tagLine ? `${gameName}-${tagLine}` : gameName)}`
}

function winRateLabel(row: LeaderboardRowResponse): string | null {
  const wr = row.stats.winRate
  return wr === null ? null : formatPercentage(wr, 0)
}

function iconUrl(row: LeaderboardRowResponse): string | null {
  return getProfileIconUrl(row.identity.profileIconId, props.patch)
}

// Precompute the per-row derived values so the template doesn't evaluate the
// same helper twice (once in a `v-if`, once for display).
const displayRows = computed(() => props.rows.map(row => ({
  row,
  href: profileHref(row),
  iconUrl: iconUrl(row),
  winRateLabel: winRateLabel(row),
})))

function championName(id: number): string {
  return props.championsById.get(id)?.name ?? `#${id}`
}
function championIcon(id: number): string | null {
  return props.championsById.get(id)?.iconUrl ?? null
}

// Shared with the leaderboard row — resolve build ids the same way the
// fetching composable does.
const { perk, perkStyle, item: buildItem } = useBuildResolvers(runeTree, itemsMap)
</script>

<template>
  <section
    ref="rootEl"
    class="surface flex flex-col rounded-2xl p-3 sm:p-4"
    aria-labelledby="home-truemains-title"
  >
    <header class="pb-2">
      <h2
        id="home-truemains-title"
        class="text-sm font-semibold text-default"
      >
        Top truemains
      </h2>
    </header>

    <div
      v-if="initialLoading"
      class="space-y-0.5"
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
      class="space-y-0.5"
    >
      <li
        v-for="{ row, href, iconUrl, winRateLabel } in displayRows"
        :key="`${row.identity.gameName}-${row.identity.tagLine}`"
      >
        <!-- `-mx-2 px-2`: hover background bleeds into the panel padding while
             the icon stays flush with the section header (no row indent). -->
        <NuxtLink
          :to="href"
          class="surface-hover -mx-2 flex items-center gap-3 rounded-lg px-2 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <SkeletonImage
            v-if="iconUrl"
            :src="iconUrl"
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

          <!-- Rank emblem, same treatment as the full leaderboard row: crest +
               division only (no LP), full rank + LP + win-loss on hover. -->
          <UTooltip
            v-if="row.ranked"
            :delay-duration="150"
            :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
          >
            <div class="flex w-12 shrink-0 items-center justify-end gap-1">
              <RankIcon
                :tier="row.ranked.tier"
                :size="26"
              />
              <span
                v-if="!isApexTier(row.ranked.tier)"
                class="text-sm font-semibold tabular-nums"
              >
                {{ row.ranked.division }}
              </span>
            </div>

            <template #content>
              <GameTooltipSurface>
                <RankSummary
                  :tier="row.ranked.tier"
                  :division="row.ranked.division"
                  :league-points="row.ranked.leaguePoints"
                  :wins="row.stats.wins"
                  :losses="row.stats.losses"
                  :win-rate="row.stats.winRate"
                  :size="32"
                />
              </GameTooltipSurface>
            </template>
          </UTooltip>

          <span
            v-if="winRateLabel"
            class="w-10 shrink-0 text-right text-sm font-semibold tabular-nums"
          >
            {{ winRateLabel }}
            <span class="block text-[10px] font-normal uppercase tracking-wide text-muted">WR</span>
          </span>
        </NuxtLink>
      </li>
    </ul>

    <UEmpty
      v-else
      size="sm"
      icon="i-lucide-trophy"
      description="No ranked truemains yet."
    />

    <footer class="mt-auto flex justify-end pt-2">
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
