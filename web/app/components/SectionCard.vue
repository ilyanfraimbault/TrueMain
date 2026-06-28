<script setup lang="ts">
withDefaults(defineProps<{
  title?: string
  /** Ignored when the #title slot is provided. */
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

defineSlots<{
  /**
   * Replaces the default title heading. When this slot is used the card drops
   * its automatic `aria-labelledby` (which targets the default heading), so the
   * `<section>` region is left unnamed unless the slotted content names it — and
   * the `title`/`subtitle` props are ignored. Prefer the `title` prop unless you
   * need custom markup (badge, link) in the heading.
   */
  title?: () => unknown
  /** Card body. */
  default?: () => unknown
  /** Controls shown opposite the title in the header (filters, toggles, links). */
  actions?: () => unknown
}>()

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
