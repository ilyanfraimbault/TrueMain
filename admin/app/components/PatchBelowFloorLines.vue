<script setup lang="ts">
// The "still below the floor" section of one patch card on `/patch-coverage`.
//
// It exists to give a thin patch a **cause** — "four lines short, here is which ones" —
// so it names only lines an operator can act on: `belowFloor` (and `belowFloorCount`)
// carry the champions' PRIMARY lanes for that patch, never the off-role tail (#1442).
// An off-role line is below the floor because nobody plays the champion there, and no
// amount of ingestion will move it.
//
// The tail is not hidden, it is counted: `lines - linesPastFloor` is every below-floor
// line, so the difference against `belowFloorCount` is the off-role remainder, and it is
// printed as a sentence. When that remainder is the whole population — the normal state
// of a healthy patch — the sentence carries the section on its own: an empty list under
// "146 lines below the floor" reads as a bug, and "no champion is short of games on its
// own lane" is the answer.
import type { PatchCoverageRow } from '~~/shared/types/ops'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { formatNumber } from '~~/shared/utils/format'

const { patch } = defineProps<{ patch: PatchCoverageRow }>()

const { nameFor, iconFor } = useChampionStatic()

const totalBelowFloor = computed(() => Math.max(0, patch.lines - patch.linesPastFloor))
const offRole = computed(() => Math.max(0, totalBelowFloor.value - patch.belowFloorCount))

/** The header's suffix: how many lines the list is about, and how many of them it shows. */
const note = computed(() => {
  const shown = patch.belowFloor.length < patch.belowFloorCount
    ? `, showing ${formatNumber(patch.belowFloor.length)}`
    : ''
  return `— ${formatNumber(patch.belowFloorCount)} line(s) on the champion's own lane, closest first${shown}`
})

const offRoleNote = computed(() =>
  patch.belowFloorCount === 0
    ? `All ${formatNumber(offRole.value)} line(s) below the floor are on a lane their champion is `
      + 'not played on. No champion is short of games on its own lane.'
    : `${formatNumber(offRole.value)} more line(s) sit below the floor on a lane their champion is `
      + 'not played on — short of games because nobody picks it there, not because the patch is.',
)
</script>

<template>
  <template v-if="totalBelowFloor">
    <USeparator class="my-4" />
    <p class="mb-2 text-xs text-muted uppercase">
      Still below the floor
      <span v-if="patch.belowFloor.length" class="normal-case text-dimmed">{{ note }}</span>
    </p>
    <ul v-if="patch.belowFloor.length" class="grid gap-1.5 sm:grid-cols-2">
      <li
        v-for="line in patch.belowFloor"
        :key="`${line.championId}-${line.position}`"
        class="flex items-center justify-between gap-2 text-xs"
      >
        <span class="flex min-w-0 items-center gap-2">
          <img
            v-if="iconFor(line.championId)"
            :src="iconFor(line.championId)!"
            :alt="nameFor(line.championId)"
            class="size-5 shrink-0 rounded"
            loading="lazy"
          >
          <span class="truncate text-highlighted">{{ nameFor(line.championId) }}</span>
          <!-- Lane as the icon the rest of the portal draws it with, the text kept beside
               it: the glyph alone is unreadable at this size for anyone who does not already
               know the set, and an unknown lane has no glyph at all. -->
          <span class="flex shrink-0 items-center gap-1 text-dimmed">
            <img
              v-if="POSITION_BY_VALUE.has(line.position)"
              :src="POSITION_BY_VALUE.get(line.position)!.iconUrl"
              :alt="POSITION_BY_VALUE.get(line.position)!.label"
              class="size-3.5 shrink-0"
              loading="lazy"
            >
            {{ line.position }}
          </span>
        </span>
        <span class="shrink-0 tabular-nums text-muted">
          {{ line.games }} · {{ line.gamesToFloor }} short
        </span>
      </li>
    </ul>
    <p
      v-if="offRole"
      class="text-xs text-dimmed"
      :class="patch.belowFloor.length ? 'mt-2' : ''"
    >
      {{ offRoleNote }}
    </p>
  </template>
</template>
