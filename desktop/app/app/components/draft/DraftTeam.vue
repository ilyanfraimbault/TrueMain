<script setup lang="ts">
import type { Lane, TeamRow } from '~/types/draft'
import { LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * Five picks of one side, left to right, each over the lane it holds. Ours
 * follow the lanes the client assigned; theirs follow the lanes the guesser
 * resolved, and one dragged onto another swaps the two (pinned, so the rest
 * re-solve around the correction).
 */
const props = withDefaults(defineProps<{
  team: 'ally' | 'enemy'
  rows: TeamRow[]
  /** The card whose build is on screen, ringed. */
  selectedCell?: number | null
  /** The selected champion's lane opponent, when it is on this side. */
  opponentCell?: number | null
  /** The cell the simulator is filling. */
  activeCell?: number | null
  /** Portraits to suggest in our own empty slot. */
  suggested?: number[]
  /** Enemy lanes resolved: the cards can be dragged to correct them. */
  correctable?: boolean
}>(), { selectedCell: null, opponentCell: null, activeCell: null, suggested: () => [], correctable: false })

const emit = defineEmits<{ view: [championId: number], swap: [from: Lane, to: Lane] }>()

const { nameOf } = useChampionStatics()
const { entryOf, mainEntryOf } = useTierList()

/** The tier on the lane the card holds; an enemy on no lane yet reads its main lane's. */
function tierOf(row: TeamRow): string | null {
  if (row.championId === null) return null
  return (entryOf(row.championId, row.lane) ?? mainEntryOf(row.championId))?.tier ?? null
}

const dragging = ref<Lane | null>(null)

function onDrop(lane: Lane | null) {
  if (dragging.value && lane && dragging.value !== lane) emit('swap', dragging.value, lane)
  dragging.value = null
}

/**
 * A card opens its champion's build once the draft can place it: our own
 * always, anyone else locked on a lane — a hovered ally or an enemy with no
 * lane yet has no build to read.
 */
const readable = (row: TeamRow) => row.championId !== null && (row.me || (row.locked && row.lane !== null))

function label(row: TeamRow) {
  if (row.championId === null) return undefined
  return `Build for ${nameOf(row.championId)}${row.lane ? `, ${LANE_LABELS[row.lane]}` : ''}`
}
</script>

<template>
  <div class="grid grid-cols-5 gap-1.5">
    <div
      v-for="(row, index) in rows"
      :key="`${team}-${index}`"
      class="flex min-w-0 flex-col items-center gap-1.5"
      @dragover.prevent
      @drop.prevent="onDrop(row.lane)"
    >
      <button
        type="button"
        class="block h-[140px] w-full rounded-lg outline-none transition-[filter] focus-visible:ring-2 focus-visible:ring-primary enabled:cursor-pointer enabled:hover:brightness-110 disabled:cursor-default"
        :disabled="!readable(row)"
        :draggable="correctable && row.championId !== null"
        :aria-label="label(row)"
        @click="emit('view', row.championId!)"
        @dragstart="dragging = row.lane"
        @dragend="dragging = null"
      >
        <DraftCard
          :team="team"
          :champion-id="row.championId"
          :tier="tierOf(row)"
          :lane="row.lane"
          :selected="selectedCell === index"
          :opponent="opponentCell === index"
          :tentative="row.championId !== null && !row.locked"
          :active="activeCell === index"
          :drop-target="dragging !== null && row.lane !== null && dragging !== row.lane"
          :suggested="row.me ? suggested : []"
        />
      </button>

      <!-- The lane, under a placed champion — an empty card already carries it. An enemy lane we are not sure of says so. -->
      <div class="relative flex h-5 items-center justify-center">
        <img
          v-if="row.lane && row.championId !== null"
          :src="laneIconUrl(row.lane)"
          :alt="LANE_LABELS[row.lane]"
          :title="LANE_LABELS[row.lane]"
          class="size-4"
        >
        <span
          v-if="team === 'enemy' && row.championId !== null && row.lane && (row.pinned || (row.confidence ?? 1) < 0.85)"
          class="absolute -right-3 top-0 flex size-3.5 items-center justify-center rounded-full bg-ink-800 text-[9px] font-bold"
          :class="row.pinned ? 'text-primary' : 'text-gold'"
          :title="row.pinned ? 'Set by you' : 'Uncertain lane'"
        >
          <UIcon v-if="row.pinned" name="i-lucide-pin" class="size-2" />
          <template v-else>?</template>
        </span>
      </div>
    </div>
  </div>
</template>
