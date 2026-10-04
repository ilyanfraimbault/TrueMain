<script setup lang="ts">
const props = defineProps<{
  /** The failed refetch. Renders nothing when null/undefined, so callers need no `v-if`. */
  error?: unknown
  /** What the stale content is, in the reader's words: "the previous champion list". */
  subject: string
  /** Re-runs the failed request; the button is left out when absent. */
  onRetry?: () => unknown
}>()
</script>

<template>
  <!--
    The other half of a failed refetch (#1668): the action toast answered the
    click, this stays put and says the content under it is not what the filters
    now ask for. Deliberately not a second `FetchErrorAlert` — the content is
    still there and readable, so the notice is a muted line, not an error block
    standing in for a region.
  -->
  <p
    v-if="props.error != null"
    role="status"
    class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted"
  >
    <UIcon
      name="i-lucide-history"
      class="size-4 shrink-0 text-warning"
      aria-hidden="true"
    />
    <span>Couldn't update — showing {{ props.subject }}.</span>
    <UButton
      v-if="props.onRetry"
      size="xs"
      color="neutral"
      variant="link"
      icon="i-lucide-refresh-cw"
      label="Retry"
      class="p-0"
      @click="props.onRetry()"
    />
  </p>
</template>
