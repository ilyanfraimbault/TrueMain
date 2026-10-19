<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import { championDisplayName } from '#shared/utils/champion-name'
import {
  buildTimelineCurves,
  CURVE_METRICS,
  formatCurveDelta,
  formatCurveValue,
  formatTeamDifference,
  minuteTicks,
  niceCeiling,
  teamCurves,
  type CurveMetric,
  type PlayerCurve,
} from '#common/utils/timeline-curves'
import { objectiveMarkers } from '#common/utils/timeline-objectives'
import { formatGameClock } from '#common/utils/win-probability-swing'

/**
 * The ten players' gold, XP or creep score over the game (#2102), above the
 * win probability in the desktop app's Timeline view: one curve a player — the
 * viewer's own on top in the primary colour, the others thinner in their side's
 * colour —, the team lead as a band under them, the objectives on the time
 * axis, and a list per side whose rows pin a line. Read for
 * `perspectiveTeamId` from the client's timeline (`timeline-curves.ts`); the
 * caller mounts it only when that timeline carries the curves.
 */
const props = defineProps<{
  timeline: WinProbabilityTimeline
  perspectiveTeamId: number
  /** The viewer's participant id, null when the row's owner is not found. */
  selfParticipantId: number | null
  participants: MatchDetailParticipant[]
  champions: ChampionStaticListItem[]
}>()

const metric = ref<CurveMetric>('gold')
const pinned = ref<number | null>(null)

const curves = computed(() => buildTimelineCurves(props.timeline, metric.value, props.perspectiveTeamId))
const teams = computed(() => (curves.value ? teamCurves(curves.value, props.perspectiveTeamId) : null))
const difference = computed(() => (teams.value ? teams.value[0].total - teams.value[1].total : 0))
const unit = computed(() => CURVE_METRICS.find(entry => entry.key === metric.value)!.unit)

// Plot units, both 0..100: x is game time over the game's length, y the value
// over the axis' round top (the lead band: ± its own round top, zero at 50).
const width = computed(() => Math.max(1, curves.value?.durationMs ?? 1))
const x = (ms: number) => (ms / width.value) * 100
const top = computed(() => niceCeiling(Math.max(0, ...(curves.value?.players.flatMap(p => p.points.map(point => point.value)) ?? [0]))))
const leadTop = computed(() => niceCeiling(Math.max(0, ...(curves.value?.lead.map(point => Math.abs(point.value)) ?? [0]))))
const y = (value: number) => (1 - value / top.value) * 100

const championsById = useChampionsById(() => props.champions)
const championIdByParticipant = computed(() => new Map(props.participants.map(p => [p.participantId, p.championId])))
function champion(participantId: number) {
  const championId = championIdByParticipant.value.get(participantId) ?? 0
  const entry = championsById.value.get(championId)
  return { name: championDisplayName(championId, entry), iconUrl: entry?.iconUrl ?? null }
}

const lines = computed(() => {
  const players = curves.value?.players ?? []
  const emphasis = (participantId: number) => {
    if (participantId === props.selfParticipantId) return 'self' as const
    return participantId === pinned.value ? 'pinned' as const : 'muted' as const
  }
  const tone = (player: PlayerCurve) => {
    if (player.participantId === props.selfParticipantId) return 'self' as const
    return player.teamId === props.perspectiveTeamId ? 'ally' as const : 'enemy' as const
  }
  const order = { muted: 0, pinned: 1, self: 2 }
  return players
    .map(player => ({
      key: player.participantId,
      points: player.points.map(point => `${x(point.ms)},${y(point.value)}`).join(' '),
      tone: tone(player),
      emphasis: emphasis(player.participantId),
    }))
    .sort((a, b) => order[a.emphasis] - order[b.emphasis])
})
const band = computed(() => (curves.value?.lead ?? []).map(point => ({ x: x(point.ms), y: 50 - (point.value / leadTop.value) * 50 })))

const markers = computed(() => objectiveMarkers(props.timeline, props.perspectiveTeamId).map(marker => ({
  ...marker,
  x: x(marker.ms),
  ours: marker.teamId === props.perspectiveTeamId,
})))
const minutes = computed(() => minuteTicks(width.value).map(tick => ({ minute: tick.minute, x: x(tick.ms) })))
const maxEnd = computed(() => Math.max(0, ...(curves.value?.players.map(player => player.end) ?? [0])))

function pin(participantId: number) {
  pinned.value = pinned.value === participantId ? null : participantId
}

// ─── Hover: the nearest frame, its lead and the highlighted players' values ───
const plot = ref<HTMLElement | null>(null)
const hoverIndex = ref<number | null>(null)
const hovered = computed(() => {
  const index = hoverIndex.value
  const lead = index === null ? null : curves.value?.lead[index]
  if (!lead || index === null) return null
  const players = [props.selfParticipantId, pinned.value]
    .filter((id, i, ids): id is number => id !== null && ids.indexOf(id) === i)
    .flatMap((id) => {
      const player = curves.value?.players.find(p => p.participantId === id)
      return player ? [{ participantId: id, name: champion(id).name, value: player.points[index]!.value }] : []
    })
  return { ms: lead.ms, x: x(lead.ms), lead: lead.value, players }
})

