<script setup lang="ts">
import { extractFetchError, extractFetchErrorTraceId } from '~/utils/fetch-error'

const props = defineProps<{
  /** Raw thrown error — message/traceId are derived from it unless overridden below. */
  error?: unknown
  title: string
  fallback?: string
  /** Overrides the message derived from `error` (e.g. a pre-built 404 message from useDeepLinkedDetail). */
  message?: string
  /** Overrides the traceId derived from `error`. */
  traceId?: string
  class?: string
}>()

const message = computed(() => props.message ?? extractFetchError(props.error, props.fallback))
const traceId = computed(() => props.traceId ?? extractFetchErrorTraceId(props.error))
const visible = computed(() => props.error != null || props.message != null)
</script>

<template>
  <UAlert
    v-if="visible"
    color="error"
    variant="subtle"
    icon="i-lucide-triangle-alert"
    :title="title"
    :class="props.class"
  >
    <template #description>
      <p>{{ message }}</p>
      <!-- Ops-only detail: lets an operator match a reported failure to the
           backend log line that produced it (Program.cs's CustomizeProblemDetails). -->
      <p v-if="traceId" class="mt-1 font-mono text-xs text-muted">
        Trace ID: {{ traceId }}
      </p>
    </template>
  </UAlert>
</template>
