<script setup lang="ts">
import { describeFetchError } from '~/utils/errors'

const props = defineProps<{
  /** Raw thrown error. Renders nothing when null/undefined, so callers need no `v-if`. */
  error?: unknown
  title: string
  /** Overrides the line derived from `error`. Reserved for a failure the app diagnoses itself. */
  description?: string
  class?: string
}>()

const description = computed(() => props.description ?? describeFetchError(props.error))
</script>

<template>
  <!--
    The inline surface of the error vocabulary: one *region* of a live page
    failed and the rest is still usable, so the notice sits where the missing
    content was and stays there. Deliberately not paired with a toast — a toast
    disappears, and the message a reader most needs to re-read is the one
    explaining why a panel is empty.

    Backend detail never reaches it: `describeFetchError` maps the status to the
    app's own copy. The admin portal's namesake is the same component for the
    opposite audience — it shows the ProblemDetails `detail` and the traceId,
    because an operator is expected to quote them.
  -->
  <UAlert
    v-if="error != null"
    color="error"
    variant="subtle"
    icon="i-lucide-triangle-alert"
    :title="title"
    :description="description"
    :class="props.class"
  />
</template>
