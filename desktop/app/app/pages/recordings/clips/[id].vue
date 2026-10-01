<script setup lang="ts">
import { dayLabel, formatBytes, recordingKda, revealLabel } from '~/utils/recording-library'
import { formatClock } from '~/utils/recording-moments'
import { getQueueLabel } from '~/utils/queues'

/**
 * A saved clip's player: the clip's own file with the moments inside it on its
 * timeline, its title (renamed in place), favourite, the file, delete — and
 * the way back to the full game it was cut from, while that still exists.
 */
const route = useRoute()
const router = useRouter()
const { library, loadState, fileSrc, updateClip, deleteClip, reveal } = useRecordings()
const { nameOf } = useChampionStatics()

const id = computed(() => String(route.params.id))
const clip = computed(() => library.value?.clips.find(entry => entry.id === id.value) ?? null)
const kda = computed(() => (clip.value ? recordingKda(clip.value) : null))
const source = computed(() => library.value?.games.find(game => game.id === clip.value?.recordingId) ?? null)

const editing = ref(false)
const draftTitle = ref('')
function startRename() {
  draftTitle.value = clip.value?.title ?? ''
  editing.value = true
}
async function commitRename() {
  if (!editing.value) return
  editing.value = false
  const title = draftTitle.value.trim()
  if (clip.value && title && title !== clip.value.title) await updateClip(clip.value.id, { title })
}

const confirming = ref(false)
async function remove() {
  if (clip.value && await deleteClip(clip.value.id)) void router.push('/recordings')
}
</script>

<template>
  <div v-if="clip" class="flex h-full flex-col gap-3 overflow-y-auto p-4">
    <header class="flex items-center gap-3">
      <UButton to="/recordings" icon="i-lucide-arrow-left" color="neutral" variant="ghost" size="sm" aria-label="Back to recordings" />
      <ChampionPortrait :champion-id="clip.championId" size="sm" />
      <div class="min-w-0 flex-1 leading-tight">
        <UInput
          v-if="editing"
          v-model="draftTitle"
          autofocus
          size="sm"
          :maxlength="80"
          class="w-full max-w-md"
          aria-label="Clip name"
          @keydown.enter="commitRename"
          @keydown.esc="editing = false"
          @blur="commitRename"
        />
        <button v-else type="button" class="group flex max-w-full items-center gap-1.5 text-left" title="Rename" @click="startRename">
          <h1 class="truncate text-lg font-semibold tracking-tight text-highlighted">{{ clip.title }}</h1>
          <UIcon name="i-lucide-pencil" class="size-3.5 shrink-0 text-dimmed opacity-0 transition-opacity group-hover:opacity-100" />
        </button>
        <p class="flex items-center gap-1.5 truncate text-xs text-muted tabular-nums">
          <span class="text-default">{{ clip.championId ? nameOf(clip.championId) : 'Game' }}<template v-if="clip.win !== null"> · {{ clip.win ? 'Victory' : 'Defeat' }}</template></span>
          <template v-if="kda"><span>{{ kda.line }}</span><span>{{ kda.ratio }}</span></template>
          <span class="text-dimmed">·</span>
          <span>{{ getQueueLabel(clip.queueId) }}</span>
          <span class="text-dimmed">·</span>
          <span>{{ dayLabel(clip.gameStartedAtMs) }}, at {{ formatClock(clip.startMs) }}</span>
          <span class="text-dimmed">·</span>
          <span>{{ formatBytes(clip.sizeBytes) }}</span>
        </p>
      </div>

      <div class="flex shrink-0 items-center gap-2">
        <UButton
          v-if="source"
          :to="`/recordings/${encodeURIComponent(source.id)}`"
          icon="i-lucide-film"
          label="Full game"
          color="neutral"
          variant="outline"
          size="sm"
        />
        <UButton
          icon="i-lucide-star"
          :color="clip.favorite ? 'primary' : 'neutral'"
          :variant="clip.favorite ? 'soft' : 'ghost'"
          size="sm"
          :aria-pressed="clip.favorite"
          :aria-label="clip.favorite ? 'Remove from favourites' : 'Add to favourites'"
          :title="clip.favorite ? 'Remove from favourites' : 'Add to favourites'"
          @click="updateClip(clip.id, { favorite: !clip.favorite })"
        />
        <UButton icon="i-lucide-folder-open" color="neutral" variant="ghost" size="sm" :aria-label="revealLabel()" :title="revealLabel()" @click="reveal(clip.videoPath)" />
        <UButton icon="i-lucide-trash-2" color="error" variant="ghost" size="sm" aria-label="Delete clip" title="Delete clip" @click="confirming = true" />
      </div>
    </header>

    <RecapPlayer
      class="mx-auto w-full max-w-[46rem]"
      :src="fileSrc(clip.videoPath)"
      :length-ms="clip.durationMs"
      :moments="clip.moments"
    />

    <RecordingsConfirm
      v-model:open="confirming"
      title="Delete this clip?"
      :description="`&quot;${clip.title}&quot; is removed from your disk.`"
      confirm-label="Delete clip"
      @confirm="remove"
    />
  </div>

  <div v-else-if="loadState !== 'pending' && library" class="flex h-full items-center justify-center">
    <UEmpty
      icon="i-lucide-scissors"
      title="This clip is no longer on disk"
      :actions="[{ label: 'Recordings', icon: 'i-lucide-arrow-left', color: 'neutral', variant: 'outline', to: '/recordings' }]"
      variant="naked"
    />
  </div>

  <div v-else class="flex h-full items-center justify-center">
    <UIcon name="i-lucide-loader-circle" class="size-6 text-dimmed" />
  </div>
</template>
