<script setup lang="ts">
import type { GameItem } from '~/types/game'

/**
 * One player's inventory as the in-game scoreboard lays it out: the six
 * slots in slot order, then the trinket, round. A stack (potions, control
 * wards) carries its count. An item that just landed in a slot is ringed for
 * a few seconds, so a purchase reads as one on a board that is otherwise
 * still. `mirrored` puts the trinket on the inner side for the right-hand team.
 */
const props = withDefaults(defineProps<{
  items: GameItem[]
  mirrored?: boolean
  size?: number
}>(), { mirrored: false, size: 26 })

const { items: statics } = useStaticData()

const INVENTORY = [0, 1, 2, 3, 4, 5]
const TRINKET = 6
/** How long a new item stays ringed. */
const FRESH_MS = 4000

const bySlot = computed(() => new Map(props.items.map(item => [item.slot, item])))

const fresh = ref(new Set<number>())
let clear: ReturnType<typeof setTimeout> | undefined

watch(() => props.items, (next, previous) => {
  if (!previous) return
  const before = new Map(previous.map(item => [item.slot, item.itemId]))
  const landed = next.filter(item => before.get(item.slot) !== item.itemId).map(item => item.slot)
  if (landed.length === 0) return
  fresh.value = new Set(landed)
  clearTimeout(clear)
  clear = setTimeout(() => (fresh.value = new Set()), FRESH_MS)
})
onBeforeUnmount(() => clearTimeout(clear))

const cell = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))
/** The trinket stands a little apart from the six, on the side away from the player. */
const trinketGap = computed(() => (props.mirrored ? 'mr-0.5' : 'ml-0.5'))
</script>

<template>
  <div class="flex shrink-0 items-center gap-1" :class="mirrored && 'flex-row-reverse'">
    <template v-for="slot in [...INVENTORY, TRINKET]" :key="slot">
      <div
        v-if="bySlot.get(slot)"
        class="relative shrink-0 transition-shadow duration-500"
        :class="[slot === TRINKET ? `rounded-full ${trinketGap}` : 'rounded', fresh.has(slot) ? 'ring-2 ring-primary shadow-[0_0_12px_-2px_var(--color-rosegold-400)]' : 'ring-1 ring-white/5']"
        :style="cell"
      >
        <GameTooltipItemIcon
          :item="statics[bySlot.get(slot)!.itemId] ?? null"
          :width="size"
          :height="size"
          class="size-full"
          :class="slot === TRINKET ? 'rounded-full' : 'rounded'"
        />
        <span
          v-if="bySlot.get(slot)!.count > 1"
          class="pointer-events-none absolute -bottom-1 -right-1 rounded-sm bg-default px-0.5 text-[9px] font-bold leading-3 tabular-nums text-highlighted"
        >{{ bySlot.get(slot)!.count }}</span>
      </div>
      <div
        v-else
        class="shrink-0 bg-white/5"
        :class="slot === TRINKET ? `rounded-full ${trinketGap}` : 'rounded'"
        :style="cell"
        aria-hidden="true"
      />
    </template>
  </div>
</template>
