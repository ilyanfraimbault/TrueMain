<script setup lang="ts">
import type { RegionSlug } from '#shared/types/leaderboard'
import type { MatchSummaryResponse } from '#shared/types/matches'
import type { ProfileRanked } from '#shared/types/profile'
import { formTiles, RECENT_GAMES } from '#common/utils/match-form'
import { isApexTier } from '#common/utils/tiers'

/**
 * A player's profile, one page on both apps: the site's `/truemains/{nameTag}`
 * and, in the desktop app, the dashboard (the player in the client) and any
 * other player's profile. The desktop dashboard's layout (#1682): a banner on a
 * splash, four form tiles read from the games on screen, the match history, and
 * an 18rem column of cards beside it.
 *
 * What differs between the hosts goes in slots — the header row (a breadcrumb
 * and filters on a profile, the dashboard's title and queue switch), the
 * history (TrueMain's paged list, or the client's), the column's cards (the
 * truemain score on the site, the goals in the app) — and the banner's art is
 * resolved by the host: the champion a true main mains on a profile, the skin
 * the player chose in the client on the dashboard (TrueMain cannot read it).
 */
const props = withDefaults(defineProps<{
  gameName: string
  tagLine?: string | null
  profileIconUrl?: string | null
  region?: RegionSlug | null
  level?: number | null
  ranked?: ProfileRanked | null
  /** The champion the player truemains, named on the banner. */
  truemain?: { name: string, iconUrl: string | null } | null
  splashUrl?: string | null
  splashPosition?: string
  /** The games the form tiles read, newest first. */
  matches: MatchSummaryResponse[]
  loading?: boolean
}>(), {
  tagLine: null,
  profileIconUrl: null,
  region: null,
  level: null,
  ranked: null,
  truemain: null,
  splashUrl: null,
  splashPosition: '68% 22%',
  loading: false,
})

const tiles = computed(() => formTiles(props.matches))
const hint = computed(() => `Last ${RECENT_GAMES} games against the average over ${props.matches.length}`)

const rankLabel = computed(() => {
  const ranked = props.ranked
  if (!ranked) return null
  const tier = ranked.tier.charAt(0) + ranked.tier.slice(1).toLowerCase()
  const division = isApexTier(ranked.tier) ? '' : ` ${ranked.division}`
  return `${tier}${division} ${ranked.leaguePoints.toLocaleString('en-US')} LP`
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <slot name="header" />

    <PageHero
      :splash-url="splashUrl"
      :splash-position="splashPosition"
    >
      <template #portrait>
        <SkeletonImage
          :src="profileIconUrl"
          :alt="`${gameName} profile icon`"
          width="72"
          height="72"
          class="size-[72px] shrink-0 rounded-lg shadow-lg ring-1 ring-white/10"
        />
      </template>

      <template #title>
        <div class="flex min-w-0 items-center gap-2">
          <h1 class="truncate text-[28px] font-semibold leading-tight text-highlighted">
            {{ gameName }}<span
              v-if="tagLine"
              class="font-medium text-muted"
            >#{{ tagLine }}</span>
          </h1>
          <slot name="title-actions" />
        </div>
      </template>

      <template #subtitle>
        <LeaderboardRegionFlag
          v-if="region"
          :region="region"
          :width="18"
        />
        <template v-if="ranked && rankLabel">
          <RankIcon
            :tier="ranked.tier"
            :size="20"
          />
          <span class="font-semibold text-highlighted">{{ rankLabel }}</span>
        </template>
        <span v-else-if="level !== null">Level {{ level }}</span>
        <template v-if="truemain">
          <span class="text-dimmed">·</span>
          <span class="flex items-center gap-1.5">
            Truemain
            <SkeletonImage
              :src="truemain.iconUrl"
              :alt="truemain.name"
              width="20"
              height="20"
              class="size-5 rounded"
            />
            {{ truemain.name }}
          </span>
        </template>
      </template>

      <template
        v-if="$slots.actions"
        #actions
      >
        <slot name="actions" />
      </template>

      <template v-if="loading && !matches.length">
        <USkeleton
          v-for="index in 4"
          :key="index"
          class="h-[86px] rounded-lg bg-ink-950/55"
        />
      </template>
      <KpiTile
        v-for="tile in tiles"
        v-else
        :key="tile.key"
        :label="tile.label"
        :value="tile.value"
        :delta="tile.delta"
        :hint="hint"
        :series="tile.series"
      />
    </PageHero>

    <div class="grid grid-cols-1 items-start gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
      <section class="flex min-w-0 flex-col gap-2">
        <slot name="history" />
      </section>
      <aside class="flex min-w-0 flex-col gap-4">
        <slot name="aside" />
      </aside>
    </div>
  </div>
</template>
