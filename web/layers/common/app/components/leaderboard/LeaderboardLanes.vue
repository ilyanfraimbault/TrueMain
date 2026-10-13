<script setup lang="ts">
import type { LeaderboardPositions } from '#shared/types/leaderboard'
import { leaderboardLanes } from '#common/utils/leaderboard-row'

// A truemain's primary and secondary lane icons; the secondary is dimmed to
// read as the lesser one. The host sets the display and the slot's width, so
// a row reserves the same room whether the player has one lane or none.
const props = defineProps<{ positions: LeaderboardPositions | null }>()

const lanes = computed(() => leaderboardLanes(props.positions))
</script>

<template>
  <div class="items-center gap-1">
    <SkeletonImage
      v-for="lane in lanes"
      :key="lane.position"
      :src="lane.iconUrl"
      :alt="lane.title"
      :title="lane.title"
      class="size-5 shrink-0"
      :class="lane.primary ? undefined : 'opacity-40'"
      loading="lazy"
      transparent
    />
  </div>
</template>
