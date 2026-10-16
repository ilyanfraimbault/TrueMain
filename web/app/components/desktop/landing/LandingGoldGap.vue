<script setup lang="ts">
// The item-value panel's gap, as the app's `OverlayGoldGap` draws it: a chevron toward the side ahead, the gap under it.
defineProps<{ gap: number, large?: boolean }>()
</script>

<template>
  <div class="flex min-w-14 flex-col items-center leading-none">
    <UIcon
      :name="gap > 0 ? 'i-lucide-chevron-left' : gap < 0 ? 'i-lucide-chevron-right' : 'i-lucide-minus'"
      :class="[gap > 0 ? 'text-ally' : gap < 0 ? 'text-enemy' : 'text-dimmed', large ? 'size-3.5' : 'size-3']"
    />
    <span
      class="mt-0.5 font-semibold tabular-nums"
      :class="[large ? 'text-xs' : 'text-[10px]', gap > 0 ? 'text-ally' : gap < 0 ? 'text-enemy' : 'text-dimmed']"
    >{{ formatCount(Math.abs(gap)) }}</span>
  </div>
</template>
