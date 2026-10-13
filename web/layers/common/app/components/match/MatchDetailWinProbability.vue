<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { championDisplayName } from '#shared/utils/champion-name'
import { formatPercentage } from '#shared/utils/ddragon'
import type { MatchWinProbability } from '#shared/types/win-probability'
import {
  describeObjective,
  describeSwing,
  forTeam,
  SWINGS_SHOWN,
} from '#common/utils/win-probability-swing'

/**
 * A finished game's win-probability curve and the moments that swung it
 * (#1911), read for `perspectiveTeamId`. What happened is read on the curve
 * itself: hovering a minute lists that stretch's turning points and objectives
 * in the tooltip and lights their ticks — no list under the chart (the product
 * owner's call on the refreshed match detail). Every figure is the model
 * applied to the game's timeline (`win-probability-timeline.ts`); a monster the
 * model does not weigh is a tick with no figure. This file places everything on
 * the plot's axes; the curve, the tooltip and the time axis draw it.
 */
const props = defineProps<{
  winProbability: MatchWinProbability
  perspectiveTeamId: number
  participants: MatchDetailParticipant[]
  champions: ChampionStaticListItem[]
}>()

// Plot units, both 0..100: x is game time over the game's length, y the chance.
const width = computed(() => Math.max(1, props.winProbability.points.at(-1)?.ms ?? 1))
const x = (ms: number) => (ms / width.value) * 100
const y = (p: number) => (1 - p) * 100

const points = computed(() => props.winProbability.points.map((point) => {
  const p = forTeam(point.p, props.perspectiveTeamId, 'probability')
  return { ms: point.ms, p, x: x(point.ms), y: y(p) }
}))
const final = computed(() => formatPercentage(points.value.at(-1)?.p ?? 0.5, 0))

const championsById = useChampionsById(() => props.champions)
const championIdByParticipant = computed(() => new Map(props.participants.map(p => [p.participantId, p.championId])))

function championName(participantId: number) {
  const championId = championIdByParticipant.value.get(participantId) ?? 0
  return championDisplayName(championId, championsById.value.get(championId))
}

const swings = computed(() => props.winProbability.swings.slice(0, SWINGS_SHOWN).map((swing, index) => ({
  index,
  ms: swing.ms,
  x: x(swing.ms),
  ...describeSwing(swing, { perspectiveTeamId: props.perspectiveTeamId, championName }),
})))
// The monsters no turning point already marks: the model's unweighted ones, and
// the weighed ones that did not make the list.
const objectives = computed(() => props.winProbability.objectives
  .filter(objective => !swings.value.some(swing => swing.ms === objective.ms))
  .map(objective => ({ ms: objective.ms, x: x(objective.ms), label: describeObjective(objective, props.perspectiveTeamId) })))

const minutes = computed(() => {
  const total = Math.floor(width.value / 60_000)
  const step = total > 30 ? 10 : 5
  return Array.from({ length: Math.floor(total / step) + 1 }, (_, i) => ({ minute: i * step, x: x(i * step * 60_000) }))
})

// ─── Hover: the nearest point on the curve and what happened around it ───────
const plot = ref<HTMLElement | null>(null)
const hoverMs = ref<number | null>(null)

const hovered = computed(() => {
  if (hoverMs.value === null) return null
  let best = points.value[0]
  for (const point of points.value) {
    if (Math.abs(point.ms - hoverMs.value) < Math.abs((best?.ms ?? 0) - hoverMs.value)) best = point
  }
  return best ?? null
})

// The curve is sampled about once a minute, so a hovered point stands for the
// half-minute either side of it: the events in that window are its story.
const HOVER_WINDOW_MS = 30_000
const nearHover = (ms: number) => hovered.value !== null && Math.abs(ms - hovered.value.ms) <= HOVER_WINDOW_MS
const litSwings = computed(() => swings.value.map(swing => ({ ...swing, lit: nearHover(swing.ms) })))
const hoveredSwings = computed(() => litSwings.value.filter(swing => swing.lit))
const hoveredObjectives = computed(() => objectives.value.filter(objective => nearHover(objective.ms)))
const rules = computed(() => litSwings.value.map(swing => ({ key: swing.index, x: swing.x, ours: swing.ours, lit: swing.lit })))

function onMove(event: PointerEvent) {
  const box = plot.value?.getBoundingClientRect()
  if (!box || box.width === 0) return
  hoverMs.value = Math.min(1, Math.max(0, (event.clientX - box.left) / box.width)) * width.value
}

const summary = computed(() => `Win probability over the game, ending at ${final.value} for your side`)
</script>

<template>
  <section class="surface flex flex-col gap-4 rounded-md p-3">
    <header class="flex items-baseline justify-between gap-3">
      <h3 class="text-xs font-semibold uppercase tracking-wider text-muted">
        Win probability
      </h3>
      <span class="hidden text-xs text-dimmed @md:inline">
        Model on lane leads and objectives
      </span>
    </header>

    <div>
      <div class="flex gap-2">
        <div class="relative h-36 w-8 shrink-0 text-right text-[10px] tabular-nums text-dimmed">
          <span v-for="p in [1, 0.5, 0]" :key="p" class="absolute right-0 -translate-y-1/2" :style="{ top: `${y(p)}%` }">{{ formatPercentage(p, 0) }}</span>
        </div>
        <div
          ref="plot"
          class="relative h-36 min-w-0 flex-1"
          @pointermove="onMove"
          @pointerleave="hoverMs = null"
        >
          <MatchDetailWinProbabilityCurve
            :points="points"
            :rules="rules"
            :hover-x="hovered?.x ?? null"
            :summary="summary"
          />
          <template v-if="hovered">
            <span
              class="pointer-events-none absolute size-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-[var(--ui-text-highlighted)] ring-2 ring-[var(--ui-bg-elevated)]"
              :style="{ left: `${hovered.x}%`, top: `${hovered.y}%` }"
            />
            <MatchDetailWinProbabilityTooltip
              :ms="hovered.ms"
              :chance="hovered.p"
              :left="Math.min(80, Math.max(20, hovered.x))"
              :swings="hoveredSwings"
              :objectives="hoveredObjectives"
            />
          </template>
        </div>
      </div>

      <MatchDetailWinProbabilityTimeAxis
        class="ml-10"
        :swings="litSwings"
        :objectives="objectives"
        :hovering="hoveredSwings.length > 0"
        :minutes="minutes"
      />
    </div>
  </section>
</template>
