<script setup lang="ts">
/**
 * Navigation progress bar pinned under the sticky header (#1689).
 *
 * Nuxt's own `<NuxtLoadingIndicator>` draws the same bar but pins it to the
 * viewport's top edge with inline styles, above the header; this one reads the
 * same `useLoadingIndicator` singleton — started on `page:loading:start`,
 * finished once the destination page has resolved what it awaits in setup —
 * and sits on the header's bottom edge instead.
 *
 * Decorative for assistive tech: `NuxtRouteAnnouncer` already announces the
 * page change.
 */
const { progress, isLoading, error } = useLoadingIndicator()
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-x-0 top-(--ui-header-height) z-50 h-0.5 origin-left"
    :class="error ? 'bg-error' : 'bg-primary'"
    :style="{
      opacity: isLoading ? 1 : 0,
      transform: `scaleX(${progress / 100})`,
      transition: 'transform 100ms linear, opacity 400ms ease-out',
    }"
  />
</template>
