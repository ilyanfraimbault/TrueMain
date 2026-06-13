<script setup lang="ts">
import type { CommandPaletteGroup } from '@nuxt/ui'
import { isNavigationFailure } from 'vue-router'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Hero search: a search-field-looking trigger that opens a command palette
// over the full champion roster, plus quick links to the list pages. The
// palette lives in a modal so it never participates in SSR — the trigger is
// the only part of this component in the server HTML.
const props = defineProps<{
  champions: ChampionStaticListItem[]
}>()

const open = ref(false)
const router = useRouter()

function go(path: string) {
  open.value = false
  // Swallow only aborted/redirected navigations (guards, duplicate pushes);
  // let an unexpected error surface instead of disappearing silently.
  router.push(path).catch((error) => {
    if (!isNavigationFailure(error)) throw error
  })
}

const groups = computed<CommandPaletteGroup[]>(() => [
  {
    id: 'champions',
    label: 'Champions',
    items: [...props.champions]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(champion => ({
        label: champion.name,
        avatar: { src: champion.iconUrl, alt: champion.name },
        onSelect: () => go(`/champions/${champion.championId}`),
      })),
  },
  {
    id: 'browse',
    label: 'Browse',
    items: [
      {
        label: 'Champion tier list',
        icon: 'i-lucide-swords',
        onSelect: () => go('/champions'),
      },
      {
        label: 'Truemains leaderboard',
        icon: 'i-lucide-trophy',
        onSelect: () => go('/truemains'),
      },
    ],
  },
])

defineShortcuts({
  meta_k: () => {
    open.value = !open.value
  },
})
</script>

<template>
  <div>
    <button
      type="button"
      class="group flex h-14 w-full items-center gap-3 rounded-2xl border border-default bg-default/60 px-5 text-left shadow-lg shadow-black/5 backdrop-blur-md transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:shadow-black/20"
      aria-label="Search a champion"
      @click="open = true"
    >
      <UIcon
        name="i-lucide-search"
        class="size-5 shrink-0 text-dimmed transition-colors group-hover:text-primary"
      />
      <span class="flex-1 truncate text-base text-dimmed">
        Search a champion…
      </span>
      <span class="hidden items-center gap-0.5 sm:flex">
        <UKbd value="meta" />
        <UKbd value="K" />
      </span>
    </button>

    <UModal
      v-model:open="open"
      title="Search"
      description="Search a champion or jump to a page"
      :ui="{ content: 'sm:max-w-xl' }"
    >
      <template #content>
        <UCommandPalette
          :groups="groups"
          placeholder="Search a champion…"
          icon="i-lucide-search"
          class="h-96"
          :close="{ onClick: () => { open = false } }"
        />
      </template>
    </UModal>
  </div>
</template>
