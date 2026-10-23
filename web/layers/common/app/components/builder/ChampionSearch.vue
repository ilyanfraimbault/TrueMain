<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * The champion search every slot of the matchup board opens: a champion-only
 * command palette (the same search interaction as `AppSearch`, minus the
 * truemain and browse groups). Search beats a dropdown for ~170 entries: the
 * palette opens focused, with fuzzy matching, so a champion is two keystrokes
 * away instead of a scroll.
 *
 * One instance for the whole board: the board says which slot it is filling
 * (`title`, `championId`) and receives the pick.
 */
const props = defineProps<{
  champions: ChampionStaticListItem[]
  /** The slot's current champion, offered as a "Clear" row. */
  championId: number | null
  /** Modal heading. */
  title: string
}>()

const emit = defineEmits<{ select: [championId: number | null] }>()

const open = defineModel<boolean>('open', { required: true })
const term = ref('')

type SlotItem = CommandPaletteItem & { iconUrl?: string | null }

// Sorted and mapped once rather than on every keystroke: the palette re-reads
// `groups` as the query narrows, and re-sorting ~170 names through
// localeCompare each time would be needless work.
const championItems = computed<SlotItem[]>(() =>
  [...props.champions]
    .sort((a, b) => a.name.localeCompare(b.name, 'en'))
    .map(entry => ({
      label: entry.name,
      slot: 'champion',
      iconUrl: entry.iconUrl,
      onSelect: () => select(entry.championId),
    })),
)

// `ignoreFilter` keeps the reset reachable while a term is typed, so a mistyped
// name can still be undone in one step instead of clearing the box first.
const groups = computed<CommandPaletteGroup<SlotItem>[]>(() => {
  const list: CommandPaletteGroup<SlotItem>[] = []
  if (props.championId !== null) {
    list.push({
      id: 'clear',
      label: 'Current',
      ignoreFilter: true,
      items: [{ label: 'Clear', icon: 'i-lucide-x', onSelect: () => select(null) }],
    })
  }
  list.push({ id: 'champions', label: 'Champions', items: championItems.value })
  return list
})

function select(championId: number | null) {
  emit('select', championId)
  open.value = false
}

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})
</script>

<template>
  <UModal
    v-model:open="open"
    :title="title"
    description="Search the champion roster"
    :ui="{ content: 'sm:max-w-lg' }"
  >
    <template #content>
      <!-- Lazy on purpose, same reason as AppSearch (#832): a static
           reference would pull the palette chunk and Fuse into the page's
           initial JS, and most visits never open it. -->
      <LazyUCommandPalette
        v-model:search-term="term"
        :groups="groups"
        placeholder="Search a champion…"
        icon="i-lucide-search"
        class="h-96"
        :close="{ onClick: () => { open = false } }"
      >
        <template #champion-leading="{ item }">
          <SearchItemAvatar :src="item.iconUrl" />
        </template>
      </LazyUCommandPalette>
    </template>
  </UModal>
</template>
