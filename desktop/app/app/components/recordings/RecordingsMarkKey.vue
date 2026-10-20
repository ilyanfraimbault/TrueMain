<script setup lang="ts">
import type { Chord, ChordView } from '~/types/overlay'
import { chordOf, isKnownKey, isModifier } from '~/utils/overlay-keys'
import { MARK_WINDOW_MS } from '~/utils/recording-moments'

/**
 * The mark key (#2079): while a game records, a press keeps the 30 seconds
 * before it as a clip. Recorded like an overlay panel's chord — from the keys
 * pressed on the field, by position — and checked by the shell before it is
 * saved (`recording_check_mark_key`): a refusal shows under the field, and a
 * chord whose key the game also uses shows what it does there, since the key
 * is read from the keyboard, never taken from the game.
 */
const props = defineProps<{ markKey: Chord | null, chord: ChordView | null }>()
const emit = defineEmits<{ change: [markKey: Chord | null] }>()

/** ⌥⇧5 / Alt+Shift+5: the shell's `DEFAULT_MARK_KEY`, offered when marking is turned back on. */
const DEFAULT_MARK_KEY: Chord = { alt: true, shift: true, ctrl: false, meta: false, tab: false, key: 'Digit5' }
const seconds = MARK_WINDOW_MS / 1000

const { checkMarkKey } = useRecordings()

const refusal = ref<string | null>(null)
/** The refused chord, as it reads. */
const attempt = ref<string | null>(null)
const listening = ref(false)
const field = ref<HTMLButtonElement | null>(null)
watch(() => props.markKey, () => {
  refusal.value = null
  attempt.value = null
})

async function submit(chord: Chord) {
  const answer = await checkMarkKey(chord).catch(error => ({ refusal: String(error), chord: null }))
  if (answer.refusal) {
    refusal.value = answer.refusal
    attempt.value = answer.chord?.label ?? null
    return
  }
  refusal.value = null
  attempt.value = null
  emit('change', chord)
}

function toggle(on: boolean) {
  if (on) void submit(DEFAULT_MARK_KEY)
  else emit('change', null)
}

function record() {
  listening.value = true
  void nextTick(() => field.value?.focus())
}

let tab = false
function stop() {
  listening.value = false
  tab = false
}

function keydown(event: KeyboardEvent) {
  if (!listening.value) return
  event.preventDefault()
  event.stopPropagation()
  if (event.repeat) return
  if (event.code === 'Tab') {
    tab = true
    return
  }
  if (isModifier(event.code)) return
  if (event.code === 'Escape' && !tab && !event.altKey && !event.shiftKey && !event.ctrlKey && !event.metaKey) {
    stop()
    return
  }
  const chord = chordOf(event, tab)
  stop()
  if (!isKnownKey(event.code)) {
    refusal.value = 'This key cannot be part of a shortcut.'
    attempt.value = null
    return
  }
  void submit(chord)
}

function keyup(event: KeyboardEvent) {
  if (listening.value && event.code === 'Tab') tab = false
}

const label = computed(() => {
  if (listening.value) return 'Press the keys…'
  if (refusal.value) return attempt.value ?? 'Record a shortcut'
  return props.chord?.label ?? 'Record a shortcut'
})
</script>

<template>
  <section class="flex flex-col gap-2">
    <USwitch
      :model-value="markKey !== null"
      label="Mark key"
      :description="`While a game records, press it to keep the ${seconds} seconds before as a clip.`"
      @update:model-value="toggle"
    />
    <div v-if="markKey" class="flex items-center gap-1">
      <button
        ref="field"
        type="button"
        class="min-w-0 flex-1 truncate rounded-md bg-elevated px-2 py-1 text-left font-mono text-xs ring-1 transition"
        :class="[listening ? 'text-primary ring-primary' : 'text-highlighted ring-default hover:ring-primary/60', refusal && !listening && 'ring-error/60']"
        :aria-label="`Mark key: ${label}. Click, then press the keys.`"
        :title="listening ? 'Esc to cancel' : 'Click, then press the keys'"
        @click="record"
        @blur="stop"
        @keydown="keydown"
        @keyup="keyup"
      >
        {{ label }}
      </button>
    </div>
    <p v-if="refusal" class="text-xs text-error">{{ refusal }}</p>
    <p v-else-if="markKey && chord?.warning" class="flex gap-1 text-xs text-warning">
      <UIcon name="i-lucide-triangle-alert" class="mt-0.5 size-3 shrink-0" />
      <span>{{ chord.warning }}</span>
    </p>
    <p v-if="markKey" class="text-xs text-muted">
      Read from the keyboard while League is in front, never taken from it. Each mark is saved like a clip you cut, and you
      can move its ends in the game's recap.
    </p>
  </section>
</template>
