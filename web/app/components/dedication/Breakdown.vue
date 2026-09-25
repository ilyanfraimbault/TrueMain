<script setup lang="ts">
import type { TruemainScorePartView } from '~/utils/dedication'

// The Truemain score's parts, shared by the profile card and the leaderboard
// row's tooltip so the two surfaces can't drift apart. Each line prints the
// raw fact and the points it added; the bar is those points over the part's
// maximum, so the printed figure and the bar always tell the same story, and
// the points add up to the score above them.
defineProps<{
  parts: TruemainScorePartView[]
}>()
</script>

<template>
  <ul class="flex flex-col gap-2">
    <li
      v-for="part in parts"
      :key="part.key"
      class="flex flex-col gap-1"
    >
      <div class="flex items-baseline gap-2 text-[11px]">
        <span class="font-medium text-default">{{ part.label }}</span>
        <span class="min-w-0 flex-1 truncate text-muted">{{ part.detail }}</span>
        <span class="shrink-0 tabular-nums text-default">
          {{ part.points }}<span class="text-dimmed">/{{ part.maxPoints }}</span>
        </span>
      </div>
      <div class="h-1.5 w-full overflow-hidden rounded-full bg-elevated/60">
        <div
          class="h-full rounded-full bg-primary/70"
          :style="{ width: `${part.maxPoints > 0 ? Math.round((part.points / part.maxPoints) * 100) : 0}%` }"
        />
      </div>
    </li>
  </ul>
</template>
