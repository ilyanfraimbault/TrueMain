<script setup lang="ts">
// A titled panel of the selected player's breakdown (Build tab): laning, per-minute
// stats, build order, skill order — one `surface` card each, under the same
// small uppercase caption.
defineProps<{
  title: string
  icon?: string
}>()
</script>

<template>
  <div class="surface rounded-md p-3">
    <p class="mb-2 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wide text-muted">
      <UIcon v-if="icon" :name="icon" class="size-3.5 text-primary" />
      {{ title }}
    </p>
    <slot />
  </div>
</template>
