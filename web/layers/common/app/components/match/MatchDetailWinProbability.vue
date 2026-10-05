<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { MatchWinProbability } from '#shared/types/win-probability'
import {
  describeObjective,
  describeSwing,
  forTeam,
  formatGameClock,
  formatSwingPoints,
  SWINGS_SHOWN,
} from '#common/utils/win-probability-swing'

/**
 * A finished game's win-probability curve and the moments that swung it
 * (#1911), read for `perspectiveTeamId`: the chance above even tinted ally,
 * below it enemy, each turning point a tick on the time axis and a row in the
 * list underneath — hovering a row lights its tick. Every figure is the model
 * applied to the game's timeline (`win-probability-timeline.ts`); a monster
 * the model does not weigh is a tick with no figure.
 */
const props = defineProps<{
  winProbability: MatchWinProbability
  perspectiveTeamId: number
  participants: MatchDetailParticipant[]
  champions: ChampionStaticListItem[]
}>()

// The plot's own units: x in game milliseconds over `width`, y the chance
// over `HEIGHT`. The SVG stretches to its box; strokes keep their width.
const HEIGHT = 100
// Each open match draws its own clips: two panels open side by side must not share an id.
const clipId = `wp-${useId()}`
const width = computed(() => Math.max(1, props.winProbability.points.at(-1)?.ms ?? 1))
const x = (ms: number) => (ms / width.value) * 100
const y = (p: number) => (1 - p) * HEIGHT

const points = computed(() => props.winProbability.points.map(point => ({
  ms: point.ms,
  p: forTeam(point.p, props.perspectiveTeamId, 'probability'),
})))
const line = computed(() => points.value.map(point => `${x(point.ms)},${y(point.p)}`).join(' '))
const area = computed(() => {
  const first = points.value[0]
  const last = points.value.at(-1)
  if (!first || !last) return ''
  return `M${x(first.ms)},${HEIGHT / 2} L${line.value.replaceAll(' ', ' L')} L${x(last.ms)},${HEIGHT / 2} Z`
})
const final = computed(() => Math.round((points.value.at(-1)?.p ?? 0.5) * 100))

function championName(participantId: number) {
  const championId = props.participants.find(p => p.participantId === participantId)?.championId
  return props.champions.find(c => c.championId === championId)?.name ?? 'Champion'
}

const swings = computed(() => props.winProbability.swings.slice(0, SWINGS_SHOWN).map((swing, index) => ({
  index,
  ms: swing.ms,
  ...describeSwing(swing, { perspectiveTeamId: props.perspectiveTeamId, championName }),
})))
// The monsters no turning point already marks: the model's unweighted ones, and
// the weighed ones that did not make the list.
const objectives = computed(() => props.winProbability.objectives
  .filter(objective => !swings.value.some(swing => swing.ms === objective.ms))
  .map(objective => ({
    ms: objective.ms,
    ours: objective.teamId === props.perspectiveTeamId,
    label: describeObjective(objective, props.perspectiveTeamId),
  })))

const minuteTicks = computed(() => {
  const minutes = Math.floor(width.value / 60_000)
  const step = minutes > 30 ? 10 : 5
  return Array.from({ length: Math.floor(minutes / step) + 1 }, (_, i) => i * step * 60_000)
})

// ─── Hover: the nearest point on the curve, or a list row's tick ─────────────
const plot = ref<HTMLElement | null>(null)
const hoverMs = ref<number | null>(null)
const highlighted = ref<number | null>(null)

const hovered = computed(() => {
  if (hoverMs.value === null) return null
  let best = points.value[0]
  for (const point of points.value) {
    if (Math.abs(point.ms - hoverMs.value) < Math.abs((best?.ms ?? 0) - hoverMs.value)) best = point
  }
  return best ?? null
})

function onMove(event: PointerEvent) {
  const box = plot.value?.getBoundingClientRect()
  if (!box || box.width === 0) return
  hoverMs.value = Math.min(1, Math.max(0, (event.clientX - box.left) / box.width)) * width.value
}

