<script setup lang="ts">
/**
 * "Lock in" (#1909), under the card of the champion the client shows hovered
 * on our pick (#2465): a click on a card selects it, this button commits it.
 * Its own button, never the card's click, so a misclick on a card cannot lock.
 * It locks only what the client already shows hovered, and only when clicked —
 * never as the timer runs out: what the client does then is the client's rule,
 * which the hint on it leaves to the client. An ally's pick intent asks first.
 *
 * `block` is the bar under a suggestion card; without it, the compact button of
 * a search row.
 */
withDefaults(defineProps<{ block?: boolean }>(), { block: false })

const { nameOf } = useChampionStatics()
const { writable, kind, hovered, canLock, pending, allyHovering, allyName, lock } = useChampSelectActions()

const shown = computed(() => writable.value && kind.value === 'pick' && hovered.value !== null)
const busy = computed(() => pending.value?.kind === 'lock')
const ally = computed(() => (hovered.value === null ? null : allyHovering(hovered.value)))
const confirming = ref(false)

const reason = computed(() => {
  if (!canLock.value) return 'Your pick turn has not started'
  return 'TrueMain never locks for you: when the timer runs out, what happens to your hover is the client\'s rule'
})

function act() {
  confirming.value = false
  void lock()
}

function onOpen(open: boolean) {
  if (!open) {
    confirming.value = false
    return
  }
  if (!canLock.value || pending.value) return
  if (ally.value) confirming.value = true
  else act()
}
</script>

<template>
  <UTooltip v-if="shown" :text="reason" :content="{ side: 'top' }">
    <!-- A disabled button takes no pointer events: the span is what the tooltip hangs on. -->
    <span class="inline-flex" :class="block && 'w-full'">
      <UPopover :open="confirming" :content="{ side: 'top', sideOffset: 6 }" @update:open="onOpen">
        <UButton
          :size="block ? 'sm' : 'xs'"
          :icon="busy ? 'i-lucide-loader-circle' : 'i-lucide-lock'"
          label="Lock in"
          :block="block"
          :disabled="!canLock || busy"
          :class="[!canLock && 'pointer-events-none', block && 'justify-center font-semibold']"
          :ui="{ leadingIcon: busy ? 'animate-spin' : undefined }"
          :aria-label="hovered !== null ? `Lock in ${nameOf(hovered)}` : 'Lock in'"
        />

        <template #content>
          <div v-if="ally && hovered !== null" class="flex max-w-60 flex-col gap-2.5 p-3">
            <p class="text-sm text-highlighted">{{ nameOf(hovered) }} is {{ allyName(ally) }}'s pick intent. Lock it in anyway?</p>
            <div class="flex justify-end gap-2">
              <UButton size="xs" color="neutral" variant="ghost" label="Cancel" @click="confirming = false" />
              <UButton size="xs" label="Lock in" @click="act" />
            </div>
          </div>
        </template>
      </UPopover>
    </span>
  </UTooltip>
</template>
