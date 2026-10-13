<script setup lang="ts">
// The ordinal of a `LeaderboardRow`.
//
// It sits in a fixed slot so the ordinals line up down the list, which means a
// long ordinal has to fit the slot rather than the slot fitting the ordinal —
// `#172` was already spilling out of it and over the avatar. Both the slot and
// the label size come from the digit count of the *list's* deepest ordinal,
// not this row's: a page that straddles a decade boundary (76…100) would
// otherwise mix two slot widths and unalign the columns it was supposed to
// hold still. The slot only grows where it has to (the champion-page sidebar
// lists a top ten, so it keeps the tight two-digit slot that leaves its width
// to the Riot ID), and past four digits the label steps down instead of
// widening the column further.
const props = defineProps<{
  rank: number
  /** Deepest ordinal in the list; falls back to this row's own. */
  maxRank?: number
}>()

const slotClass = computed(() => {
  const digits = String(Math.max(props.maxRank ?? 0, props.rank)).length
  switch (digits) {
    case 1:
    case 2:
      return 'w-6 text-xs @xl:w-8 @xl:text-sm'
    case 3:
      return 'w-8 text-xs @xl:w-10 @xl:text-sm'
    case 4:
      return 'w-9 text-[11px] @xl:w-10 @xl:text-xs'
    case 5:
      return 'w-10 text-[10px] @xl:w-12 @xl:text-xs'
    default:
      return 'w-10 text-[8px] @xl:w-12 @xl:text-[10px]'
  }
})
</script>

<template>
  <span
    class="shrink-0 text-center font-semibold tabular-nums leading-none text-muted"
    :class="slotClass"
  >
    #{{ rank }}
  </span>
</template>
