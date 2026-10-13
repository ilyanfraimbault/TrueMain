<script setup lang="ts">
// What opens the search palette (`AppSearch`): a search-bar look in a page
// (`field`) or a compact icon in the header (`button`). When the palette filters
// the page by a champion, the field shows that champion with a primary-tinted
// border instead of the placeholder — otherwise nothing on the page would say a
// filter is active — and a clear button sits on its right: a sibling, not a
// nested button, which would be invalid HTML.
const props = withDefaults(defineProps<{
  variant?: 'field' | 'button'
  /**
   * Field height: `sm` for a narrow column (the app's build page), `md` the
   * default (the leaderboard), `lg` the homepage hero — taller, more rounded,
   * larger text and the ⌘K hint.
   */
  size?: 'sm' | 'md' | 'lg'
  placeholder: string
  /** The accessible name, which the placeholder may abbreviate. */
  label: string
  activeChampion?: { name: string, iconUrl: string | null } | null
}>(), {
  variant: 'field',
  size: 'md',
  activeChampion: null,
})

const emit = defineEmits<{ open: [], clear: [] }>()

const SIZE_CLASS = {
  sm: 'h-8 gap-2 rounded-md px-2.5 text-sm',
  md: 'h-12 gap-3 rounded-xl px-4 text-sm',
  lg: 'h-14 gap-3 rounded-2xl px-5 text-base',
} as const

const ariaLabel = computed(() => (props.activeChampion
  ? `Filtering by ${props.activeChampion.name} — ${props.label.toLowerCase()}`
  : props.label))
</script>

<template>
  <div v-if="variant === 'field'" class="relative">
    <button
      type="button"
      class="group keycap flex w-full items-center border bg-muted text-left transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      :class="[SIZE_CLASS[size], activeChampion ? 'border-primary/50 pr-12' : 'border-default']"
      :aria-label="ariaLabel"
      @click="emit('open')"
    >
      <UIcon
        name="i-lucide-search"
        class="shrink-0 text-dimmed transition-colors group-hover:text-primary"
        :class="size === 'sm' ? 'size-4' : 'size-5'"
      />
      <span v-if="activeChampion" class="flex min-w-0 flex-1 items-center gap-2">
        <SearchItemAvatar :src="activeChampion.iconUrl" />
        <span class="truncate font-medium text-highlighted">{{ activeChampion.name }}</span>
      </span>
      <span v-else class="flex-1 truncate text-dimmed">{{ placeholder }}</span>
      <!-- ⌘K hint on the prominent hero bar only. The shortcut itself is owned
           by the always-mounted header instance; pressing ⌘K opens the
           (identical) unified search, and `usingInput` blocks it while a modal
           input is focused, so it can't stack a second modal.

           `<ClientOnly>` because `UKbd value="meta"` resolves the modifier from
           the *platform* (`⌘` on macOS, `Ctrl` everywhere else), which the
           server cannot know: it rendered an empty key and the client rendered
           `Ctrl`, a hydration text mismatch on every non-Mac visit to the
           homepage. No fallback on purpose — the hint advertises a shortcut
           that does not work until the handler is mounted. -->
      <ClientOnly>
        <span v-if="size === 'lg'" class="hidden items-center gap-0.5 sm:flex">
          <UKbd value="meta" class="font-mono" />
          <UKbd value="K" class="font-mono" />
        </span>
      </ClientOnly>
    </button>
    <UButton
      v-if="activeChampion"
      icon="i-lucide-x"
      color="neutral"
      variant="ghost"
      size="xs"
      class="absolute right-2 top-1/2 -translate-y-1/2"
      :aria-label="`Clear champion filter (${activeChampion.name})`"
      @click="emit('clear')"
    />
  </div>

  <UButton
    v-else
    icon="i-lucide-search"
    color="neutral"
    variant="ghost"
    :aria-label="label"
    @click="emit('open')"
  />
</template>
