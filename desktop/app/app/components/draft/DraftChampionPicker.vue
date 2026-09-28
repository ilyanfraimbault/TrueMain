<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import type { Lane } from '~/types/draft'
import { LANE_LABELS } from '~/types/draft'

/**
 * The champion to put on a slot of the board: a search over every champion,
 * the ones played on the slot's lane first (the tier list's order), the ones
 * already on the board left out.
 */
const props = defineProps<{
  /** What the slot is, in the player's words: "Your pick · Mid", "Enemy ban". */
  title: string
  /** The slot's lane, for a pick; a ban has none. */
  lane: Lane | null
  /** Champions already picked or banned. */
  exclude: Set<number>
}>()

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ pick: [championId: number] }>()

const { champions, portraitOf } = useChampionStatics()
const { laneEntries } = useTierList()

function item(id: number, name: string): CommandPaletteItem {
  return {
    id,
    label: name,
    avatar: { src: portraitOf(id) ?? undefined, alt: name },
    onSelect: () => {
      emit('pick', id)
      open.value = false
    },
  }
}

const groups = computed<CommandPaletteGroup<CommandPaletteItem>[]>(() => {
  const available = [...champions.value.values()].filter(champion => !props.exclude.has(champion.id))
  const all = available.sort((a, b) => a.name.localeCompare(b.name)).map(champion => item(champion.id, champion.name))
  if (!props.lane) return [{ id: 'all', label: 'Champions', items: all }]

  const onLane = laneEntries(props.lane)
    .filter(entry => !props.exclude.has(entry.championId) && champions.value.has(entry.championId))
    .map(entry => item(entry.championId, champions.value.get(entry.championId)!.name))
  return [
    { id: 'lane', label: `Played ${LANE_LABELS[props.lane]}`, items: onLane },
    { id: 'all', label: 'All champions', items: all },
  ]
})
</script>

<template>
  <UModal v-model:open="open" :title="title" :ui="{ content: 'max-w-md' }">
    <template #content>
      <UCommandPalette
        :groups="groups"
        :placeholder="`${title} — search a champion…`"
        class="h-96"
        close
        @update:open="open = $event"
      />
    </template>
  </UModal>
</template>
