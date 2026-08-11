<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

/**
 * One side of the matchup: a champion portrait that *is* the control. Clicking
 * it opens a champion-only command palette (the same search interaction as
 * `AppSearch`, minus the truemain and browse groups).
 *
 * It replaces a `ChampionPicker` select. At this size the select was the loudest
 * thing in the stage — a wide field wrapping a single word — while the portrait
 * beside it, the thing you actually read, was decoration. Inverting the two puts
 * the picture in charge and moves the name to a caption. Search also beats a
 * dropdown for ~170 entries: the palette opens focused, with fuzzy matching, so
 * a champion is two keystrokes away instead of a scroll.
 *
 * Deliberately wordless. A `?` on an empty tile, a portrait on a filled one and
 * the swords between them already say "these two fight each other"; the
 * champion's own splash names it better than a caption repeating the name
 * underneath. The accessible names live on the trigger's `aria-label`, where
 * they cost nothing visually.
 *
 * `ChampionPicker` stays as-is for the eight draft slots in `TeamContext`, which
 * are a dense list of secondary inputs — a portrait grid there would out-shout
 * the matchup this component serves.
 */
const props = withDefaults(defineProps<{
  champions: ChampionStaticListItem[]
  championId: number | null
  /** Modal heading, and the accessible name of the trigger. */
  title: string
  /**
   * Ring the tile in the brand accent. Reserved for the player's own side: it
   * is a selected/owned state, not decoration.
   */
  accent?: boolean
  /** Offer a "clear" row in the palette and an inline clear button. */
  clearable?: boolean
}>(), {
  accent: false,
  clearable: true,
})

const emit = defineEmits<{
  'update:championId': [value: number | null]
}>()

const open = ref(false)
const term = ref('')

const champion = computed(() =>
  props.champions.find(entry => entry.championId === props.championId) ?? null)

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
  if (props.clearable && props.championId !== null) {
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
  emit('update:championId', championId)
  open.value = false
}

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})
</script>

<template>
  <!-- The clear button is a sibling of the trigger, not a child: a button
       inside a button is invalid HTML and the inner one never receives the
       click. -->
  <div class="relative">
    <button
      type="button"
      class="group relative flex size-24 cursor-pointer items-center justify-center overflow-hidden rounded-2xl ring-2 ring-inset transition-colors hover:ring-primary focus-visible:ring-primary focus-visible:outline-none sm:size-32"
      :class="[
        accent ? 'ring-primary/60' : 'ring-accented',
        champion ? '' : 'bg-muted',
      ]"
      :aria-label="champion ? `${title} — currently ${champion.name}` : title"
      @click="open = true"
    >
      <!-- No `width`/`height`: SkeletonImage turns them into an inline
           `style="width:…px"` to reserve layout space, and an inline style
           beats `size-full`'s class rule — the portrait would stay pinned at
           one breakpoint's size inside a tile that grows at `sm:`. The button
           already reserves the space, so `size-full` alone is correct here
           (same as AppSearch's palette rows). -->
      <SkeletonImage
        v-if="champion"
        :src="champion.iconUrl"
        :alt="''"
        class="size-full"
      />
      <span
        v-else
        class="font-mono text-4xl font-semibold text-dimmed transition-colors group-hover:text-default"
      >?</span>
    </button>
    <!-- `subtle`, not `solid`: on a dark-only theme `color="neutral"` solid is
         white, which put a bright disc on top of the champion art it sits on.
         Subtle is the elevated surface with a hairline — legible over any
         splash without competing with it. -->
    <UButton
      v-if="clearable && champion"
      icon="i-lucide-x"
      color="neutral"
      variant="subtle"
      size="xs"
      class="absolute -end-1.5 -top-1.5 rounded-full"
      :aria-label="`Clear ${champion.name}`"
      @click="select(null)"
    />

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
          <!-- SkeletonImage over the built-in avatar: the palette re-uses the
               same row DOM as the query narrows, so a plain <img> keeps
               painting the previous champion until the new icon decodes. -->
          <template #champion-leading="{ item }">
            <SkeletonImage
              :src="item.iconUrl"
              :alt="''"
              class="size-5 shrink-0 rounded-full"
            />
          </template>
        </LazyUCommandPalette>
      </template>
    </UModal>
  </div>
</template>
