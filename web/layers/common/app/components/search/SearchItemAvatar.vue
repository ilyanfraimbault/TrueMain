<script setup lang="ts">
// A search palette row's picture — a champion's portrait or a player's icon —
// in `AppSearch` and the builder's `ChampionSlot`. SkeletonImage rather than
// the palette's built-in UAvatar: the palette re-fills the same row DOM as the
// query narrows, so a plain <img> keeps painting the previous entry until the
// new picture decodes (type "kai" fast and Aatrox's icon stays next to Kai'Sa's
// name); SkeletonImage blanks to a skeleton on every `src` change. `size-5` is
// the palette's own 2xs avatar box.
defineProps<{ src: string | null | undefined }>()
</script>

<template>
  <SkeletonImage
    :src="src"
    :alt="''"
    class="size-5 shrink-0 rounded-full"
  />
</template>
