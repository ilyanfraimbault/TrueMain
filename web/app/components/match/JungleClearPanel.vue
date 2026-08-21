<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { JUNGLE_CAMP_LABELS } from '~/utils/jungle-map'

/**
 * One jungler's first clear: the camp they opened on and how fast they cleared,
 * minute by minute.
 *
 * There is no map (#1195). Riot samples positions once per minute while a clear
 * runs 1:30 → ~3:15, so three to four camps fall between two consecutive
 * samples: plotting them drew five dots that read as a route through a jungle
 * the jungler had already finished clearing between the dots. What the data
 * does support exactly is *how many* camps were done at each minute — a camp is
 * scored as a unit worth 4 CS, so the count is a division, not an estimate.
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
  return camp ? JUNGLE_CAMP_LABELS[camp] ?? null : null
})

const fullClearCamps = computed(() => clear.value?.fullClearCamps ?? 6)

/**
 * One row per sampled minute. The t=0 sample is dropped: it only ever says
 * "in the fountain, nothing cleared", which is noise in a speed readout.
 */
const steps = computed(() => (clear.value?.samples ?? [])
  .filter(sample => sample.timestampMs > 0)
  .map(sample => ({
    timestampMs: sample.timestampMs,
    label: formatDuration(sample.timestampMs / 1000),
    camps: sample.campsCleared,
    // Capped at the full clear so a jungler who kept farming past six camps
    // doesn't overflow the bar; the count beside it still reads the true value.
    fill: Math.min(sample.campsCleared / fullClearCamps.value, 1) * 100,
    done: sample.campsCleared >= fullClearCamps.value,
  })))

const verdict = computed(() => {
  const c = clear.value
  if (!c) return null
  if (c.fullClearTimeMs !== null) {
    return { text: `Full clear by ${formatDuration(c.fullClearTimeMs / 1000)}`, complete: true }
  }
  const last = c.samples.at(-1)
  if (!last) return null
  return {
    text: `${last.campsCleared}/${c.fullClearCamps} camps by ${formatDuration(last.timestampMs / 1000)}`,
    complete: false,
  }
})

const barColor = computed(() => isBlueTeam.value ? 'bg-sky-500' : 'bg-red-500')
</script>

<template>
  <div class="surface flex flex-col gap-3 rounded-md p-3">
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
        v-if="verdict"
        text="Jungle CS is sampled once a minute, so this is the first minute mark by which the camps were done — not an exact instant."
      >
        <span
          class="ml-auto shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold"
          :class="verdict.complete
            ? (isBlueTeam ? 'bg-sky-500/15 text-sky-400' : 'bg-red-500/15 text-red-400')
            : 'bg-elevated text-muted'"
        >
          {{ verdict.text }}
        </span>
      </UTooltip>
    </div>

    <p class="text-xs text-muted">
      <template v-if="startCampLabel">
        Started on <span class="font-medium text-default">{{ startCampLabel }}</span>
      </template>
      <template v-else>Starting camp unknown</template>
    </p>

    <ol class="flex flex-col gap-1.5">
      <li
        v-for="step in steps"
        :key="step.timestampMs"
        class="flex items-center gap-2 text-xs"
      >
        <span class="w-9 shrink-0 tabular-nums text-muted">{{ step.label }}</span>
        <span class="h-2 flex-1 overflow-hidden rounded-full bg-elevated">
          <span
            class="block h-full rounded-full"
            :class="barColor"
            :style="{ width: `${step.fill}%` }"
          />
        </span>
        <span
          class="w-16 shrink-0 text-right tabular-nums"
          :class="step.done ? 'font-semibold text-default' : 'text-muted'"
        >
          {{ step.camps }} camp{{ step.camps === 1 ? '' : 's' }}
        </span>
      </li>
    </ol>

    <p class="text-[11px] text-dimmed">
      Riot doesn't record which camps were cleared — only how many, once a minute.
    </p>
  </div>
</template>
