<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { LibraryItem } from '~/utils/recording-library'
import { itemTitle, recordingKda, revealLabel } from '~/utils/recording-library'
import { getQueueLabel } from '~/utils/queues'

/**
 * One card of the Recordings page, laid out as the reference's: the picture,
 * then the champion, the kind and the title, then the game's line — K / D / A,
 * KDA ratio, queue. A full game opens its recap, a clip its player. The menu
 * pins (a favourite clip, a kept game), shows the file and deletes.
 */
const props = defineProps<{ item: LibraryItem }>()

const { nameOf } = useChampionStatics()
const { setKept, updateClip, deleteClip, deleteRecording, reveal } = useRecordings()

const title = computed(() => itemTitle(props.item, nameOf))
const kda = computed(() => recordingKda(props.item))
const isClip = computed(() => props.item.kind === 'clip')
const to = computed(() => isClip.value
  ? `/recordings/clips/${encodeURIComponent(props.item.id)}`
  : `/recordings/${encodeURIComponent(props.item.id)}`)

const confirming = ref(false)

function togglePin() {
  if (isClip.value) void updateClip(props.item.id, { favorite: !props.item.pinned })
  else void setKept(props.item.id, !props.item.pinned)
}

const menu = computed<DropdownMenuItem[][]>(() => [
  [
    isClip.value
      ? { label: props.item.pinned ? 'Remove from favourites' : 'Add to favourites', icon: 'i-lucide-star', onSelect: togglePin }
      : { label: props.item.pinned ? 'Stop keeping' : 'Keep full game', icon: 'i-lucide-pin', onSelect: togglePin },
    { label: revealLabel(), icon: 'i-lucide-folder-open', onSelect: () => void reveal(props.item.videoPath) },
  ],
  [{ label: isClip.value ? 'Delete clip' : 'Delete full game', icon: 'i-lucide-trash-2', color: 'error', onSelect: () => (confirming.value = true) }],
])

const confirmText = computed(() => isClip.value
  ? { title: 'Delete this clip?', description: `"${title.value}" is removed from your disk.`, label: 'Delete clip' }
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
        </div>
        <p class="flex items-baseline gap-1.5 truncate text-[11px] tabular-nums">
          <template v-if="kda">
            <span class="font-medium text-default">{{ kda.line }}</span>
            <span class="text-muted">{{ kda.ratio }}</span>
          </template>
          <span v-else class="text-dimmed">Waiting for the match history</span>
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
        class="absolute right-1.5 top-1.5 bg-black/70 opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 data-[state=open]:opacity-100"
        aria-label="More actions"
      />
    </UDropdownMenu>

    <RecordingsConfirm
      v-model:open="confirming"
      :title="confirmText.title"
      :description="confirmText.description"
      :confirm-label="confirmText.label"
      @confirm="remove"
    />
  </article>
</template>
