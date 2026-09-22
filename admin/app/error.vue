<script setup lang="ts">
import type { NuxtError } from '#app'
import { extractFetchError } from '~/utils/fetch-error'

const props = defineProps<{ error: NuxtError }>()

/**
 * Heading from the status, detail from the error. The public site derives both
 * from the status because a raw `statusMessage` can leak the backend's
 * internals at a visitor; here the opposite holds — Nuxt writes the requested
 * path into a 404's message and a proxied failure carries the ProblemDetails
 * `detail`, and naming the bad route or the real reason is the whole point of
 * an operator-facing error page.
 */
const HEADINGS: Record<number, string> = {
  401: 'Not signed in',
  403: 'Not allowed',
  404: 'No such page',
  503: 'Temporarily unavailable',
}

// `NuxtError.statusCode` is optional in the type even though Nuxt always fills
// it — a thrown non-NuxtError reaches here as `{ message }` alone.
const status = computed(() => props.error.statusCode ?? 500)

const displayed = computed(() => ({
  statusCode: status.value,
  statusMessage: HEADINGS[status.value] ?? 'Something went wrong',
  message: extractFetchError(props.error, 'This page could not be loaded.'),
}))
</script>

<template>
  <!--
    The page surface of the error vocabulary (#1661): the route itself cannot
    resolve. Inside `NuxtLayout` so the sidebar and the ⌘K palette stay up — an
    operator who mistypes a route needs the way out still on screen, and Nuxt's
    stock error page (what the portal fell through to before this file existed)
    has neither.
  -->
  <UApp>
    <NuxtLayout>
      <!--
        `w-full`: `UDashboardGroup` lays the portal out as a flex row, so the
        error `<main>` is a flex item that shrinks to its content. Its theme
        centres with `items-center`, which then centres inside that narrow box
        and leaves the whole block hugging the sidebar.
      -->
      <UError
        class="w-full"
        icon="i-lucide-triangle-alert"
        :error="displayed"
        :clear="{ label: 'Back to the dashboard' }"
      />
    </NuxtLayout>
  </UApp>
</template>
