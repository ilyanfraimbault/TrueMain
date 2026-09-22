<script setup lang="ts">
import type { EnemyLane, Lane } from '~/types/draft'
import { LANE_ICONS, LANE_LABELS } from '~/types/draft'

const props = defineProps<{
  lanes: EnemyLane[]
  /** Our own lane, so the row we face can be marked. */
  myPosition: string
}>()

const emit = defineEmits<{ 'update:pinned': [Record<number, string>] }>()

const lanes = toRef(props, 'lanes')
const { slots, pinned, selected, hasCorrections, swap, activate, reset } = useLaneAssignment(lanes)
const { nameOf } = useChampionStatics()

watch(pinned, value => emit('update:pinned', value), { deep: true })

const dragging = ref<Lane | null>(null)

function onDrop(lane: Lane) {
  if (dragging.value) swap(dragging.value, lane)
  dragging.value = null
}

/**
 * A guess reads as a guess. The matchup, runes and build below all rest on this
 * assignment, so presenting it with the same weight as a fact would be printing
 * a number we did not measure.
 */
function confidenceLabel(slot: { championId: number | null, confidence: number, pinned: boolean }) {
  if (slot.championId === null) return 'Not picked yet'
  if (slot.pinned) return 'Set by you'
  if (slot.confidence >= 0.85) return 'Likely'
  if (slot.confidence >= 0.6) return 'Uncertain'
  return 'Coin flip'
}
</script>

<template>
  <section class="space-y-2">
    <header class="flex items-center justify-between gap-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">
        Enemy lanes
      </h2>
      <UButton
        v-if="hasCorrections"
        size="xs"
        variant="ghost"
        color="neutral"
        icon="i-lucide-rotate-ccw"
        label="Reset"
        @click="reset"
      />
    </header>

    <ul class="space-y-1">
      <li
        v-for="slot in slots"
        :key="slot.lane"
        class="flex items-center gap-3 rounded-md px-2 py-1.5 ring-1 ring-inset transition-colors"
        :class="[
          slot.lane === myPosition ? 'bg-elevated ring-accented' : 'ring-transparent',
          selected === slot.lane && 'ring-primary',
          dragging && dragging !== slot.lane && 'ring-dashed ring-accented',
        ]"
        @dragover.prevent
        @drop.prevent="onDrop(slot.lane)"
      >
        <UIcon :name="LANE_ICONS[slot.lane]" class="size-4 shrink-0 text-dimmed" />
        <span class="w-16 shrink-0 text-xs text-muted">{{ LANE_LABELS[slot.lane] }}</span>

        <!--
          The whole row is the control: a button, so the click-then-click path
          is reachable from the keyboard. Drag is the shortcut, not the only way
          in — a five-row trackpad drag under a champion-select timer is slower
          than two clicks, and drag alone cannot be operated without a pointer.
        -->
        <button
          type="button"
          class="flex flex-1 items-center gap-2 text-left"
          :draggable="slot.championId !== null"
          :aria-label="`${LANE_LABELS[slot.lane]}: ${slot.championId ? nameOf(slot.championId) : 'empty'}. ${
            selected ? 'Swap with the selected lane' : 'Select to swap'
          }`"
          @click="activate(slot.lane)"
          @dragstart="dragging = slot.lane"
          @dragend="dragging = null"
        >
          <ChampionPortrait :champion-id="slot.championId" size="sm" />
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm text-default">
              {{ slot.championId ? nameOf(slot.championId) : '—' }}
            </span>
            <span
              class="block text-[11px]"
              :class="slot.pinned ? 'text-primary' : 'text-dimmed'"
            >
              {{ confidenceLabel(slot) }}
            </span>
          </span>
          <UIcon
            v-if="slot.pinned"
            name="i-lucide-pin"
            class="size-3.5 shrink-0 text-primary"
          />
        </button>
      </li>
    </ul>

    <p class="text-[11px] text-dimmed">
      Champion select does not say who plays where. Drag a champion onto its lane —
      or click one, then the lane — and the rest re-solve around it.
    </p>
  </section>
</template>
