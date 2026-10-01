<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'
import type { RecordingsEmptyReason } from '~/utils/recording-library'
import { RESOLUTION_LABELS, formatBytes } from '~/utils/recording-library'

/**
 * Why the Recordings page has nothing to list, and the one thing to do about
 * it: capture not allowed or not available, recording off (with what turning
 * it on costs, from the shell's own estimate), nothing recorded yet, or
 * nothing matching the filters.
 */
const props = defineProps<{ reason: RecordingsEmptyReason }>()
const emit = defineEmits<{ clear: [], settings: [] }>()

const { settings, status, saveSettings, requestPermission } = useRecordings()

const hourCost = computed(() => {
  const view = settings.value
  if (!view) return null
  const { resolution, frameRate } = view.settings.quality
  const estimate = view.estimates.find(entry => entry.resolution === resolution && entry.frameRate === frameRate)
  const preset = `${RESOLUTION_LABELS[resolution]} at ${frameRate} fps`
  return estimate?.bytesPerHour
    ? `About ${formatBytes(estimate.bytesPerHour)} an hour at ${preset} (an estimate), within a ${formatBytes(view.settings.budgetBytes)} budget that drops the oldest games you did not keep.`
    : `Recordings stay within a ${formatBytes(view.settings.budgetBytes)} budget that drops the oldest games you did not keep.`
})

function turnOn() {
  if (settings.value) void saveSettings({ ...settings.value.settings, enabled: true })
}

const content = computed<{ icon: string, title: string, description: string, actions: ButtonProps[] }>(() => {
  const settingsButton: ButtonProps = { label: 'Settings', icon: 'i-lucide-settings-2', color: 'neutral', variant: 'outline', onClick: () => emit('settings') }
  switch (props.reason) {
    case 'unavailable':
      return { icon: 'i-lucide-video-off', title: 'Recording is not part of this build', description: 'This version of the app cannot record games yet.', actions: [] }
    case 'permission':
      return {
        icon: 'i-lucide-shield-alert',
        title: 'TrueMain needs to record your screen',
        description: `${status.value?.message ?? 'Screen Recording is not allowed for TrueMain.'} macOS asks once; after an update it may ask again.`,
        actions: [{ label: 'Allow Screen Recording', icon: 'i-lucide-monitor', onClick: () => void requestPermission() }],
      }
    case 'unsupported':
      return { icon: 'i-lucide-monitor-x', title: 'Recording is not available here yet', description: status.value?.message ?? 'Recording works on macOS for now; Windows comes next.', actions: [] }
    case 'missing':
      return { icon: 'i-lucide-triangle-alert', title: 'The recorder is missing', description: `${status.value?.message ?? 'Part of the app is missing.'} Reinstalling the app brings it back.`, actions: [] }
    case 'off':
      return {
        icon: 'i-lucide-video',
        title: 'Record your games',
        description: `Rewatch every game with your kills, deaths and assists on its timeline, and cut clips from it. ${hourCost.value ?? ''}`,
        actions: [{ label: 'Turn recording on', icon: 'i-lucide-circle-dot', onClick: turnOn }, settingsButton],
      }
    case 'nothing':
      return {
        icon: 'i-lucide-clapperboard',
        title: 'Nothing recorded yet',
        description: settings.value?.settings.queues === 'all'
          ? 'Your next game is recorded, and its recap opens when it ends.'
          : 'Your next ranked game is recorded, and its recap opens when it ends.',
        actions: [settingsButton],
      }
    default:
      return {
        icon: 'i-lucide-search-x',
        title: 'Nothing matches',
        description: 'No clip or full game fits these filters.',
        actions: [{ label: 'Clear filters', icon: 'i-lucide-x', color: 'neutral', variant: 'outline', onClick: () => emit('clear') }],
      }
  }
})
</script>

<template>
  <UEmpty
    :icon="content.icon"
    :title="content.title"
    :description="content.description"
    :actions="content.actions"
    variant="naked"
    class="mx-auto max-w-lg py-16"
  />
</template>
