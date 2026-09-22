<script setup lang="ts">
import type { EnemyLane, Lane } from '~/types/draft'
import { LANE_ICONS, LANE_LABELS } from '~/types/draft'

const props = defineProps<{
  lanes: EnemyLane[]
  /** Our own lane, so the card we face can be marked. */
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
 * A guess reads as a guess. The matchup, runes and build all rest on this
 * assignment, so presenting it with the weight of a fact would be printing a
 * number we did not measure.
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
  <section class="space-y-3">
    <header class="flex items-center justify-between gap-2">
      <div class="flex items-baseline gap-3">
        <h2 class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Enemy lanes</h2>
        <p class="text-xs text-muted">
          Champion select does not say who plays where — drag a champion onto its lane, or click one then the lane.
        </p>
      </div>
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

    <ul class="grid grid-cols-5 gap-3">
      <li
        v-for="slot in slots"
        :key="slot.lane"
        @dragover.prevent
        @drop.prevent="onDrop(slot.lane)"
      >
        <!--
          The whole card is the control: a button, so the click-then-click path
          is reachable from the keyboard. Drag is the shortcut, not the only way
          in — a five-card trackpad drag under a champion-select timer is slower
          than two clicks, and drag alone cannot be operated without a pointer.
        -->
        <button
          type="button"
          class="block w-full rounded-xl text-left outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--ui-bg)]"
          :draggable="slot.championId !== null"
          :aria-label="`${LANE_LABELS[slot.lane]}: ${slot.championId ? nameOf(slot.championId) : 'empty'}. ${
            selected ? 'Swap with the selected lane' : 'Select to swap'
          }`"
          @click="activate(slot.lane)"
          @dragstart="dragging = slot.lane"
          @dragend="dragging = null"
        >
          <ChampionCard
            :champion-id="slot.championId"
            :gold="slot.lane === myPosition"
            :selected="selected === slot.lane"
            :drop-target="dragging !== null && dragging !== slot.lane"
          >
            <template #top>
              <span
                class="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-semibold uppercase tracking-wide backdrop-blur-sm"
                :class="slot.lane === myPosition ? 'bg-gold/90 text-ink-950' : 'bg-ink-950/70 text-default'"
              >
                <UIcon :name="LANE_ICONS[slot.lane]" class="size-3" />
                {{ slot.lane === myPosition ? 'Your lane' : LANE_LABELS[slot.lane] }}
              </span>
              <UIcon
                v-if="slot.pinned"
                name="i-lucide-pin"
                class="size-3.5 rounded-md bg-ink-950/70 p-0.5 text-primary"
              />
            </template>

            <template #bottom>
              <p class="truncate text-sm font-semibold text-highlighted">
                {{ slot.championId ? nameOf(slot.championId) : 'Not picked' }}
              </p>
              <p class="mt-0.5 text-[11px]" :class="slot.pinned ? 'text-primary' : 'text-muted'">
                {{ confidenceLabel(slot) }}
              </p>
              <!-- The confidence itself, as a meter: a coin flip should look like one. -->
              <div v-if="slot.championId !== null" class="mt-1.5 h-0.5 overflow-hidden rounded-full bg-ink-100/15">
                <div
                  class="h-full rounded-full transition-[width] duration-300"
                  :class="slot.pinned ? 'bg-primary' : 'bg-rosegold-300'"
                  :style="{ width: `${Math.round((slot.pinned ? 1 : slot.confidence) * 100)}%` }"
                />
              </div>
            </template>
          </ChampionCard>
        </button>
      </li>
    </ul>
  </section>
</template>
