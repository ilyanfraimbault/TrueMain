<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { PositionOption } from '#shared/utils/positions'
import { formatCompactCount } from '~~/shared/utils/counts'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { winRateTone } from '#common/utils/rate-tone'

// The front card's figures, on a liquid-glass plate that bends the art under
// its rim (`FxLiquidGlass`; frosted where the engine cannot refract). Mounted
// only on the card in front once the ring rests, so a single plate refracts at
// a time, and its rates count up each time a new champion comes round.
defineProps<{
  row: ChampionOverviewRow
  name: string
  lane: PositionOption | undefined
}>()

const pct = (value: number) => formatPercentage(value)
</script>

<template>
  <FxLiquidGlass
    :radius="20"
    :bezel="18"
    :depth="22"
    :blur="1.4"
    :saturation="1.5"
    class="h-plate"
  >
    <span class="h-plate__meta">
      <img
        v-if="lane?.iconUrl"
        :src="lane.iconUrl"
        alt=""
        width="14"
        height="14"
        class="size-3.5 opacity-90"
      >
      <span>{{ lane?.label ?? row.position }}</span>
      <span
        class="h-plate__dot"
        aria-hidden="true"
      />
      <span class="tabular-nums">{{ formatCompactCount(row.games) }} games</span>
    </span>
    <span class="h-plate__name">
      <span class="truncate">{{ name }}</span>
      <UIcon
        name="i-lucide-arrow-up-right"
        class="h-plate__go"
      />
    </span>
    <span class="h-plate__stats">
      <span class="h-plate__stat">
        <span
          class="h-plate__value"
          :class="winRateTone(row.winRate)"
        ><FxCount
          :value="row.winRate"
          :format="pct"
          :duration="1100"
        /></span>
        <span class="h-plate__label">Win</span>
      </span>
      <span class="h-plate__stat">
        <span class="h-plate__value"><FxCount
          :value="row.pickRate"
          :format="pct"
          :duration="1100"
        /></span>
        <span class="h-plate__label">Pick</span>
      </span>
      <span
        v-if="row.banRate !== null"
        class="h-plate__stat"
      >
        <span class="h-plate__value"><FxCount
          :value="row.banRate"
          :format="pct"
          :duration="1100"
        /></span>
        <span class="h-plate__label">Ban</span>
      </span>
    </span>
  </FxLiquidGlass>
</template>
