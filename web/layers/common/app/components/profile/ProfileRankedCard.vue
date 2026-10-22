<script setup lang="ts">
import type { ProfileRanked } from '#shared/types/profile'
import type { ApexCutoff } from '#shared/types/apex-cutoffs'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import { rankChartPoints } from '#common/utils/rank-chart'

// The player's Solo/Duo standing: rank, record, LP moved over 7 and 30 days,
// and the LP curve over the rank-history window. The site's profile and
// player-scoped champion page read the history from TrueMain; the app's
// dashboard from its own local snapshots. The app adds what only the client
// knows through two slots the site leaves empty: `standing` under the Solo/Duo
// block (the Flex standing), `moves` under the curve (the latest games' LP).
const props = withDefaults(defineProps<{
  ranked: ProfileRanked | null
  history?: readonly RankHistoryEntry[]
  /** The region's Grandmaster / Challenger cutoffs over the window, for the curve's apex thresholds. */
  cutoffs?: readonly ApexCutoff[]
  historyLoading?: boolean
}>(), {
  history: () => [],
  cutoffs: () => [],
  historyLoading: false,
})

const points = computed(() => rankChartPoints(props.history))

const showEmptyChart = computed(
  () => !props.historyLoading && points.value.length === 0 && props.ranked !== null,
)
</script>

<template>
  <section class="flex flex-col gap-3 surface rounded-lg px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Ranked Solo/Duo
    </h2>

    <template v-if="ranked">
      <RankSummary
        :tier="ranked.tier"
        :division="ranked.division"
        :league-points="ranked.leaguePoints"
        :wins="ranked.wins"
        :losses="ranked.losses"
        :win-rate="ranked.winRate"
        :size="48"
      />
      <ProfileLpDeltaChips :points="points" />
    </template>
    <p v-else class="text-base text-muted">
      Unranked
    </p>

    <slot name="standing" />

    <USkeleton v-if="historyLoading" class="h-[150px] w-full rounded-md" />

    <p v-else-if="showEmptyChart" class="text-sm text-muted">
      No ranked snapshots in the last 90 days.
    </p>

    <ProfileRankChart
      v-else-if="points.length > 0"
      :points="points"
      :cutoffs="cutoffs"
    />

    <slot name="moves" />
  </section>
</template>
