<script setup lang="ts">
import { LAUNCHER_DESKTOP, LAUNCHER_PRIMARY, activeNavKey } from './nav-items'

// The rail on a phone (<768px): a bottom tab bar within thumb reach. Four
// destinations and search in the middle; the rest (tier list, matchup,
// favorites, the desktop app) sit in a "More" sheet, which lights when one of
// them is the current page. The ⌘K shortcut stays with the rail's search.
const route = useRoute()
const activeKey = computed(() => activeNavKey(route.path))

const byKey = new Map(LAUNCHER_PRIMARY.map(item => [item.key, item]))
const tabs = ['home', 'champions', 'truemains'].map(key => byKey.get(key)!)
const more = [...LAUNCHER_PRIMARY.filter(item => !['home', 'champions', 'truemains'].includes(item.key)), LAUNCHER_DESKTOP]
const moreActive = computed(() => more.some(item => item.key === activeKey.value))

const moreOpen = ref(false)
watch(() => route.fullPath, () => (moreOpen.value = false))

const legal = [
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]
</script>

<template>
  <nav
    class="e-tabbar"
    aria-label="Primary"
  >
    <NuxtLink
      v-for="item in tabs.slice(0, 2)"
      :key="item.key"
      :to="item.to"
      class="e-tab"
      :class="{ 'is-active': item.key === activeKey }"
      :aria-current="item.key === activeKey ? 'page' : undefined"
    >
      <UIcon
        :name="item.icon"
        class="e-tab__icon"
      />
      <span class="e-tab__label">{{ item.label }}</span>
    </NuxtLink>

    <div class="e-tab e-tab--search">
      <AppSearch
        variant="button"
        class="e-tab__search"
      />
    </div>

    <NuxtLink
      v-for="item in tabs.slice(2)"
      :key="item.key"
      :to="item.to"
      class="e-tab"
      :class="{ 'is-active': item.key === activeKey }"
      :aria-current="item.key === activeKey ? 'page' : undefined"
    >
      <UIcon
        :name="item.icon"
        class="e-tab__icon"
      />
      <span class="e-tab__label">{{ item.label }}</span>
    </NuxtLink>

    <button
      type="button"
      class="e-tab"
      :class="{ 'is-active': moreActive }"
      aria-haspopup="dialog"
      :aria-expanded="moreOpen"
      @click="moreOpen = true"
    >
      <UIcon
        name="i-lucide-layout-grid"
        class="e-tab__icon"
      />
      <span class="e-tab__label">More</span>
    </button>

    <UDrawer
      v-model:open="moreOpen"
      title="More"
      description="The rest of TrueMain."
      :ui="{ content: 'e-sheet', header: 'sr-only' }"
    >
      <template #body>
        <ul class="e-sheet__list">
          <li
            v-for="item in more"
            :key="item.key"
          >
            <NuxtLink
              :to="item.to"
              class="e-sheet__link"
              :class="{ 'is-active': item.key === activeKey }"
              :aria-current="item.key === activeKey ? 'page' : undefined"
            >
              <UIcon
                :name="item.icon"
                class="e-sheet__icon"
              />
              <span class="flex-1">{{ item.label }}</span>
              <span
                v-if="item.badge"
                class="e-badge"
              >{{ item.badge }}</span>
              <UIcon
                name="i-lucide-chevron-right"
                class="size-4 text-dimmed"
              />
            </NuxtLink>
          </li>
        </ul>
        <p class="e-sheet__legal">
          <NuxtLink
            v-for="link in legal"
            :key="link.to"
            :to="link.to"
          >
            {{ link.label }}
          </NuxtLink>
        </p>
      </template>
    </UDrawer>
  </nav>
</template>
