<script setup lang="ts">
import type { NuxtError } from '#app'
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
import { describeHttpStatus } from '~/utils/errors'

const props = defineProps<{ error: NuxtError }>()

/**
 * The heading is derived from the status and nothing else — the error's own
 * `statusMessage` never reaches the page. Nuxt writes the *path* into it for a
 * 404 ("Page not found: /definitely-not-a-route"), and a failure proxied from
 * the backend can put its technical detail there; either one is a raw string
 * set as an `<h1>`. The status code carries every distinction the page needs,
 * including the route guard's own 404-vs-503 split (`utils/champion-route.ts`).
 */
const HEADINGS: Record<number, string> = {
  404: 'Page not found',
  410: 'This page is gone',
  429: 'Too many requests',
  503: 'Temporarily unavailable',
}

// `NuxtError.statusCode` is optional in the type even though Nuxt always fills
// it — a thrown non-NuxtError reaches here as `{ message }` alone.
const status = computed(() => props.error.statusCode ?? 500)

const displayed = computed(() => ({
  statusCode: status.value,
  statusMessage: HEADINGS[status.value] ?? 'Something went wrong',
  // Same line every inline fetch error in the app reads.
  message: describeHttpStatus(status.value),
}))
</script>

<template>
  <!--
    The page surface of the error vocabulary (#1661): the route itself cannot
    exist or cannot render (404, 503, a fatal SSR throw). It renders in the
    site's own chrome — header, footer, skip link — because the visitor has not
    left the site, and the bare centred panel this replaced told them they had.
  -->
  <UApp>
    <NuxtRouteAnnouncer />
    <AppSkipLink />
    <AppHeader />

    <UError
      :id="MAIN_CONTENT_ID"
      tabindex="-1"
      class="outline-none"
      icon="i-lucide-triangle-alert"
      :error="displayed"
      :clear="{ label: 'Go back home' }"
    />

    <AppFooter />
  </UApp>
</template>
