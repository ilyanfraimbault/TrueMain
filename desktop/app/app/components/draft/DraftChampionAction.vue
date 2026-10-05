<script setup lang="ts">
/**
 * The control that writes one champion to champion select (#1909): "Hover" on
 * our pick, "Ban" on our ban. A click is the only way it acts.
 *
 * A ban is irreversible and costs the team one, so it always asks first; a
 * hover asks only when an ally is already showing that champion as their pick
 * intent, and says whose. Out of turn, or for a champion the client would
 * refuse, the control stays disabled with the reason in its tooltip.
 */
const props = withDefaults(defineProps<{
  championId: number
  size?: 'xs' | 'sm'
}>(), { size: 'xs' })

const { nameOf } = useChampionStatics()
const { writable, kind, shownKind, pending, availability, allyHovering, allyName, hover, ban } = useChampSelectActions()

const state = computed(() => availability(props.championId))
const ally = computed(() => allyHovering(props.championId))
const isBan = computed(() => shownKind.value === 'ban')
const busy = computed(() => pending.value?.championId === props.championId && pending.value.kind !== 'lock')

/** Whether the click opens a confirmation rather than writing at once. */
const asks = computed(() => kind.value === 'ban' || ally.value !== null)
const confirming = ref(false)

const question = computed(() => {
  const name = nameOf(props.championId)
  const intent = ally.value ? `${name} is ${allyName(ally.value)}'s pick intent.` : null
  if (kind.value === 'ban') return intent ? `${intent} Ban it anyway?` : `Ban ${name}?`
  return `${intent} Hover it anyway?`
})

function act() {
  confirming.value = false
  if (kind.value === 'ban') void ban(props.championId)
  else void hover(props.championId)
}

function onOpen(open: boolean) {
  if (!open) {
    confirming.value = false
    return
  }
  if (!state.value.allowed || pending.value) return
  if (asks.value) confirming.value = true
  else act()
}
</script>

<template>
  <UTooltip v-if="writable" :text="state.reason ?? undefined" :disabled="!state.reason" :content="{ side: 'top' }">
    <!-- A disabled button takes no pointer events: the span is what the tooltip hangs on. -->
    <span class="inline-flex" @click.stop>
      <UPopover :open="confirming" :content="{ side: 'top', sideOffset: 6 }" @update:open="onOpen">
        <UButton
          :size="size"
          :color="isBan ? 'error' : 'primary'"
          :variant="state.allowed ? 'subtle' : 'soft'"
          :icon="busy ? 'i-lucide-loader-circle' : isBan ? 'i-lucide-ban' : 'i-lucide-hand'"
          :label="state.reason === 'Hovered in the client' ? 'Hovered' : isBan ? 'Ban' : 'Hover'"
          :disabled="!state.allowed || busy"
          :class="!state.allowed && 'pointer-events-none'"
          :ui="{ leadingIcon: busy ? 'animate-spin' : undefined }"
          :aria-label="`${isBan ? 'Ban' : 'Hover'} ${nameOf(championId)}`"
        />

        <template #content>
          <div class="flex max-w-60 flex-col gap-2.5 p-3">
            <p class="text-sm text-highlighted">{{ question }}</p>
            <p v-if="kind === 'ban'" class="text-xs text-muted">A ban cannot be undone.</p>
            <div class="flex justify-end gap-2">
              <UButton size="xs" color="neutral" variant="ghost" label="Cancel" @click="confirming = false" />
              <UButton size="xs" :color="kind === 'ban' ? 'error' : 'primary'" :label="kind === 'ban' ? 'Ban' : 'Hover'" @click="act" />
            </div>
          </div>
        </template>
      </UPopover>
    </span>
  </UTooltip>
</template>
