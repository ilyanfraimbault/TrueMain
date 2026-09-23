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
 * Shown on every page change, fast ones included: the outgoing page stays on
 * screen until the destination has its data, so the bar is the only sign the
 * click was taken. Nuxt's 200 ms throttle hid it on most navigations — they
 * land between 100 and 400 ms, and a busy main thread fires the timer late —
 * and the 400 ms fade-in left the rest barely visible. The 50 ms throttle
 * stays for one case only: a query-only navigation (filters, pagers) finishes
 * within a tick and must not flash the bar while its own skeleton is loading.
 * It appears at full opacity and only fades on the way out.
 *
 * Decorative for assistive tech: `NuxtRouteAnnouncer` already announces the
 * page change.
 */
const { progress, isLoading, error } = useLoadingIndicator({ throttle: 50 })
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-x-0 top-(--ui-header-height) z-50 h-0.5 origin-left"
    :class="error ? 'bg-error' : 'bg-primary'"
    :style="{
      opacity: isLoading ? 1 : 0,
      transform: `scaleX(${progress / 100})`,
      transition: isLoading
        ? 'transform 100ms linear'
        : 'transform 100ms linear, opacity 400ms ease-out',
    }"
  />
</template>
