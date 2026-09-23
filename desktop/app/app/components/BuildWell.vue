<script setup lang="ts">
/**
 * One block of the build: a title, its win rate, and the icons, centred in
 * whatever room the grid gives it — a well is sized by the layout, never by
 * its icons, so a short row does not leave a lopsided gap on one side.
 */
defineProps<{
  title: string
  /** Win rate of this block's own choice, already formatted. */
  rate?: string | null
  /** Content stacks rather than runs in a row — the rune tree, the build tree. */
  vertical?: boolean
}>()
</script>

<template>
  <div class="well flex min-w-0 flex-col rounded-xl p-3">
    <div class="flex items-baseline justify-between gap-3">
      <h3 class="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">{{ title }}</h3>
      <span v-if="rate" class="text-[11px] tabular-nums text-dimmed">{{ rate }}</span>
    </div>
    <div
      class="mt-2 flex flex-1 items-center justify-center gap-1"
      :class="vertical ? 'flex-col' : 'min-h-9'"
    >
      <slot />
    </div>
  </div>
</template>
