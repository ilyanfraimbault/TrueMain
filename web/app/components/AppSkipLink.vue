<script setup lang="ts">
import { MAIN_CONTENT_ID, focusMainContent } from '~/utils/route-focus'

/**
 * "Skip to content" (#1616, WCAG 2.4.1): the first tab stop of every page, so
 * a keyboard reader does not tab through the whole header on every route.
 *
 * Off-screen until focused, then it drops in at the top-left corner, above the
 * sticky header, dressed as a solid primary button — focus is interaction, which
 * is what the accent is for. A plain `<a>` rather than `UButton`: the button
 * would render a `NuxtLink` and turn the fragment into a router navigation.
 * The `href` keeps it working before hydration; once hydrated the click moves
 * focus directly, so no `#main-content` lands in the URL or the history.
 * `preventScroll`: scrolling `<main>` into view would align its top with the
 * viewport's, under the sticky header; the next Tab scrolls to its own target.
 */
function skip(event: MouseEvent) {
  if (focusMainContent({ preventScroll: true })) event.preventDefault()
}
</script>

<template>
  <a
    :href="`#${MAIN_CONTENT_ID}`"
    class="fixed top-3 left-3 z-[100] -translate-y-[calc(100%+1rem)] rounded-md bg-primary px-3 py-2 text-sm font-medium text-inverted shadow-lg transition-transform duration-150 focus:translate-y-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary motion-reduce:transition-none"
    @click="skip"
  >
    Skip to content
  </a>
</template>