function onMove(event: PointerEvent) {
  const box = plot.value?.getBoundingClientRect()
  const lead = curves.value?.lead ?? []
  if (!box || box.width === 0 || lead.length === 0) return
  const ms = Math.min(1, Math.max(0, (event.clientX - box.left) / box.width)) * width.value
  let best = 0
  lead.forEach((point, index) => {
    if (Math.abs(point.ms - ms) < Math.abs(lead[best]!.ms - ms)) best = index
  })
  hoverIndex.value = best
}

const summary = computed(() => `${unit.value} of the ten players over the game; ${formatTeamDifference(difference.value, metric.value)} at the end`)
const leadSummary = computed(() => `Team ${unit.value} lead over the game, your side above the line`)
const differenceTone = computed(() => {
  if (Math.round(difference.value) === 0) return 'text-muted'
  return difference.value > 0 ? 'text-ally' : 'text-enemy'
})
</script>

<template>
  <section v-if="curves && teams" class="surface flex flex-col gap-3 rounded-md p-3">
    <header class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex items-baseline gap-3">
        <h3 class="text-xs font-semibold uppercase tracking-wider text-muted">
          {{ CURVE_METRICS.find(entry => entry.key === metric)!.label }} over the game
        </h3>
        <span class="text-xs font-semibold tabular-nums" :class="differenceTone">
          {{ formatTeamDifference(difference, metric) }}
        </span>
      </div>
      <MatchDetailTimelineMetricSwitch v-model="metric" />
    </header>

    <div class="flex flex-col gap-4 @2xl:flex-row">
      <div class="min-w-0 flex-1" @pointermove="onMove" @pointerleave="hoverIndex = null">
        <div class="flex gap-2">
          <div class="relative h-40 w-10 shrink-0 text-right text-[10px] tabular-nums text-dimmed">
            <span v-for="value in [top, top / 2, 0]" :key="value" class="absolute right-0 -translate-y-1/2" :style="{ top: `${y(value)}%` }">{{ formatCurveValue(value, metric) }}</span>
          </div>
          <div ref="plot" class="relative h-40 min-w-0 flex-1">
            <MatchDetailTimelineCurvesPlot :lines="lines" :hover-x="hovered?.x ?? null" :summary="summary" />
            <div
              v-if="hovered"
              class="pointer-events-none absolute top-0 z-10 flex w-max max-w-64 -translate-x-1/2 flex-col gap-0.5 rounded-md bg-default px-2.5 py-1.5 text-xs shadow-lg ring-1 ring-default"
              :style="{ left: `${Math.min(80, Math.max(20, hovered.x))}%` }"
            >
              <span class="tabular-nums">
                <span class="text-muted">{{ formatGameClock(hovered.ms) }}</span>
                <span class="ml-1.5 font-semibold text-highlighted">{{ formatTeamDifference(hovered.lead, metric) }}</span>
              </span>
              <span v-for="player in hovered.players" :key="player.participantId" class="flex items-center justify-between gap-3">
                <span class="text-default">{{ player.name }}</span>
                <span class="tabular-nums text-highlighted">{{ formatCurveValue(player.value, metric) }}</span>
              </span>
            </div>
          </div>
        </div>

        <div class="mt-2 flex gap-2">
          <div class="relative h-12 w-10 shrink-0 text-right text-[10px] tabular-nums text-dimmed">
            <!-- Inside the band's edges, clear of the curves' `0` just above. -->
            <span class="absolute right-0 top-0 leading-none">{{ formatCurveDelta(leadTop, metric) }}</span>
            <span class="absolute bottom-0 right-0 leading-none">{{ formatCurveDelta(-leadTop, metric) }}</span>
          </div>
          <div class="relative h-12 min-w-0 flex-1">
            <MatchDetailWinProbabilityCurve :points="band" :rules="[]" :hover-x="hovered?.x ?? null" :summary="leadSummary" />
          </div>
        </div>

        <MatchDetailTimelineObjectiveAxis class="ml-12" :markers="markers" :minutes="minutes" />
      </div>

      <div class="grid grid-cols-1 gap-3 @md:grid-cols-2 @2xl:w-60 @2xl:shrink-0 @2xl:grid-cols-1">
        <MatchDetailTimelineTeamList
          v-for="(team, index) in teams"
          :key="team.teamId"
          :team="team"
          :ours="index === 0"
          :metric="metric"
          :max="maxEnd"
          :self-participant-id="selfParticipantId"
          :pinned-participant-id="pinned"
          :champion="champion"
          @pin="pin"
        />
      </div>
    </div>
  </section>
</template>
