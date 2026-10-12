<script setup lang="ts">
import type { Clip } from '~/types/recordings'
import { clipPublishText, YOUTUBE_DESCRIPTION_MAX } from '~/utils/clip-publish'
import { formatBytes, revealLabel } from '~/utils/recording-library'
import { formatClock } from '~/utils/recording-moments'

/**
 * "Publish on YouTube" (#1815): YouTube's own upload page does the upload, in
 * the player's browser and under their channel. The dialog walks the four
 * steps — open the page, drag the clip onto it, paste the prepared text, give
 * the video's link back — and keeps nothing until that link is saved. The
 * shell keeps a link only once YouTube confirms it is a video anyone with the
 * link can watch, titled as the clip: so the title is not editable here (the
 * clip is renamed instead). Then the file can leave this PC, the clip playing
 * from YouTube (#2005).
 */
const props = defineProps<{ clip: Clip }>()
const open = defineModel<boolean>('open', { default: false })

const { fileSrc, reveal } = useRecordings()
const { nameOf, aliasOf } = useChampionStatics()
const { openUpload, openVideo, startDrag, setYoutubeUrl, copy } = useClipPublish()

const title = ref('')
const description = ref('')
const tags = ref('')
const link = ref('')
const saving = ref(false)
const linkError = ref<string | null>(null)
const freeing = ref(false)

/** Prepared again on each opening, from the clip as it is now (a rename included). */
watch(open, (opened) => {
  if (!opened) return
  const championId = props.clip.championId
  const champion = championId ? { name: nameOf(championId), alias: aliasOf(championId) } : null
  const text = clipPublishText(props.clip, champion)
  title.value = text.title
  description.value = text.description
  tags.value = text.tags
  link.value = props.clip.youtubeUrl ?? ''
  linkError.value = null
}, { immediate: true })
watch(link, () => (linkError.value = null))

const thumbnailFailed = ref(false)
const thumbnail = computed(() => (thumbnailFailed.value ? null : fileSrc(props.clip.thumbnailPath)))
watch(() => props.clip.thumbnailPath, () => (thumbnailFailed.value = false))

async function saveLink() {
  saving.value = true
  linkError.value = await setYoutubeUrl(props.clip.id, link.value)
  saving.value = false
}