const summary = computed(() => `Win probability over the game, ending at ${final.value}% for your side`)
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
          <span v-for="p in [1, 0.5, 0]" :key="p" class="absolute right-0 -translate-y-1/2" :style="{ top: `${y(p)}%` }">{{ p * 100 }}%</span>
        </div>
        <div
          ref="plot"
          class="relative h-36 min-w-0 flex-1"
          @pointermove="onMove"
          @pointerleave="hoverMs = null"
        >
          <svg
            :viewBox="`0 0 100 ${HEIGHT}`"
            preserveAspectRatio="none"
            class="absolute inset-0 size-full overflow-visible"
            role="img"
            :aria-label="summary"
          >
            <defs>
              <clipPath :id="`${clipId}-ahead`"><rect x="0" y="0" width="100" :height="HEIGHT / 2" /></clipPath>
              <clipPath :id="`${clipId}-behind`"><rect x="0" :y="HEIGHT / 2" width="100" :height="HEIGHT / 2" /></clipPath>
            </defs>
            <line
              v-for="p in [0.25, 0.75]"
              :key="p"
              x1="0"
              x2="100"
              :y1="y(p)"
              :y2="y(p)"
              class="stroke-[var(--ui-border)]"
              vector-effect="non-scaling-stroke"
            />
            <path :d="area" class="fill-ally/20" :clip-path="`url(#${clipId}-ahead)`" />
            <path :d="area" class="fill-enemy/20" :clip-path="`url(#${clipId}-behind)`" />
            <line
              x1="0"
              x2="100"
              :y1="HEIGHT / 2"
              :y2="HEIGHT / 2"
              class="stroke-[var(--ui-text-dimmed)]"
              stroke-dasharray="3 3"
              vector-effect="non-scaling-stroke"
            />
            <polyline
              :points="line"
              fill="none"
              class="stroke-[var(--ui-text-highlighted)]"
              stroke-width="2"
              stroke-linejoin="round"
              vector-effect="non-scaling-stroke"
            />
            <line
              v-for="swing in swings"
              :key="`swing-${swing.index}`"
              :x1="x(swing.ms)"
              :x2="x(swing.ms)"
              y1="0"
              :y2="HEIGHT"
              :class="highlighted === swing.index ? (swing.ours ? 'stroke-ally' : 'stroke-enemy') : 'stroke-transparent'"
              stroke-width="1.5"
              vector-effect="non-scaling-stroke"
            />
            <line
              v-if="hovered"
              :x1="x(hovered.ms)"
              :x2="x(hovered.ms)"
              y1="0"
              :y2="HEIGHT"
              class="stroke-[var(--ui-text-dimmed)]"
              vector-effect="non-scaling-stroke"
            />
          </svg>
          <span
            v-if="hovered"
            class="pointer-events-none absolute size-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-[var(--ui-text-highlighted)] ring-2 ring-[var(--ui-bg-elevated)]"
            :style="{ left: `${x(hovered.ms)}%`, top: `${y(hovered.p)}%` }"
          />
          <div
            v-if="hovered"
            class="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded bg-default px-2 py-1 text-xs tabular-nums shadow ring-1 ring-default"
            :style="{ left: `${Math.min(90, Math.max(10, x(hovered.ms)))}%` }"
          >
            <span class="text-muted">{{ formatGameClock(hovered.ms) }}</span>
            <span class="ml-1.5 font-semibold text-highlighted">{{ Math.round(hovered.p * 100) }}%</span>
          </div>
        </div>
      </div>

      <div class="ml-10">
        <!-- Event ticks: turning points by side, other monsters neutral. -->
        <div class="relative mt-1 h-3">
          <span
            v-for="swing in swings"
            :key="`tick-${swing.index}`"
            class="absolute top-0 h-3 w-0.5 -translate-x-1/2 rounded-full transition-opacity"
            :class="[swing.ours ? 'bg-ally' : 'bg-enemy', highlighted === null || highlighted === swing.index ? 'opacity-100' : 'opacity-30']"
            :style="{ left: `${x(swing.ms)}%` }"
            :title="`${formatGameClock(swing.ms)} · ${swing.label}`"
          />
          <span
            v-for="objective in objectives"
            :key="`objective-${objective.ms}`"
            class="absolute top-1 size-1.5 -translate-x-1/2 rounded-full bg-[var(--ui-text-dimmed)]"
            :style="{ left: `${x(objective.ms)}%` }"
            :title="`${formatGameClock(objective.ms)} · ${objective.label}`"
          />
        </div>
        <div class="relative h-4 text-[10px] tabular-nums text-dimmed">
          <span
            v-for="tick in minuteTicks"
            :key="tick"
            class="absolute -translate-x-1/2"
            :style="{ left: `${x(tick)}%` }"
          >{{ tick / 60_000 }}'</span>
        </div>
      </div>
    </div>

    <div>
      <h4 class="mb-1.5 text-xs font-semibold uppercase tracking-wider text-muted">
        Turning points
      </h4>
      <ol class="divide-y divide-default/40">
        <li
          v-for="swing in swings"
          :key="swing.index"
          class="flex items-center gap-3 py-1.5 text-sm"
          @pointerenter="highlighted = swing.index"
          @pointerleave="highlighted = null"
        >
          <span class="w-10 shrink-0 text-xs tabular-nums text-muted">{{ formatGameClock(swing.ms) }}</span>
          <span class="size-1.5 shrink-0 rounded-full" :class="swing.ours ? 'bg-ally' : 'bg-enemy'" />
          <span class="min-w-0 flex-1 text-pretty text-default">{{ swing.label }}</span>
          <span v-if="swing.gold !== null" class="inline-flex shrink-0 items-center gap-1 text-xs tabular-nums text-muted">
            <UIcon name="i-lucide-coins" class="size-3 text-amber-400/80" />{{ swing.gold }}
          </span>
          <span class="w-12 shrink-0 text-right text-xs font-semibold tabular-nums text-highlighted">
            {{ formatSwingPoints(swing.delta) }}<span class="font-normal text-dimmed"> pts</span>
          </span>
        </li>
      </ol>
    </div>
  </section>
</template>
