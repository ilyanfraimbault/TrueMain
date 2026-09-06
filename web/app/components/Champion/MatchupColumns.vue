<script setup lang="ts">
defineProps<{
  /** Section label sitting over the champion column. */
  label: string
  /** Tone class for that label — best/worst sections colour theirs. */
  labelClass?: string
}>()
</script>

<template>
  <!-- Column captions: two bare percentages side by side are unreadable without
       them, and "lane" vs "game" is exactly the distinction the panel exists to
       make (#919). They belong to every rendering of a row, the single-opponent
       search included — a filtered panel showing "11 games / 73% / 36%" with no
       captions above it is three numbers with no question. Mirrors the row's
       trailing structure — same px-1.5, same gap-3, two w-12 columns — so the
       captions sit over their values. -->
  <div class="flex items-center gap-3 px-1.5">
    <p
      class="flex-1 text-xs font-semibold uppercase tracking-wide"
      :class="labelClass ?? 'text-dimmed'"
    >
      {{ label }}
    </p>
    <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Lane</span>
    <span class="w-12 shrink-0 text-right text-[10px] uppercase tracking-wide text-dimmed">Game</span>
  </div>
</template>
