<script setup lang="ts">
import type { Clip } from '~/types/recordings'

/**
 * A published clip whose file left this PC, played from YouTube (#2005). The
 * frame is the shell's loopback page (`recording/embed.rs`) — YouTube's player
 * refuses to play inside the app's own page on macOS. A private video does not
 * play embedded, so "Watch on YouTube" always sits under it.
 */
const props = defineProps<{ clip: Pick<Clip, 'id' | 'thumbnailPath'> }>()

const { fileSrc } = useRecordings()
const { embedUrl, openVideo } = useClipPublish()

const src = ref<string | null>(null)
const resolved = ref(false)
watch(() => props.clip.id, async (id) => {
  resolved.value = false
  src.value = await embedUrl(id)
  resolved.value = true
}, { immediate: true })

const poster = computed(() => fileSrc(props.clip.thumbnailPath))
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="relative aspect-video w-full overflow-hidden rounded-lg bg-black">
      <iframe
        v-if="src"
        :src="src"
        title="The clip on YouTube"
        class="size-full border-0"
        allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
        allowfullscreen
      />
      <template v-else-if="resolved">
        <img v-if="poster" :src="poster" alt="" class="size-full object-cover opacity-40">
        <UButton
          icon="i-lucide-youtube"
          label="Watch on YouTube"
          class="absolute left-1/2 top-1/2 -translate-1/2"
          @click="openVideo(clip.id)"
        />
      </template>
    </div>
    <div class="flex items-center gap-2 text-xs text-muted">
      <UIcon name="i-lucide-youtube" class="size-3.5 shrink-0" />
      <span class="flex-1">Played from YouTube: this clip's video is no longer on this PC. A private video only plays on YouTube.</span>
      <UButton icon="i-lucide-external-link" label="Watch on YouTube" color="neutral" variant="outline" size="xs" @click="openVideo(clip.id)" />
    </div>
  </div>
</template>
