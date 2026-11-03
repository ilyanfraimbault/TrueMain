<script setup lang="ts">
/**
 * The control that writes one champion to champion select (#1909): "Hover" on
 * our pick, "Ban" on our ban. A click is the only way it acts.
 *
 * A ban is irreversible and costs the team one, so it always asks first; a
 * hover asks only when an ally is already showing that champion as their pick
 * intent. Asking is the same button armed: the first click turns it into the
 * question, a second click on it answers yes, and it disarms on its own — no
 * popover to open over the card. Out of turn, or for a champion the client
 * would refuse, the control stays disabled and says why, in its tooltip — or,
 * for a bar under a card (`block`), in its label when the reason is the
 * champion's own.
 */
const props = withDefaults(defineProps<{
  championId: number
  /** Full width under a suggestion card, the reason it is disabled written out. */
  block?: boolean
}>(), { block: false })

/** How long an armed control waits for its second click. */
const ARMED_MS = 4000

const { nameOf } = useChampionStatics()
const { writable, kind, shownKind, pending, availability, allyHovering, allyName, hover, ban } = useChampSelectActions()

const state = computed(() => availability(props.championId))
const ally = computed(() => allyHovering(props.championId))
const isBan = computed(() => shownKind.value === 'ban')
const busy = computed(() => pending.value?.championId === props.championId && pending.value.kind !== 'lock')
const hovered = computed(() => state.value.reason === 'Hovered in the client')

/** Whether the click asks first rather than writing at once. */
const asks = computed(() => kind.value === 'ban' || ally.value !== null)
const armed = ref(false)
let disarm: ReturnType<typeof setTimeout> | undefined

function setArmed(value: boolean) {
  clearTimeout(disarm)
  armed.value = value
  if (value) disarm = setTimeout(() => { armed.value = false }, ARMED_MS)
}

onBeforeUnmount(() => clearTimeout(disarm))
// The draft moved under an armed control: its question no longer stands.
watch(() => [kind.value, state.value.allowed], () => setArmed(false))

/**
 * A bar writes why this champion cannot be written ("Banned", "Not owned or
 * disabled"); being out of turn holds for every card alike and the strip over
 * the draft already says it, so that one stays in the tooltip.
 */
const spelled = computed(() => props.block && !state.value.allowed && !!state.value.reason && !state.value.reason.startsWith('Not your turn'))

const label = computed(() => {
  if (armed.value) return 'Confirm'
  if (hovered.value) return 'Hovered'
  if (spelled.value && state.value.reason) return state.value.reason
  return isBan.value ? 'Ban' : 'Hover'
})

/** What the armed control asks, said in its tooltip and to a screen reader. */
const question = computed(() => {
  const name = nameOf(props.championId)
  const intent = ally.value ? `${name} is ${allyName(ally.value)}'s pick intent.` : null
  if (kind.value === 'ban') return [intent, `Click again to ban ${name}: a ban cannot be undone.`].filter(Boolean).join(' ')
  return `${intent} Click again to hover it anyway.`
})

const tooltip = computed(() => {
  if (armed.value) return question.value
  return spelled.value ? null : state.value.reason
})

const icon = computed(() => {
  if (busy.value) return 'i-lucide-loader-circle'
  if (hovered.value) return 'i-lucide-check'
  if (armed.value) return 'i-lucide-triangle-alert'
  return isBan.value ? 'i-lucide-ban' : 'i-lucide-hand'
})

function click() {
  if (!state.value.allowed || pending.value) return
  if (asks.value && !armed.value) {
    setArmed(true)
    return
  }
  setArmed(false)
  if (kind.value === 'ban') void ban(props.championId)
  else void hover(props.championId)
}
</script>

<template>
  <UTooltip v-if="writable" :text="tooltip ?? undefined" :disabled="!tooltip" :content="{ side: 'top' }">
    <!-- A disabled button takes no pointer events: the span is what the tooltip hangs on. -->
    <span class="inline-flex" :class="block && 'w-full'">
      <UButton
        :size="block ? 'sm' : 'xs'"
        :color="!state.allowed && !hovered ? 'neutral' : isBan ? 'error' : 'primary'"
        :variant="armed ? 'solid' : state.allowed ? (block ? 'solid' : 'subtle') : 'soft'"
        :icon="icon"
        :label="label"
        :block="block"
        :disabled="!state.allowed || busy"
        :class="[
          !state.allowed && 'pointer-events-none',
          block && 'justify-center font-semibold',
          spelled && 'text-[11px] font-medium',
        ]"
        :ui="{ leadingIcon: busy ? 'animate-spin' : undefined, label: 'truncate' }"
        :aria-label="armed ? question : `${isBan ? 'Ban' : 'Hover'} ${nameOf(championId)}`"
        @click="click"
      />
    </span>
  </UTooltip>
</template>
