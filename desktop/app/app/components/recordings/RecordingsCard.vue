<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { LibraryItem } from '~/utils/recording-library'
import { itemTitle, recordingKda, revealLabel } from '~/utils/recording-library'
import { getQueueLabel } from '~/utils/queues'

/**
 * One card of the Recordings page, laid out as the reference's: the picture,
 * then the champion, the kind and the title, then the game's line — K / D / A,
 * KDA ratio, queue. A full game opens its recap, a clip its player. The menu
 * pins (a favourite clip, a kept game), publishes a clip on YouTube, shows the
 * file and deletes.
 */
const props = defineProps<{ item: LibraryItem }>()

const { nameOf } = useChampionStatics()
const { library, setKept, updateClip, deleteClip, deleteRecording, reveal } = useRecordings()
const { openVideo } = useClipPublish()

const title = computed(() => itemTitle(props.item, nameOf))
const kda = computed(() => recordingKda(props.item))
const isClip = computed(() => props.item.kind === 'clip')
const to = computed(() => isClip.value
  ? `/recordings/clips/${encodeURIComponent(props.item.id)}`
  : `/recordings/${encodeURIComponent(props.item.id)}`)

const confirming = ref(false)
const publishing = ref(false)
const freeing = ref(false)
const clip = computed(() => (isClip.value ? library.value?.clips.find(entry => entry.id === props.item.id) ?? null : null))

function togglePin() {
  if (isClip.value) void updateClip(props.item.id, { favorite: !props.item.pinned })
  else void setKept(props.item.id, !props.item.pinned)
}

/** A clip's YouTube entries: publish while its file is here, watch once linked, free the file once published. */
function youtubeMenu(): DropdownMenuItem[] {
  const { videoOnDisk, youtubeUrl, id } = props.item
  return [
    ...(videoOnDisk ? [{ label: 'Publish on YouTube', icon: 'i-lucide-youtube', onSelect: () => (publishing.value = true) }] : []),
    ...(youtubeUrl ? [{ label: 'Watch on YouTube', icon: 'i-lucide-external-link', onSelect: () => void openVideo(id) }] : []),
    ...(youtubeUrl && videoOnDisk ? [{ label: 'Remove from this PC', icon: 'i-lucide-hard-drive-download', onSelect: () => (freeing.value = true) }] : []),
  ]
}

const menu = computed<DropdownMenuItem[][]>(() => [
  [
    isClip.value
      ? { label: props.item.pinned ? 'Remove from favourites' : 'Add to favourites', icon: 'i-lucide-star', onSelect: togglePin }
      : { label: props.item.pinned ? 'Stop keeping' : 'Keep full game', icon: 'i-lucide-pin', onSelect: togglePin },
    ...(props.item.videoOnDisk ? [{ label: revealLabel(), icon: 'i-lucide-folder-open', onSelect: () => void reveal(props.item.videoPath) }] : []),
  ],
  ...(isClip.value ? [youtubeMenu()] : []),
  [{ label: isClip.value ? 'Delete clip' : 'Delete full game', icon: 'i-lucide-trash-2', color: 'error', onSelect: () => (confirming.value = true) }],
])

const confirmText = computed(() => isClip.value
  ? { title: 'Delete this clip?', description: props.item.videoOnDisk ? `"${title.value}" is removed from your disk.` : `"${title.value}" leaves your recordings; the video stays on YouTube.`, label: 'Delete clip' }
  : { title: 'Delete this full game?', description: 'The game\'s video is removed from your disk. Clips you saved from it stay.', label: 'Delete full game' })

async function remove() {
  if (isClip.value) await deleteClip(props.item.id)
  else await deleteRecording(props.item.id)
}
</script>

<template>
  <article class="group surface surface-hover relative overflow-hidden rounded-lg">
    <NuxtLink :to="to" class="block focus-visible:outline-2 focus-visible:outline-primary" :aria-label="title">
      <RecordingsThumb :item="item" />
      <div class="flex flex-col gap-1 px-2.5 py-2">
        <div class="flex min-w-0 items-center gap-2">
          <ChampionPortrait :champion-id="item.championId" size="sm" class="size-7! rounded-md!" />
          <UIcon
            :name="isClip ? 'i-lucide-scissors' : 'i-lucide-film'"
            class="size-3.5 shrink-0"
            :class="isClip ? 'text-primary' : 'text-dimmed'"
            :title="isClip ? 'Clip' : 'Full game'"
          />
          <p class="truncate text-[13px] font-semibold text-highlighted" :title="title">{{ title }}</p>
          <UIcon v-if="item.youtubeUrl && item.videoOnDisk" name="i-lucide-youtube" class="ml-auto size-3.5 shrink-0 text-muted" title="Published on YouTube" />
        </div>
        <p class="flex items-baseline gap-1.5 truncate text-[11px] tabular-nums">
          <template v-if="kda">
            <span class="font-medium text-default">{{ kda.line }}</span>
            <span class="text-muted">{{ kda.ratio }}</span>
          </template>
          <span v-else-if="item.status !== 'ready'" class="text-dimmed">Waiting for the match history</span>
          <span class="truncate text-dimmed">{{ getQueueLabel(item.queueId) }}</span>
        </p>
      </div>
    </NuxtLink>

    <UDropdownMenu :items="menu" :content="{ align: 'end' }">
      <UButton
        icon="i-lucide-ellipsis-vertical"
        color="neutral"
        variant="solid"
        size="xs"
        class="absolute right-1.5 top-1.5 bg-black/70 text-white opacity-0 hover:bg-black/85 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 data-[state=open]:opacity-100"
        aria-label="More actions"
      />
    </UDropdownMenu>

    <RecordingsPublish v-if="clip" v-model:open="publishing" :clip="clip" />
    <RecordingsDeleteVideo v-if="clip" v-model:open="freeing" :clip="clip" />

    <RecordingsConfirm
      v-model:open="confirming"
      :title="confirmText.title"
      :description="confirmText.description"
      :confirm-label="confirmText.label"
      @confirm="remove"
    />
  </article>
</template>
