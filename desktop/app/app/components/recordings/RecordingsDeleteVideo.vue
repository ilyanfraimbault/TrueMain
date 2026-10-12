<script setup lang="ts">
import type { Clip } from '~/types/recordings'
import { formatBytes } from '~/utils/recording-library'

/**
 * "Remove from this PC" for a clip published on YouTube (#2005): its video
 * file goes, the clip stays in the library — thumbnail, title, K/D/A — and
 * plays from YouTube. Asked once, since the file does not come back.
 */
const props = defineProps<{ clip: Pick<Clip, 'id' | 'title' | 'sizeBytes'> }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ deleted: [] }>()

const { deleteVideo } = useClipPublish()

async function confirm() {
  if (await deleteVideo(props.clip.id)) emit('deleted')
}
</script>

<template>
  <RecordingsConfirm
    v-model:open="open"
    title="Remove the video from this PC?"
    :description="`Frees about ${formatBytes(clip.sizeBytes)}. &quot;${clip.title}&quot; stays in your recordings and plays from YouTube; the file cannot be brought back from the app.`"
    confirm-label="Remove from this PC"
    @confirm="confirm"
  />
</template>
