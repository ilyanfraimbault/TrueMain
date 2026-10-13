<script setup lang="ts">
import { isApexTier } from '#common/utils/tiers'

// A player's rank where a row has no room to write it: the tier's crest, the
// division beside it below the apex (the crest alone can't show it), and the
// full standing — rank, LP, win-loss — on hover, the profile card's
// `RankSummary`. The one emblem of the truemains table, the champion page's
// truemains, the home page's top truemains, the app's build mains and the match
// scoreboard (#2025).
//
// Attributes land on the trigger, not the tooltip: a host passes the column's
// width there, or `relative z-10` to lift it above a row's stretched link.
defineOptions({ inheritAttrs: false })

withDefaults(defineProps<{
  rank: { tier: string, division: string, leaguePoints: number }
  /** The win-loss the tooltip writes under the rank; none when the host has no record. */
  record?: { wins: number | null, losses: number | null, winRate: number | null } | null
  /** Crest size in px. */
  size?: number
  /** The crest without the division beside it, where the column is too short for it. */
  crestOnly?: boolean
  ui?: { division?: string }
}>(), {
  record: null,
  size: 24,
  crestOnly: false,
  ui: () => ({}),
})

const TOOLTIP_UI = { content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }
</script>

<template>
  <UTooltip :delay-duration="150" :ui="TOOLTIP_UI">
    <span class="flex shrink-0 items-center gap-1" v-bind="$attrs">
      <RankIcon :tier="rank.tier" :size="size" loading="lazy" />
      <span
        v-if="!crestOnly && !isApexTier(rank.tier)"
        class="text-xs font-semibold tabular-nums"
        :class="ui.division"
      >{{ rank.division }}</span>
    </span>

    <template #content>
      <GameTooltipSurface>
        <RankSummary
          :tier="rank.tier"
          :division="rank.division"
          :league-points="rank.leaguePoints"
          :wins="record?.wins ?? null"
          :losses="record?.losses ?? null"
          :win-rate="record?.winRate ?? null"
          :size="32"
        />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
