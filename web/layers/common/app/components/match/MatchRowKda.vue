<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'

// KDA cluster of a match-history row: KDA on top with the ratio under it.
// Tabular-nums everywhere so digits never jitter, and the column is a fixed
// width (not just min-width) — a 12/2/15 game and a 4/8/8 game must occupy the
// exact same box, or every column to its right drifts row to row and the
// columns stop lining up down the list. The same holds for the row's stats
// column.
const props = defineProps<{
  self: MatchSummarySelf
  durationLabel: string
}>()

const kdaRatio = computed(() => {
  const { kills, deaths, assists } = props.self
  if (deaths === 0) return 'Perfect'
  return `${((kills + assists) / deaths).toFixed(2)} KDA`
})

// Value-graded accent on the KDA ratio (op.gg-style): gold for standout games
// (Perfect or 5+), the data axis' good end for solid ones (3+), muted
// otherwise.
//
// The middle step used to be `text-sky-300` under a comment claiming it
// "matches the win axis". It never did — the win axis is the row's own
// blue/red result colour, not this — and a sky blue on a measurement is a hue
// this palette does not have. Standout is `--color-gold`, the same token the
// MVP crown wears, which is the point: a Perfect KDA is the row saying the same
// thing the crown does.
const kdaColor = computed(() => {
  const { kills, deaths, assists } = props.self
  const ratio = deaths === 0 ? Infinity : (kills + assists) / deaths
  if (ratio >= 5) return 'text-gold'
  if (ratio >= 3) return 'text-data-good'
  return 'text-muted'
})

const lpDeltaText = computed(() => {
  const delta = props.self.lpDelta
  if (delta === null || delta === undefined) return null
  return delta > 0 ? `+${delta} LP` : `${delta} LP`
})
</script>

<template>
  <div class="flex w-28 shrink-0 flex-col items-center">
    <!-- Explicit gap: whitespace between inline spans spaced the slashes unevenly. -->
    <div class="flex items-baseline gap-1 whitespace-nowrap text-base font-bold leading-tight tabular-nums @2xl:text-lg">
      <span>{{ self.kills }}</span>
      <span class="text-muted/70">/</span>
      <span class="text-red-400">{{ self.deaths }}</span>
      <span class="text-muted/70">/</span>
      <span>{{ self.assists }}</span>
    </div>
    <div class="text-[11px] font-semibold tabular-nums" :class="kdaColor">
      {{ kdaRatio }}
    </div>
    <!-- LP delta stays behind a guard for when the backend starts
         deriving it (always null today, so it renders nothing in
         prod). It is a *measurement* — how much the game moved you —
         so it takes the data axis, not the blue/red the row wears for
         who won: the win/loss is already said by the tint, the KDA
         slash colours and the scoreboard header. -->
    <div
      v-if="lpDeltaText"
      class="text-[11px] font-semibold tabular-nums"
      :class="(self.lpDelta ?? 0) >= 0 ? 'text-data-good' : 'text-data-bad'"
    >
      {{ lpDeltaText }}
    </div>
    <!-- Duration lives under CS/m in the row's stats stack, which only
         exists from @3xl — below that it falls back here so a drawer or a
         phone doesn't lose it entirely. -->
    <div class="text-[11px] text-muted tabular-nums @3xl:hidden">
      {{ durationLabel }}
    </div>
  </div>
</template>
