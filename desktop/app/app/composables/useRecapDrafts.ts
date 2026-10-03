import type { Ref } from 'vue'
import type { GameRecording } from '~/types/recordings'
import { proposeClipTitle } from '~/utils/recording-moments'

/** A range of a full game the player is cutting, not saved yet. */
export interface DraftClip {
  key: string
  startMs: number
  endMs: number
  title: string
  /** Renamed by the player: the proposed title no longer follows the range. */
  edited: boolean
  saving: boolean
}

/** Shorter than this is a slip of the mouse, not a clip — and the shell cuts on 1 s keyframes. */
export const MIN_CLIP_MS = 2_000
/** "Set out" with no "in": the clip ends at the playhead and starts this far before it. */
const OUT_ONLY_MS = 20_000

/**
 * The clips being cut from one full game (#1777): several ranges, each named
 * from what it holds until the player renames it, saved one by one or all at
 * once through `clip_save`. Held per recording for the session, so leaving
 * the recap and coming back finds the ranges where they were.
 */
export function useRecapDrafts(recording: Ref<GameRecording | null>, lengthMs: Ref<number>) {
  const store = useState<Record<string, DraftClip[]>>('recap-drafts', () => ({}))
  const selected = ref<string | null>(null)
  const pendingIn = ref<number | null>(null)
  const { nameOf } = useChampionStatics()
  const { saveClip } = useRecordings()

  const drafts = computed<DraftClip[]>(() => (recording.value ? store.value[recording.value.id] ?? [] : []))

  function write(next: DraftClip[]) {
    if (recording.value) store.value = { ...store.value, [recording.value.id]: next }
  }

  function propose(startMs: number, endMs: number) {
    const game = recording.value
    if (!game) return 'Clip'
    return proposeClipTitle(game.moments, startMs, endMs, game.championId ? nameOf(game.championId) : null)
  }

  /** A range kept inside the video and at least `MIN_CLIP_MS` long. */
  function bounded(startMs: number, endMs: number) {
    const length = lengthMs.value
    let start = Math.max(0, Math.min(startMs, endMs))
    let end = Math.min(length, Math.max(startMs, endMs))
    if (end - start < MIN_CLIP_MS) {
      end = Math.min(length, start + MIN_CLIP_MS)
      start = Math.max(0, end - MIN_CLIP_MS)
    }
    return { startMs: Math.round(start), endMs: Math.round(end) }
  }

  let counter = 0
  function add(startMs: number, endMs: number) {
    const range = bounded(startMs, endMs)
    const key = `draft-${Date.now()}-${counter++}`
    write([...drafts.value, { key, ...range, title: propose(range.startMs, range.endMs), edited: false, saving: false }])
    selected.value = key
    pendingIn.value = null
    return key
  }

  function patch(key: string, change: Partial<DraftClip>) {
    write(drafts.value.map(draft => (draft.key === key ? { ...draft, ...change } : draft)))
  }

  /** Move a range's ends; its proposed title follows what it now holds, unless the player renamed it. */
  function resize(key: string, startMs: number, endMs: number) {
    const draft = drafts.value.find(entry => entry.key === key)
    if (!draft) return
    const range = bounded(startMs, endMs)
    patch(key, { ...range, title: draft.edited ? draft.title : propose(range.startMs, range.endMs) })
  }

  function rename(key: string, title: string) {
    patch(key, { title, edited: true })
  }

  function remove(key: string) {
    write(drafts.value.filter(draft => draft.key !== key))
    if (selected.value === key) selected.value = null
  }

  /** "Set in" (I): the selected range's start, or the start of the next one. */
  function setIn(atMs: number) {
    const draft = drafts.value.find(entry => entry.key === selected.value)
    if (draft) resize(draft.key, Math.min(atMs, draft.endMs - MIN_CLIP_MS), draft.endMs)
    else pendingIn.value = atMs
  }

  /** "Set out" (O): the selected range's end, else a new range from the "in" (or 20 s back) to here. */
  function setOut(atMs: number) {
    const draft = drafts.value.find(entry => entry.key === selected.value)
    if (draft) return resize(draft.key, draft.startMs, Math.max(atMs, draft.startMs + MIN_CLIP_MS))
    const start = pendingIn.value !== null && pendingIn.value < atMs ? pendingIn.value : atMs - OUT_ONLY_MS
    add(start, atMs)
  }

  async function save(key: string) {
    const game = recording.value
    const draft = drafts.value.find(entry => entry.key === key)
    if (!game || !draft || draft.saving) return
    patch(key, { saving: true })
    const clip = await saveClip(game.id, draft.startMs, draft.endMs, draft.title.trim() || propose(draft.startMs, draft.endMs))
    if (clip) remove(key)
    else patch(key, { saving: false })
  }

  /** One at a time: each is a cut of the same file. */
  async function saveAll() {
    for (const draft of [...drafts.value]) await save(draft.key)
  }

  return { drafts, selected, pendingIn, add, resize, rename, remove, setIn, setOut, save, saveAll }
}
