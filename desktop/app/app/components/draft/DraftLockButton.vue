<script setup lang="ts">
/**
 * "Lock in <Champion>" (#1909): the one button that locks our pick, kept in
 * the strip by the clock and away from the cards, so a misclick on a card can
 * never lock. It locks only what the client already shows hovered, and only
 * when clicked — never as the timer runs out: what the client does then is
 * the client's rule, which the hint under it leaves to the client.
 */
const { nameOf } = useChampionStatics()
const { writable, kind, hovered, canLock, pending, allyHovering, allyName, lock } = useChampSelectActions()

const shown = computed(() => writable.value && kind.value === 'pick')
const busy = computed(() => pending.value?.kind === 'lock')
const ally = computed(() => (hovered.value === null ? null : allyHovering(hovered.value)))
const confirming = ref(false)

const reason = computed(() => {
  if (hovered.value === null) return 'Hover a champion first: the lock commits what the client shows hovered'
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
  <UTooltip v-if="shown" :text="reason" :content="{ side: 'bottom' }">
    <span class="inline-flex">
      <UPopover :open="confirming" :content="{ side: 'bottom', sideOffset: 6 }" @update:open="onOpen">
        <UButton
          size="sm"
          :icon="busy ? 'i-lucide-loader-circle' : 'i-lucide-lock'"
          :label="hovered !== null ? `Lock in ${nameOf(hovered)}` : 'Lock in'"
          :disabled="!canLock || busy"
          :class="!canLock && 'pointer-events-none'"
          :ui="{ leadingIcon: busy ? 'animate-spin' : undefined }"
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
