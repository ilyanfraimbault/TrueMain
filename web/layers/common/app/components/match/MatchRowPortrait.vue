<script setup lang="ts">
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// Champion portrait of a match row, badged with the player's role — or with the
// champion level when there is no role to show (old rows, modes without lanes,
// the scoreboard, which names the level rather than the role). The role comes
// from the PUUID-matched self read-model, so it holds even in queues that allow
// the same champion twice on a team.
const props = withDefaults(defineProps<{
  iconUrl: string | null
  championName: string
  position: string | null
  championLevel: number
  /** Portrait edge in px: 36 on the row, 40 on the scoreboard. */
  size?: number
  /**
   * Links the portrait to the champion's build page. Only where the portrait is
   * a standalone target: the row is itself a toggle, so it never sets this.
   */
  championId?: number | null
}>(), {
  size: 36,
  championId: null,
})

const positionLabel = computed(() =>
  props.position ? (POSITION_BY_VALUE.get(props.position)?.label ?? props.position) : undefined)
const box = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))
</script>

<template>
  <div class="relative shrink-0">
    <ChampionLink
      v-if="championId"
      :champion-id="championId"
      :name="championName"
      :icon-url="iconUrl"
      class="block rounded-md"
      :style="box"
    />
    <SkeletonImage
      v-else
      :src="iconUrl"
      :alt="championName"
      :title="championName"
      :width="size"
      :height="size"
      loading="lazy"
      class="rounded-md"
    />
    <span
      class="absolute -bottom-1 -right-1 flex size-4 items-center justify-center rounded-full bg-default text-[9px] font-bold ring-1 ring-default"
      :title="positionLabel"
    >
      <img v-if="position" :src="getPositionIconUrl(position)" :alt="positionLabel" class="size-3">
      <template v-else>{{ championLevel }}</template>
    </span>
  </div>
</template>
