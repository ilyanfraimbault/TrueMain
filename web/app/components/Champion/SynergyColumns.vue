<script setup lang="ts">
defineProps<{
  /** The page's champion, named in the synergy definition. */
  championName: string
}>()
</script>

<template>
  <!-- Column key. "Synergy" is points of win rate above expectation, so it
       needs naming — a bare signed number would read as a win-rate delta.
       The definition sits in the header's tooltip, not in a footnote. -->
  <div class="flex items-center gap-3 px-1.5 text-[0.65rem] font-semibold uppercase tracking-wide text-dimmed">
    <span
      class="size-8 shrink-0"
      aria-hidden="true"
    />
    <span
      class="size-4 shrink-0"
      aria-hidden="true"
    />
    <span class="min-w-0 flex-1">Partner</span>
    <span class="shrink-0">Sample</span>
    <span class="w-12 shrink-0 text-right">WR</span>
    <span
      class="w-14 shrink-0 text-right"
      :title="`Win rate minus what ${championName}'s and the partner's own win rates predicted, in points.`"
    >Synergy</span>
  </div>
</template>
