<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

/**
 * The app's navigation, down the left edge: every section the site has, then
 * the ones that exist only here — champion select and the running game — and
 * the player at the foot. The gameflow phase still opens those two on its own
 * (`app.vue`); this is how the player gets everywhere else.
 */
const route = useRoute()
const { screen } = useLcuState()
const { status: recording } = useRecordings()
// A downloaded update stays one click away after its toast is gone.
const { readyVersion: updateVersion, installing: updating, restart: restartToUpdate } = useAppUpdate()

const isActive = (prefix: string) => route.path === prefix || route.path.startsWith(`${prefix}/`)

const LIVE = { label: 'Live', color: 'primary', variant: 'subtle', size: 'sm' } as const
const REC = { label: 'Rec', color: 'error', variant: 'subtle', size: 'sm' } as const

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
      badge: screen.value === 'draft' ? LIVE : undefined,
    },
    {
      label: 'Game',
      icon: 'i-lucide-crosshair',
      to: '/game',
      active: isActive('/game'),
      // Lit while a game runs, wherever the player is.
      badge: screen.value === 'in-game' ? LIVE : undefined,
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
          badge: recording.value.recordingGameId !== null ? REC : undefined,
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
    <div class="flex flex-col items-start px-5 pb-6 pt-5">
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
        :loading="updating"
        @click="restartToUpdate"
      />
    </div>

    <UNavigationMenu
      :items="items"
      orientation="vertical"
      highlight
      class="min-h-0 flex-1 overflow-y-auto px-3"
      :ui="{
        label: 'mt-3 px-2.5 pb-1.5 stat-label text-[10px]!',
        link: 'px-2.5 py-2 text-[13px] text-muted hover:text-highlighted data-active:text-highlighted',
        linkLeadingIcon: 'size-[18px]',
        separator: 'hidden',
      }"
    />

    <AppPlayerCard class="m-3" />
  </aside>
</template>