async function forgetLink() {
  linkError.value = await setYoutubeUrl(props.clip.id, '')
  if (!linkError.value) link.value = ''
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="Publish on YouTube"
    description="YouTube uploads it from your browser, to the channel you are signed in with. Choose Unlisted or Public: TrueMain then checks the video and can play it here."
    :ui="{ content: 'max-w-xl', footer: 'justify-end' }"
  >
    <template #body>
      <ol class="flex flex-col gap-5">
        <li class="flex flex-col gap-2">
          <p class="step">Open YouTube's upload page</p>
          <UButton icon="i-lucide-youtube" label="Open YouTube" class="self-start" @click="openUpload" />
        </li>

        <li class="flex flex-col gap-2">
          <p class="step">Drag the clip onto it</p>
          <div
            draggable="true"
            role="button"
            tabindex="-1"
            aria-label="Drag the clip's video onto YouTube"
            class="surface surface-hover flex cursor-grab items-center gap-3 rounded-lg p-2 active:cursor-grabbing"
            @dragstart.prevent="startDrag(clip.id)"
          >
            <UIcon name="i-lucide-grip-vertical" class="size-4 shrink-0 text-dimmed" />
            <img v-if="thumbnail" :src="thumbnail" alt="" draggable="false" class="aspect-video w-24 shrink-0 rounded object-cover" @error="thumbnailFailed = true">
            <span v-else class="flex aspect-video w-24 shrink-0 items-center justify-center rounded bg-ink-900">
              <UIcon name="i-lucide-scissors" class="size-4 text-ink-700" />
            </span>
            <div class="min-w-0 flex-1 leading-tight">
              <p class="truncate text-sm font-medium text-highlighted">{{ clip.title }}</p>
              <p class="text-xs text-muted tabular-nums">{{ formatClock(clip.durationMs) }} · {{ formatBytes(clip.sizeBytes) }}</p>
            </div>
            <UIcon name="i-lucide-hand-grab" class="size-4 shrink-0 text-dimmed" />
          </div>
          <UButton
            :label="revealLabel()"
            icon="i-lucide-folder-open"
            color="neutral"
            variant="link"
            size="xs"
            class="self-start px-0"
            @click="reveal(clip.videoPath)"
          />
        </li>

        <li class="flex flex-col gap-2">
          <p class="step">Paste the details</p>
          <UFormField label="Title" help="Keep it as is: TrueMain recognises the video by its title. Rename the clip to change it.">
            <UInput :model-value="title" readonly class="w-full">
              <template #trailing>
                <UButton icon="i-lucide-copy" color="neutral" variant="link" size="xs" aria-label="Copy the title" @click="copy(title, 'Title')" />
              </template>
            </UInput>
          </UFormField>
          <UFormField label="Description">
            <div class="relative">
              <UTextarea v-model="description" :maxlength="YOUTUBE_DESCRIPTION_MAX" :rows="4" autoresize class="w-full" />
              <UButton
                icon="i-lucide-copy"
                color="neutral"
                variant="link"
                size="xs"
                class="absolute right-1.5 top-1.5"
                aria-label="Copy the description"
                @click="copy(description, 'Description')"
              />
            </div>
          </UFormField>
          <UFormField label="Tags" help="Under &quot;Show more&quot; in YouTube's form.">
            <UInput v-model="tags" class="w-full">
              <template #trailing>
                <UButton icon="i-lucide-copy" color="neutral" variant="link" size="xs" aria-label="Copy the tags" @click="copy(tags, 'Tags')" />
              </template>
            </UInput>
          </UFormField>
        </li>

        <li class="flex flex-col gap-2">
          <p class="step">Once it is up, paste its link</p>
          <template v-if="clip.youtubeUrl">
            <div class="flex items-center gap-2 text-sm">
              <UIcon name="i-lucide-circle-check" class="size-4 shrink-0 text-success" />
              <span class="min-w-0 flex-1 truncate text-default">{{ clip.youtubeUrl }}</span>
              <UButton icon="i-lucide-external-link" label="Watch" color="neutral" variant="outline" size="xs" @click="openVideo(clip.id)" />
              <UButton
                v-if="clip.videoOnDisk"
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Forget the link"
                title="Forget the link"
                @click="forgetLink"
              />
            </div>
            <p v-if="linkError" class="text-xs text-error" role="alert">{{ linkError }}</p>
            <div v-if="clip.videoOnDisk" class="flex items-center gap-3">
              <UButton
                icon="i-lucide-hard-drive-download"
                :label="`Remove from this PC · ${formatBytes(clip.sizeBytes)}`"
                color="neutral"
                variant="soft"
                size="sm"
                @click="freeing = true"
              />
              <span class="text-xs text-muted">The clip stays in your recordings and plays from YouTube.</span>
            </div>
          </template>
          <form v-else class="flex flex-col gap-1.5" @submit.prevent="saveLink">
            <div class="flex gap-2">
              <UInput
                v-model="link"
                placeholder="https://youtu.be/…"
                class="flex-1"
                aria-label="The video's YouTube link"
                :color="linkError ? 'error' : undefined"
                :highlight="Boolean(linkError)"
              />
              <UButton type="submit" label="Check and save" color="neutral" variant="outline" :loading="saving" :disabled="!link.trim()" />
            </div>
            <p v-if="linkError" class="text-xs text-error" role="alert">{{ linkError }}</p>
            <p v-else class="text-xs text-muted">TrueMain checks with YouTube that it is this clip's video.</p>
          </form>
        </li>
      </ol>
    </template>

    <template #footer>
      <UButton label="Close" color="neutral" variant="ghost" @click="open = false" />
    </template>
  </UModal>

  <RecordingsDeleteVideo v-model:open="freeing" :clip="clip" @deleted="open = false" />
</template>

<style scoped>
ol {
  counter-reset: step;
}

.step {
  counter-increment: step;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--ui-text-highlighted);
}

.step::before {
  content: counter(step) ". ";
  color: var(--ui-primary);
}
</style>
