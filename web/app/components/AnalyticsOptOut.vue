<script setup lang="ts">
import { isAnalyticsOptedOut, setAnalyticsOptOut } from '~/utils/analytics-opt-out'

/**
 * The privacy policy's opt-out control (#2089), the "clickable button or link
 * within the privacy policy" the CNIL's consent exemption asks for. The state
 * lives in `localStorage`, which the server cannot read: until mount the switch
 * is disabled rather than rendered with a guess that could flip on hydration.
 */
const ready = ref(false)
const measured = ref(true)

onMounted(() => {
  measured.value = !isAnalyticsOptedOut()
  ready.value = true
})

function onToggle(value: boolean) {
  measured.value = !setAnalyticsOptOut(!value)
}
</script>

<template>
  <div class="flex flex-col gap-1 rounded-lg border border-default p-4">
    <USwitch
      :model-value="measured"
      :disabled="!ready"
      label="Include my visits in the site's audience statistics"
      :description="measured
        ? 'Your visits are counted, anonymously, in the aggregate statistics.'
        : 'Your visits are no longer measured on this browser. This choice is stored on your device only.'"
      @update:model-value="onToggle"
    />
  </div>
</template>
