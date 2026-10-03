<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { Lane, TeamRow } from '~/types/draft'
import { LANES, LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * Five picks of one side, left to right, each over the lane it holds. Ours
 * follow the lanes the client assigned; theirs follow the lanes the guesser
 * resolved, and the player corrects one by clicking its lane icon and picking
 * the lane it really plays — or by dragging it onto another — which swaps the
 * two (pinned, so the rest re-solve around the correction). A guessed lane
 * carries no doubt mark: it is simply the one to correct when it is wrong.
 */
const props = withDefaults(defineProps<{
  team: 'ally' | 'enemy'
  rows: TeamRow[]
  /** The card whose build is on screen, ringed. */
  selectedCell?: number | null
  /** The selected champion's lane opponent, when it is on this side. */
  opponentCell?: number | null
  /** Portraits to suggest in our own empty slot. */
  suggested?: number[]
  /** Enemy lanes resolved: the cards can be dragged to correct them. */
  correctable?: boolean
}>(), { selectedCell: null, opponentCell: null, suggested: () => [], correctable: false })

const emit = defineEmits<{ view: [championId: number], swap: [from: Lane, to: Lane] }>()

const { nameOf } = useChampionStatics()
const { entryOf, mainEntryOf } = useTierList()

/** The tier on the lane the card holds; an enemy on no lane yet reads its main lane's. */
function tierOf(row: TeamRow): string | null {
  if (row.championId === null) return null
  return (entryOf(row.championId, row.lane) ?? mainEntryOf(row.championId))?.tier ?? null
}

const dragging = ref<Lane | null>(null)

/** WebKit starts no drag that carries no data, so the lane rides along as text. */
function onDragStart(event: DragEvent, lane: Lane | null) {
  dragging.value = lane
  if (!event.dataTransfer || !lane) return
  event.dataTransfer.effectAllowed = 'move'
  event.dataTransfer.setData('text/plain', lane)
}

function onDragOver(event: DragEvent) {
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
}

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

/** The lanes an enemy can be moved to, the one it holds marked. */
function laneChoices(row: TeamRow): DropdownMenuItem[] {
  return LANES.map(lane => ({
    label: LANE_LABELS[lane],
    avatar: { src: laneIconUrl(lane), alt: '' },
    type: 'checkbox' as const,
    checked: lane === row.lane,
    onSelect: () => {
      if (row.lane && lane !== row.lane) emit('swap', row.lane, lane)
    },
  }))
}

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
      @dragover.prevent="onDragOver"
      @drop.prevent="onDrop(row.lane)"
    >
      <button
        type="button"
        class="block h-[140px] w-full rounded-lg outline-none transition-[filter] focus-visible:ring-2 focus-visible:ring-primary enabled:cursor-pointer enabled:hover:brightness-110 disabled:cursor-default"
        :disabled="!readable(row)"
        :draggable="correctable && row.championId !== null"
        :aria-label="label(row)"
        @click="emit('view', row.championId!)"
        @dragstart="onDragStart($event, row.lane)"
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
          :drop-target="dragging !== null && row.lane !== null && dragging !== row.lane"
          :suggested="row.me ? suggested : []"
        />
      </button>

      <!-- The lane, under a placed champion — an empty card already carries it. An enemy's is a menu to correct it. -->
      <div class="flex h-5 items-center justify-center">
        <UDropdownMenu
          v-if="correctable && row.lane && row.championId !== null"
          :items="laneChoices(row)"
          :content="{ align: 'center' }"
          :ui="{ itemLeadingAvatar: 'size-4 rounded-none bg-transparent' }"
        >
          <button
            type="button"
            class="flex size-6 items-center justify-center rounded transition-colors hover:bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            :aria-label="`${nameOf(row.championId)} plays ${LANE_LABELS[row.lane]} — change`"
            :title="`${LANE_LABELS[row.lane]} — click to change`"
          >
            <img :src="laneIconUrl(row.lane)" alt="" class="size-4">
          </button>
        </UDropdownMenu>
        <img
          v-else-if="row.lane && row.championId !== null"
          :src="laneIconUrl(row.lane)"
          :alt="LANE_LABELS[row.lane]"
          :title="LANE_LABELS[row.lane]"
          class="size-4"
        >
      </div>
    </div>
  </div>
</template>
