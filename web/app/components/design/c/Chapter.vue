<script setup lang="ts">
/**
 * Design exploration, variant C — a chapter opener: a ruled HUD line (chapter
 * number, kicker, an optional real figure on the right), then a display title
 * whose lines rise out of their masks as it scrolls into view, over a giant
 * outlined chapter numeral that drifts at its own depth.
 */
defineProps<{
  index: string
  kicker: string
  lines: string[]
  /** Right-hand HUD text — real data only (a patch, a count), or nothing. */
  meta?: string | null
  id?: string
}>()
</script>

<template>
  <header class="c-chapter">
    <span
      class="c-chapter__numeral"
      aria-hidden="true"
    >{{ index }}</span>
    <div
      v-reveal="{ effect: 'c-draw' }"
      class="c-chapter__rule"
    >
      <span class="c-mono c-chapter__kicker"><b>{{ index }}</b><i aria-hidden="true" />{{ kicker }}</span>
      <span
        class="c-chapter__ticks"
        aria-hidden="true"
      />
      <span
        v-if="meta"
        class="c-mono"
      >{{ meta }}</span>
    </div>
    <h2
      :id="id"
      v-reveal.stagger="{ effect: 'c-mask', step: 110 }"
      class="c-chapter__title"
    >
      <span
        v-for="(line, i) in lines"
        :key="i"
        class="c-mask"
      ><span>{{ line }}{{ i < lines.length - 1 ? ' ' : '' }}</span></span>
    </h2>
    <div
      v-if="$slots.default"
      v-reveal="{ delay: 260, effect: 'blur' }"
      class="c-chapter__lede"
    >
      <slot />
    </div>
  </header>
</template>
