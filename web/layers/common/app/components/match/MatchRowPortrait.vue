<script setup lang="ts">
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// Champion portrait of a match row, badged with the player's role — or with the
// champion level when Riot assigned none (old rows, modes without lanes). The
// role comes from the PUUID-matched self read-model, so it holds even in queues
// that allow the same champion twice on a team.
const props = defineProps<{
  iconUrl: string | null
  championName: string
  position: string | null
  championLevel: number
}>()

const positionLabel = computed(() =>
  props.position ? (POSITION_BY_VALUE.get(props.position)?.label ?? props.position) : undefined)
</script>

<template>
  <div class="relative shrink-0">
    <SkeletonImage
      :src="iconUrl"
      :alt="championName"
      :title="championName"
      :width="36"
      :height="36"
      loading="lazy"
      class="size-9 rounded-md"
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
