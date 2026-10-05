<script setup lang="ts">
import type { Chord, ChordView, OverlayPanel, PanelTrigger } from '~/types/overlay'
import { OVERLAY_PANEL_INFO } from '~/types/overlay'
import { chordOf, chordOfTrigger, isKnownKey, isModifier, suggestedChord } from '~/utils/overlay-keys'

/**
 * What brings one panel on screen in game (#1915): always, while a chord is
 * held, or toggled by one. The chord is recorded from the keys pressed on the
 * field, by position (`KeyboardEvent.code`); the shell says whether it can be
 * the panel's (`overlay_check_trigger`) before anything is saved, and the
 * refusal shows under the field. A chord whose key the game also uses shows
 * what it does there: the overlay reads keys, it never takes them from the game.
 */
const props = defineProps<{ panel: OverlayPanel, trigger: PanelTrigger, chord: ChordView | null }>()
const emit = defineEmits<{ change: [trigger: PanelTrigger] }>()

type Kind = PanelTrigger['kind']
const KINDS: { value: Kind, label: string }[] = [
  { value: 'always', label: 'Always' },
  { value: 'whileHeld', label: 'While held' },
  { value: 'toggle', label: 'Toggle' },
]

const { check } = useGameOverlay()

/** A kind picked whose chord the shell refused: shown until a chord is accepted or the field is left. */
const pending = ref<Kind | null>(null)
const kind = computed(() => pending.value ?? props.trigger.kind)
const refusal = ref<string | null>(null)
/** The refused chord, as it reads. */
const attempt = ref<string | null>(null)
const listening = ref(false)
const field = ref<HTMLButtonElement | null>(null)
watch(() => props.trigger, () => {
  pending.value = null
  refusal.value = null
  attempt.value = null
})

const startShown = computed(() => (props.trigger.kind === 'toggle' ? props.trigger.startShown : true))

function triggerOf(next: Kind, chord: Chord): PanelTrigger {
  if (next === 'always') return { kind: 'always' }
  return next === 'toggle' ? { kind: 'toggle', chord, startShown: startShown.value } : { kind: 'whileHeld', chord }
}

async function submit(next: PanelTrigger) {
  const answer = await check(props.panel, next)
  if (answer.refusal) {
    pending.value = next.kind
    refusal.value = answer.refusal
    attempt.value = answer.chord?.label ?? null
    return
  }
  pending.value = null
  refusal.value = null
  attempt.value = null
  emit('change', next)
}

/** A new kind keeps the chord there is, or takes the panel's suggested one; a refused one asks for keys. */
async function pick(next: Kind) {
  if (next === 'always') {
    await submit({ kind: 'always' })
    return
  }
  await submit(triggerOf(next, chordOfTrigger(props.trigger) ?? suggestedChord(props.panel, next)))
  if (refusal.value) record()
}

function record() {
  listening.value = true
  void nextTick(() => field.value?.focus())
}

let tab = false
/** TAB pressed and released with nothing else: TAB alone. */
let tabAlone = false
function stop() {
  listening.value = false
  tab = false
  tabAlone = false
}

function keydown(event: KeyboardEvent) {
  if (!listening.value) return
  event.preventDefault()
  event.stopPropagation()
  if (event.repeat) return
  if (event.code === 'Tab') {
    tab = true
    tabAlone = true
    return
  }
  if (isModifier(event.code)) return
  tabAlone = false
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
  void submit(triggerOf(kind.value === 'always' ? 'whileHeld' : kind.value, chord))
}

function keyup(event: KeyboardEvent) {
  if (!listening.value || event.code !== 'Tab') return
  event.preventDefault()
  if (tabAlone) {
    const chord = chordOf(event, true)
    stop()
    void submit(triggerOf(kind.value === 'always' ? 'whileHeld' : kind.value, chord))
  }
  tab = false
}

function setStartShown(shown: boolean) {
  if (props.trigger.kind === 'toggle') emit('change', { ...props.trigger, startShown: shown })
}

const label = computed(() => {
  if (listening.value) return 'Press the keys…'
  if (refusal.value) return attempt.value ?? 'Record a shortcut'
  return props.chord?.label ?? 'Record a shortcut'
})
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <div class="flex items-center gap-1.5 text-xs font-medium text-highlighted">
      <OverlayPanelIcon :panel="panel" class="size-3.5 text-primary" />
      {{ OVERLAY_PANEL_INFO[panel].label }}
    </div>
    <RecordingsSegmented :model-value="kind" :items="KINDS" @update:model-value="pick" />
    <div v-if="kind !== 'always'" class="flex items-center gap-1">
      <button
        ref="field"
        type="button"
        class="min-w-0 flex-1 truncate rounded-md bg-elevated px-2 py-1 text-left font-mono text-xs ring-1 transition"
        :class="[listening ? 'text-primary ring-primary' : 'text-highlighted ring-default hover:ring-primary/60', refusal && !listening && 'ring-error/60']"
        :aria-label="`${OVERLAY_PANEL_INFO[panel].label} shortcut: ${label}. Click, then press the keys.`"
        :title="listening ? 'Esc to cancel' : 'Click, then press the keys'"
        @click="record"
        @blur="stop"
        @keydown="keydown"
        @keyup="keyup"
      >
        {{ label }}
      </button>
      <UButton
        icon="i-lucide-x"
        size="xs"
        color="neutral"
        variant="ghost"
        :aria-label="`Clear the ${OVERLAY_PANEL_INFO[panel].label} shortcut`"
        title="Clear: always shown"
        @click="submit({ kind: 'always' })"
      />
    </div>
    <USwitch
      v-if="kind === 'toggle' && trigger.kind === 'toggle'"
      size="xs"
      :model-value="trigger.startShown"
      label="Shown when a game starts"
      @update:model-value="setStartShown"
    />
    <p v-if="refusal" class="text-xs text-error">{{ refusal }}</p>
    <p v-else-if="kind !== 'always' && chord?.warning" class="flex gap-1 text-xs text-warning">
      <UIcon name="i-lucide-triangle-alert" class="mt-0.5 size-3 shrink-0" />
      <span>{{ chord.warning }}</span>
    </p>
  </div>
</template>
