<script setup lang="ts">
import { getQueueLabel } from '#common/utils/queues'

// Result and queue of a match row, the column the row drops first when narrow:
// the edge mark and the wash already state the result.
const props = defineProps<{
  win: boolean
  queueId: number
  gameMode?: string
}>()

const queueLabel = computed(() => getQueueLabel(props.queueId, props.gameMode))
</script>

<template>
  <div class="w-20 leading-tight">
    <p class="text-[13px] font-semibold" :class="win ? 'text-sky-300' : 'text-red-400'">
      {{ win ? 'Victory' : 'Defeat' }}
    </p>
    <p class="truncate text-[11px] text-muted" :title="queueLabel">{{ queueLabel }}</p>
  </div>
</template>
