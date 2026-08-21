<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { JUNGLE_CAMPS, MAP_VIEW, toMapView } from '~/utils/jungle-map'

/**
 * One jungler's first clear (#1188): the camp they opened on, how fast they
 * cleared, and where they were at each sampled minute.
 *
 * Deliberately **not** a camp route. Riot samples positions once per minute and
 * a first clear runs 1:30 → ~3:15, so the whole clear is covered by two samples
 * — ordering six camps from that is impossible. The dots here are timestamped
 * positions, labelled by time, never by camp name. Only the start camp is named,
 * because the jungler waits on it while their jungle CS is still 0.
 */
const props = defineProps<{
  participant: MatchDetailParticipant
  champions: ChampionStaticListItem[]
}>()

const clear = computed(() => props.participant.jungleClear)

const champion = computed(() =>
  props.champions.find(c => c.championId === props.participant.championId))

const isBlueTeam = computed(() => props.participant.teamId === 100)

const startCampLabel = computed(() => {
  const camp = clear.value?.startCamp
  return camp ? JUNGLE_CAMPS[camp]?.label ?? null : null
})

/** Sampled positions, projected onto the minimap and labelled by time. */
const points = computed(() => (clear.value?.samples ?? []).map(sample => ({
  timestampMs: sample.timestampMs,
  jungleCs: sample.jungleCs,
  label: formatDuration(sample.timestampMs / 1000),
  ...toMapView(sample.x, sample.y),
})))

/** The start camp gets its own marker — it is the one named location we can trust. */
const startPoint = computed(() => {
  const camp = clear.value?.startCamp
  const spot = camp ? JUNGLE_CAMPS[camp] : undefined
  return spot ? toMapView(spot.x, spot.y) : null
})

const trail = computed(() => points.value.map(p => `${p.x},${p.y}`).join(' '))

/** Full clear reached, or how far along the jungler got by the last sample. */
const clearVerdict = computed(() => {
  const c = clear.value
  if (!c) return null
  if (c.fullClearTimeMs !== null) {
    return { text: `Full clear by ${formatDuration(c.fullClearTimeMs / 1000)}`, complete: true }
  }
  const last = c.samples.at(-1)
  if (!last) return null
  const pct = Math.round((last.jungleCs / c.fullClearJungleCs) * 100)
  return { text: `${Math.min(pct, 99)}% of a clear by ${formatDuration(last.timestampMs / 1000)}`, complete: false }
})

const stroke = computed(() => isBlueTeam.value ? 'stroke-sky-400' : 'stroke-red-400')
const nodeFill = computed(() => isBlueTeam.value ? 'fill-sky-500' : 'fill-red-500')
</script>

<template>
  <div class="surface flex flex-col gap-2 rounded-md p-3">
    <div class="flex items-center gap-2">
      <SkeletonImage
        :src="champion?.iconUrl ?? null"
        :alt="champion?.name ?? `Champion ${participant.championId}`"
        class="size-8 rounded"
      />
      <span class="truncate text-sm font-semibold text-default">
        {{ champion?.name ?? `Champion ${participant.championId}` }}
      </span>
      <UTooltip
        v-if="clearVerdict"
        text="Jungle CS is sampled once a minute, so the clear time is the first minute mark at which a full clear's worth of camps was done."
      >
        <span
          class="ml-auto shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold"
          :class="clearVerdict.complete
            ? (isBlueTeam ? 'bg-sky-500/15 text-sky-400' : 'bg-red-500/15 text-red-400')
            : 'bg-elevated text-muted'"
        >
          {{ clearVerdict.text }}
        </span>
      </UTooltip>
    </div>

    <p class="text-xs text-muted">
      <template v-if="startCampLabel">
        Started on <span class="font-medium text-default">{{ startCampLabel }}</span>
      </template>
      <template v-else>Starting camp unknown</template>
    </p>

    <div class="relative overflow-hidden rounded">
      <img
        src="/map/map11.png"
        alt=""
        class="w-full select-none"
        draggable="false"
      >
      <svg
        :viewBox="`0 0 ${MAP_VIEW} ${MAP_VIEW}`"
        class="absolute inset-0 size-full"
        role="img"
        :aria-label="`Sampled jungler positions: ${points.map(p => `${p.label} at ${p.jungleCs} jungle CS`).join(', ')}`"
      >
        <polyline
          v-if="points.length > 1"
          :points="trail"
          fill="none"
          :class="stroke"
          class="opacity-40"
          stroke-width="3"
          stroke-dasharray="6 7"
          stroke-linecap="round"
          stroke-linejoin="round"
        />

        <!-- The one named location: the camp the jungler opened on. Drawn as a
             wide halo because the start camp is normally the same spot as the
             sample that revealed it, so a tight ring would sit under that dot. -->
        <circle
          v-if="startPoint"
          :cx="startPoint.x"
          :cy="startPoint.y"
          r="22"
          fill="none"
          :class="stroke"
          class="opacity-70"
          stroke-width="3"
          stroke-dasharray="4 4"
        >
          <title>Started on {{ startCampLabel }}</title>
        </circle>

        <g v-for="point in points" :key="`p-${point.timestampMs}`">
          <circle
            :cx="point.x"
            :cy="point.y"
            r="13"
            :class="nodeFill"
            class="stroke-white/80"
            stroke-width="2"
          />
          <text
            :x="point.x"
            :y="point.y"
            text-anchor="middle"
            dominant-baseline="central"
            class="select-none fill-white text-[11px] font-bold"
          >{{ point.label }}</text>
          <title>{{ point.label }} — {{ point.jungleCs }} jungle CS</title>
        </g>
      </svg>
    </div>

    <ol class="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
      <li v-for="point in points" :key="`cs-${point.timestampMs}`">
        {{ point.label }} · <span class="font-medium text-default">{{ point.jungleCs }}</span> CS
      </li>
    </ol>
  </div>
</template>
