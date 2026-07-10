<script setup lang="ts">
// Small glass pill overlaid on an icon's bottom-centre — the skill key
// (Q/W/E/R) on a spell icon, or the slot number on a build-path item. Lives
// at the top level so it auto-registers as <ItemRankBadge>. Positioned
// absolutely, so it expects a `relative`-positioned parent.
withDefaults(defineProps<{
  /** Badge label — a skill key (Q/W/E/R) or an item-slot number. */
  value: string | number
  /** `sm` (default, h-3) for compact rows; `md` (h-4) for larger icon tiles. */
  size?: 'sm' | 'md'
  /** Emerald (primary) emphasis instead of the neutral glass pill. */
  primary?: boolean
}>(), {
  size: 'sm',
  primary: false,
})
</script>

<template>
  <span
    class="absolute bottom-0 left-1/2 inline-flex -translate-x-1/2 items-center justify-center rounded font-bold uppercase ring-1 backdrop-blur-sm"
    :class="[
      size === 'md' ? 'h-4 min-w-4 px-1 text-[10px]' : 'h-3 min-w-3 px-0.5 text-[8px]',
      primary ? 'bg-primary text-inverted ring-primary' : 'bg-default/85 text-default ring-default',
    ]"
  >
    {{ value }}
  </span>
</template>
