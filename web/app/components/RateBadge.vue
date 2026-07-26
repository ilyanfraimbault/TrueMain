<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

defineProps<{
  pickRate: number
  winRate: number
}>()
</script>

<template>
  <div class="flex shrink-0 items-center gap-2 whitespace-nowrap">
    <UBadge
      color="neutral"
      size="sm"
    >
      {{ formatPercentage(pickRate) }} pick
    </UBadge>
    <UBadge
      color="primary"
      size="sm"
    >
      {{ formatPercentage(winRate) }} win
    </UBadge>
  </div>
</template>
