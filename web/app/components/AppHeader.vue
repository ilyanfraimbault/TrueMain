<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

const items = computed<NavigationMenuItem[]>(() => [
  {
    label: 'Champions',
    icon: 'i-lucide-swords',
    to: '/champions',
    // Exclude the tier-list route so only one of the two champion entries
    // lights up at a time.
    active: isActive('/champions') && !isActive('/champions/tierlist'),
  },
  {
    label: 'Tier List',
    icon: 'i-lucide-trending-up',
    to: '/champions/tierlist',
    active: isActive('/champions/tierlist'),
  },
  {
    label: 'Matchup',
    icon: 'i-lucide-wand-sparkles',
    to: '/matchup',
    active: isActive('/matchup'),
  },
  {
    label: 'Truemains',
    icon: 'i-lucide-trophy',
    to: '/truemains',
    // Exact match only — the player profile pages (/truemains/{nameTag})
    // shouldn't light up the leaderboard entry in the nav.
    active: route.path === '/truemains',
  },
  {
    label: 'Favorites',
    icon: 'i-lucide-star',
    to: '/truemains/favorites',
    active: route.path === '/truemains/favorites',
  },
  {
    // The desktop companion (#1719), flagged while it is a beta.
    label: 'Desktop app',
    icon: 'i-lucide-monitor-down',
    to: '/download',
    active: route.path === '/download',
    badge: { label: 'Beta', color: 'primary', variant: 'subtle', size: 'sm' },
  },
])

// A floating bar rather than a full-width strip: the root is a transparent
// sticky frame and the container carries the only translucent material on the
// site, so page content visibly slides under it. The desktop menu is text-only
// — the icons stay in the mobile drawer, where the list needs them to scan.
const desktopItems = computed(() => items.value.map(({ icon: _icon, ...item }) => item))
</script>

<template>
  <UHeader
    title="TrueMain"
    :ui="{
      root: 'bg-transparent backdrop-blur-none border-b-0 h-auto pt-3 px-3',
      container: 'glass-bar rounded-2xl h-(--ui-header-height) max-w-6xl',
    }"
  >
    <template #title>
      <AppLogo class="text-lg" />
    </template>

    <UNavigationMenu
      :items="desktopItems"
      variant="link"
      :ui="{ link: 'text-[13px] font-medium' }"
    />

    <template #right>
      <AppSearch variant="button" shortcut />
    </template>

    <template #body>
      <UNavigationMenu
        :items="items"
        orientation="vertical"
        class="-mx-2.5"
      />
    </template>
  </UHeader>
</template>
