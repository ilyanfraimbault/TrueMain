<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { FOUNTAINS, JUNGLE_CAMPS, MAP_VIEW, toMapView } from '~/utils/jungle-map'

/**
 * One jungler's first clear drawn on the Summoner's Rift minimap (#1186):
 * numbered camp nodes in clear order, a camp-to-camp path in the jungler's
 * team colour, and a dashed fountain detour for each derived base visit.
 *
 * The path connects camp centroids — the per-minute position trail is not
 * persisted (#535), so this is the clear's *order*, not the walked route, and
 * step times are frame timestamps (minute resolution, ties possible). That is
 * also why the list shows absolute times only, never per-camp durations.
 */
const props = defineProps<{
  participant: MatchDetailParticipant
  champions: ChampionStaticListItem[]
}>()

const FULL_CLEAR_CAMPS = 6

const clear = computed(() => props.participant.jungleClear)

const champion = computed(() =>
  props.champions.find(c => c.championId === props.participant.championId))

const isBlueTeam = computed(() => props.participant.teamId === 100)

/** Steps resolved to minimap coordinates; unknown camp names are skipped defensively. */
const nodes = computed(() => (clear.value?.steps ?? [])
  .map((step, index) => {
    const spot = JUNGLE_CAMPS[step.camp]
    if (!spot) return null
    return {
      index,
      order: index + 1,
      label: spot.label,
      timestampMs: step.timestampMs,
      ...toMapView(spot.x, spot.y),
    }
  })
  .filter((n): n is NonNullable<typeof n> => n !== null))

const fountain = computed(() => {
  const spot = isBlueTeam.value ? FOUNTAINS.blue : FOUNTAINS.red
  return toMapView(spot.x, spot.y)
})

const recallByGap = computed(() => {
  const map = new Map<number, number>()
  for (const recall of clear.value?.recalls ?? []) {
    map.set(recall.afterStepIndex, recall.timestampMs)
  }
  return map
})

interface PathSegment {
  points: string
  recallMs: number | null
}

/**
 * One segment per consecutive camp pair. A gap carrying a recall detours
 * through the jungler's fountain and renders dashed instead of straight.
 */
const segments = computed<PathSegment[]>(() => {
  const list: PathSegment[] = []
  const pts = nodes.value
  for (let i = 0; i < pts.length - 1; i++) {
    const from = pts[i]!
    const to = pts[i + 1]!
    const recallMs = recallByGap.value.get(from.index) ?? null
    const via = recallMs === null ? '' : ` ${fountain.value.x},${fountain.value.y}`
    list.push({
      points: `${from.x},${from.y}${via} ${to.x},${to.y}`,
      recallMs,
    })
  }
  return list
})

const hasRecall = computed(() => segments.value.some(s => s.recallMs !== null))

/** Footer rows: the numbered steps with base visits interleaved at their gap. */
const listRows = computed(() => {
  const rows: { key: string, text: string, isRecall: boolean }[] = []
  for (const node of nodes.value) {
    rows.push({
      key: `step-${node.index}`,
      text: `${node.order} · ${node.label} — ${formatDuration(node.timestampMs / 1000)}`,
      isRecall: false,
    })
    const recallMs = recallByGap.value.get(node.index)
    if (recallMs !== undefined) {
      rows.push({
        key: `recall-${node.index}`,
        text: `Base — ${formatDuration(recallMs / 1000)}`,
        isRecall: true,
      })
    }
  }
  return rows
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
        :text="clear?.fullClearTimeMs != null
          ? 'Full clear time — camp timings are minute-resolution'
          : 'The jungler did not finish all six camps in the first-clear window'"
      >
        <span
          class="ml-auto shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold"
          :class="isBlueTeam ? 'bg-sky-500/15 text-sky-400' : 'bg-red-500/15 text-red-400'"
        >
          {{ clear?.fullClearTimeMs != null
            ? `Full clear ${formatDuration(clear.fullClearTimeMs / 1000)}`
            : `Partial clear (${nodes.length}/${FULL_CLEAR_CAMPS})` }}
        </span>
      </UTooltip>
    </div>

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
        :aria-label="`First clear path: ${listRows.map(r => r.text).join(', ')}`"
      >
        <polyline
          v-for="(segment, i) in segments"
          :key="`seg-${i}`"
          :points="segment.points"
          fill="none"
          :class="stroke"
          class="opacity-70"
          stroke-width="4"
          stroke-linecap="round"
          stroke-linejoin="round"
          :stroke-dasharray="segment.recallMs !== null ? '8 8' : undefined"
        />

        <!-- Fountain marker for the base visit(s). -->
        <g v-if="hasRecall">
          <circle
            :cx="fountain.x"
            :cy="fountain.y"
            r="12"
            class="fill-neutral-900/80"
            :class="stroke"
            stroke-width="2"
          />
          <text
            :x="fountain.x"
            :y="fountain.y"
            text-anchor="middle"
            dominant-baseline="central"
            class="select-none fill-white text-[13px] font-bold"
          >B</text>
          <title>
            {{ segments.filter(s => s.recallMs !== null)
              .map(s => `Base at ${formatDuration((s.recallMs ?? 0) / 1000)}`).join(', ') }}
          </title>
        </g>

        <g v-for="node in nodes" :key="`node-${node.index}`">
          <circle
            :cx="node.x"
            :cy="node.y"
            r="12"
            :class="nodeFill"
            class="stroke-white/80"
            stroke-width="2"
          />
          <text
            :x="node.x"
            :y="node.y"
            text-anchor="middle"
            dominant-baseline="central"
            class="select-none fill-white text-[14px] font-bold"
          >{{ node.order }}</text>
          <title>{{ node.label }} — {{ formatDuration(node.timestampMs / 1000) }}</title>
        </g>
      </svg>
    </div>

    <ol class="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
      <li
        v-for="row in listRows"
        :key="row.key"
        :class="row.isRecall ? 'font-medium text-default' : undefined"
      >
        {{ row.text }}
      </li>
    </ol>
  </div>
</template>
