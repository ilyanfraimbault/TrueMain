<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

/**
 * The app's navigation, down the left edge: every section the site has, then
 * the ones that exist only here — champion select and the running game — and
 * the player at the foot. The gameflow phase still opens those two on its own
 * (`app.vue`); this is how the player gets everywhere else.
 *
 * In a narrow window (`useNarrowWindow`) it folds into a rail of icons: each
 * entry's name in a tooltip, and the "Live" / "Rec" badges as dots on their
 * icons, their words in the tooltip.
 */
const route = useRoute()
const narrow = useNarrowWindow()
const { screen } = useLcuState()
const { status: recording } = useRecordings()
// A downloaded update stays one click away after its toast is gone.
const { readyVersion: updateVersion, installing: updating, stalled: updateStalled, restart: restartToUpdate } = useAppUpdate()

const isActive = (prefix: string) => route.path === prefix || route.path.startsWith(`${prefix}/`)

const LIVE = { label: 'Live', color: 'primary', variant: 'subtle', size: 'sm' } as const
const REC = { label: 'Rec', color: 'error', variant: 'subtle', size: 'sm' } as const

/** An entry lit by `badge`: the badge itself in full, a dot on the icon in the rail. */
function lit(label: string, badge: typeof LIVE | typeof REC, on: boolean): Partial<NavigationMenuItem> {
  if (!on) return {}
  if (!narrow.value) return { badge }
  return { chip: { color: badge.color }, tooltip: { text: `${label} · ${badge.label}` } }
}

const items = computed<NavigationMenuItem[][]>(() => [
  [
    { label: 'TrueMain', type: 'label' },
    { label: 'Dashboard', icon: 'i-lucide-house', to: '/', active: route.path === '/' },
    { label: 'Champions', icon: 'i-lucide-swords', to: '/champions', active: isActive('/champions') },
    { label: 'Tier list', icon: 'i-lucide-trending-up', to: '/tierlist', active: isActive('/tierlist') },
    { label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', active: isActive('/matchup') },
    { label: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', active: isActive('/truemains') },
    { label: 'Favorites', icon: 'i-lucide-star', to: '/favorites', active: isActive('/favorites') },
  ],
  [
    { label: 'In game', type: 'label' },
    {
      label: 'Champ select',
      icon: 'i-lucide-sparkles',
      to: '/draft',
      active: isActive('/draft'),
      // Lit while the client is in champion select, wherever the player is;
      // the rest of the time the page is a draft to play by hand.
      ...lit('Champ select', LIVE, screen.value === 'draft'),
    },
    {
      label: 'Game',
      icon: 'i-lucide-crosshair',
      to: '/game',
      active: isActive('/game'),
      // Lit while a game runs, wherever the player is.
      ...lit('Game', LIVE, screen.value === 'in-game'),
    },
    { label: 'Overlay', icon: 'i-lucide-layers', to: '/overlay', active: route.path === '/overlay' },
    // Only once the shell has answered for recording: a build without it has no page to open.
    ...(recording.value
      ? [{
          label: 'Recordings',
          icon: 'i-lucide-clapperboard',
          to: '/recordings',
          active: isActive('/recordings'),
          // Lit while a game is being recorded, wherever the player is.
          ...lit('Recordings', REC, recording.value.recordingGameId !== null),
        }]
      : []),
  ],
])

const version = ref<string | null>(null)
onMounted(async () => {
  if (!insideTauri()) return
  const { getVersion } = await import('@tauri-apps/api/app')
  version.value = await getVersion()
})
</script>

<template>
  <aside class="flex h-full flex-col">
    <div v-if="narrow" class="flex flex-col items-center gap-3 pb-4 pt-5">
      <NuxtLink to="/" aria-label="Dashboard">
        <AppMark class="size-7" />
      </NuxtLink>
      <UTooltip v-if="updateVersion" :text="`Update to v${updateVersion}`">
        <UButton
          icon="i-lucide-download"
          size="xs"
          variant="soft"
          :aria-label="`Update to v${updateVersion}`"
          :loading="updating && !updateStalled"
          @click="restartToUpdate"
        />
      </UTooltip>
    </div>
    <div v-else class="flex flex-col items-start px-5 pb-6 pt-5">
      <NuxtLink to="/" aria-label="Dashboard">
        <AppWordmark class="text-xl" />
      </NuxtLink>
      <span class="ml-7 stat-label">{{ version ? `App v${version}` : 'Companion' }}</span>
      <UButton
        v-if="updateVersion"
        :label="`Update to v${updateVersion}`"
        icon="i-lucide-download"
        size="xs"
        variant="soft"
        class="ml-6 mt-2"
        :loading="updating && !updateStalled"
        @click="restartToUpdate"
      />
    </div>

    <UNavigationMenu
      :items="items"
      orientation="vertical"
      :collapsed="narrow"
      tooltip
      highlight
      class="min-h-0 flex-1 overflow-y-auto"
      :class="narrow ? 'px-2' : 'px-3'"
      :ui="{
        label: 'mt-3 px-2.5 pb-1.5 stat-label text-[10px]!',
        link: 'px-2.5 py-2 text-[13px] text-muted hover:text-highlighted data-active:text-highlighted',
        linkLeadingIcon: 'size-[18px]',
        separator: narrow ? 'my-2' : 'hidden',
      }"
    />

    <AppPlayerCard :compact="narrow" :class="narrow ? 'mx-auto mb-3' : 'm-3'" />
  </aside>
</template>
