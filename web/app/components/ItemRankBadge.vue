<script setup lang="ts">
// Small glass pill overlaid on an icon's bottom-centre — the skill key
// (Q/W/E/R) on a spell icon, or the slot number on a build-path item. Lives
// at the top level so it auto-registers as <ItemRankBadge>. Positioned
// absolutely, so it expects a `relative`-positioned parent.
defineProps<{
  /** Badge label — a skill key (Q/W/E/R) or an item-slot number. */
  value: string | number
}>()
</script>

<template>
  <span class="absolute bottom-0 left-1/2 inline-flex h-3 min-w-3 -translate-x-1/2 items-center justify-center rounded bg-default/85 px-0.5 text-[8px] font-bold uppercase ring-1 ring-default backdrop-blur-sm">
    {{ value }}
  </span>
</template>
