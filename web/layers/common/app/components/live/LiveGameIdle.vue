<script setup lang="ts">
import type { LiveGameStatus } from '#shared/types/live'
import { formatRelativeTime } from '#common/utils/relativeTime'

// The Live tab when there is no game to draw (#2103): the player is not in one,
// or Riot could not be read — two different answers, so two different copies.
// Either way the reader gets "Check now" and how old the answer is.
const props = defineProps<{
  status: Exclude<LiveGameStatus, 'inGame'> | 'error'
  /** When Riot was last asked; null when the request itself failed. */
  checkedAtUtc: string | null
  /** A check is in flight. */
  checking?: boolean
  /** The clock "last checked" is measured against. */
  now: Date
}>()

defineEmits<{ check: [] }>()

const copy = computed(() => props.status === 'notInGame'
  ? { icon: 'i-lucide-moon', title: 'Not in a game', description: 'This player is not in a game right now.' }
  : { icon: 'i-lucide-cloud-off', title: 'Live game unavailable', description: 'Riot could not be reached just now — try again in a few seconds.' })

const checkedLabel = computed(() =>
  props.checkedAtUtc ? `Last checked ${formatRelativeTime(props.checkedAtUtc, props.now)}` : null)
</script>

<template>
  <UEmpty
    :icon="copy.icon"
    :title="copy.title"
    :description="copy.description"
    class="glass rounded-xl"
  >
    <template #actions>
      <div class="flex flex-col items-center gap-2">
        <UButton
          icon="i-lucide-refresh-cw"
          label="Check now"
          color="primary"
          variant="soft"
          :loading="checking"
          @click="$emit('check')"
        />
        <span
          v-if="checkedLabel"
          class="text-xs text-dimmed"
        >{{ checkedLabel }}</span>
      </div>
    </template>
  </UEmpty>
</template>
