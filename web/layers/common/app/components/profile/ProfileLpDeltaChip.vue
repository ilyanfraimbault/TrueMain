<script setup lang="ts">
// One LP delta: its window, the direction, and the LP moved.
defineProps<{
  label: string
  delta: number
}>()
</script>

<template>
  <span class="inline-flex min-w-0 items-center justify-center gap-1 whitespace-nowrap rounded-md bg-elevated px-1.5 py-1 text-[11px]">
    <span class="text-muted">{{ label }}</span>
    <UIcon
      :name="delta >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
      class="size-3 shrink-0"
      :class="delta >= 0 ? 'text-data-good' : 'text-data-bad'"
    />
    <span class="font-semibold tabular-nums text-default">
      {{ Math.abs(delta) }} LP
    </span>
  </span>
</template>
