<script setup lang="ts">
withDefaults(defineProps<{
  title?: string
  subtitle?: string
  /**
   * Heading level for the default title. Page-level sections pass `2` so the
   * document outline stays h1 → h2 → h3; nested cards (build variations) keep
   * the `3` default.
   */
  level?: 2 | 3 | 4
}>(), {
  level: 3,
})

// Render the card as a labelled <section> so AT users can navigate by region.
// The id links the section to its heading (aria-labelledby) — only emitted when
// there's a title to point at, so a section without one isn't a nameless region.
const headingId = useId()
</script>

<template>
  <UCard
    as="section"
    :aria-labelledby="title && !$slots.title ? headingId : undefined"
  >
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex min-w-0 flex-col gap-0.5">
          <slot name="title">
            <component
              v-if="title"
              :is="`h${level}`"
              :id="headingId"
              class="text-sm font-medium text-default"
            >
              {{ title }}
            </component>
          </slot>
          <p
            v-if="subtitle && !$slots.title"
            class="text-xs text-muted"
          >
            {{ subtitle }}
          </p>
        </div>
        <slot name="actions" />
      </div>
    </template>
    <slot />
  </UCard>
</template>
