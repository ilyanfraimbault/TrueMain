<script setup lang="ts">
import type { DraftClip } from '~/composables/useRecapDrafts'
import type { GameRecording, Moment } from '~/types/recordings'

/**
 * The recap of a recorded game (#1777): the video, the game's timeline under
 * it with the player's kills, deaths and assists and the objectives, and the
 * clips cut from it by hand — several ranges, each named and saved as its own
 * video — beside it; then the call on the full game, kept or deleted. The end
 * of a recorded game opens it (`recording://recap`, `useRecordings`); the
 * Recordings page and the dashboard's "Watch" open it later.
 */
const route = useRoute()
const router = useRouter()
const { library, loadState, fileSrc, getRecording, deleteRecording } = useRecordings()

const id = computed(() => String(route.params.id))
const fetched = ref<GameRecording | null>(null)
const missing = ref(false)

const recording = computed<GameRecording | null>(() =>
  library.value?.games.find(game => game.id === id.value) ?? (fetched.value?.id === id.value ? fetched.value : null))

// Opened before the library was read (straight from the end of a game): ask for this one.
watch([id, () => library.value, loadState], async () => {
  if (recording.value || loadState.value === 'pending') return
  try {
    fetched.value = await getRecording(id.value)
    missing.value = false
  }
  catch {
    missing.value = true
  }
}, { immediate: true })

const length = computed(() => recording.value?.durationMs ?? 0)
const saved = computed(() => (library.value?.clips.filter(clip => clip.recordingId === id.value) ?? []).sort((a, b) => a.startMs - b.startMs))
const savedRanges = computed(() => saved.value.map(clip => ({ id: clip.id, startMs: clip.startMs, endMs: clip.endMs, title: clip.title })))

const player = ref<{ seek: (ms: number) => void, playRange: (start: number, end: number) => void, jumpTo: (moment: Moment) => void, currentMs: number } | null>(null)
const playerLength = computed(() => length.value)
const { drafts, selected, pendingIn, add, resize, rename, remove, setIn, setOut, save, saveAll } = useRecapDrafts(recording, playerLength)
const currentMs = computed(() => player.value?.currentMs ?? 0)

function preview(draft: DraftClip) {
  selected.value = draft.key
  player.value?.playRange(draft.startMs, draft.endMs)
}

const confirming = ref(false)
async function removeGame() {
  if (await deleteRecording(id.value)) void router.push('/recordings')
}

const note = computed(() => {
  switch (recording.value?.status) {
    case 'recording': return { title: 'This game is still being recorded', description: 'The video and its moments fill in once the game ends.' }
    case 'processing': return {
      title: 'The highlights are still being finalised',
      description: recording.value.highlightsSource === 'live'
        ? 'The moments shown were noted live during the game; the match history\'s will replace them shortly.'
        : 'They fill in once the match history has the game.',
    }
    default: return null
  }
})
</script>

<template>
  <div v-if="recording" class="flex h-full flex-col gap-3 overflow-y-auto p-4">
    <RecapHeader :recording="recording" @delete="confirming = true" />

    <UAlert
      v-if="note"
      color="neutral"
      variant="subtle"
      class="shrink-0"
      icon="i-lucide-loader-circle"
      :title="note.title"
      :description="note.description"
      :ui="{ root: 'py-2', title: 'text-xs', description: 'text-xs' }"
    />

    <div class="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_16rem] gap-4">
      <RecapPlayer
        ref="player"
        :src="fileSrc(recording.videoPath)"
        :length-ms="recording.durationMs"
        :moments="recording.moments"
        editable
        :drafts="drafts"
        :selected="selected"
        :saved="savedRanges"
        :pending-in="pendingIn"
        @create="add"
        @resize="resize"
        @select="key => (selected = key)"
        @set-in="setIn"
        @set-out="setOut"
      />
      <RecapSidePanel
        :drafts="drafts"
        :selected="selected"
        :saved="saved"
        :moments="recording.moments"
        :current-ms="currentMs"
        @select="key => { selected = key; player?.seek(drafts.find(draft => draft.key === key)?.startMs ?? currentMs) }"
        @rename="rename"
        @preview="preview"
        @remove="remove"
        @save="save"
        @save-all="saveAll"
        @jump="moment => player?.jumpTo(moment)"
      />
    </div>

    <RecordingsConfirm
      v-model:open="confirming"
      title="Delete the full game?"
      :description="saved.length
        ? `Its video is removed from your disk. The ${saved.length} clip${saved.length === 1 ? '' : 's'} you saved from it stay${saved.length === 1 ? 's' : ''}.`
        : 'Its video is removed from your disk. Save the clips you want first: unsaved ranges are lost.'"
      confirm-label="Delete full game"
      @confirm="removeGame"
    />
  </div>

  <div v-else-if="missing" class="flex h-full items-center justify-center">
    <UEmpty
      icon="i-lucide-film"
      title="This full game is no longer on disk"
      description="It was deleted, or freed to stay within the disk budget. The clips saved from it are still in Recordings."
      :actions="[{ label: 'Recordings', icon: 'i-lucide-arrow-left', color: 'neutral', variant: 'outline', to: '/recordings' }]"
      variant="naked"
    />
  </div>

  <div v-else class="flex h-full items-center justify-center">
    <UIcon name="i-lucide-loader-circle" class="size-6 text-dimmed" />
  </div>
</template>
