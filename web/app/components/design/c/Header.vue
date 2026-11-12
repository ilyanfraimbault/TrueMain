<script setup lang="ts">
import { useScrollScene } from './scroll-scenes'

/**
 * Design exploration, variant C — the header: a thin full-width line of type,
 * transparent until the page moves, that steps out of the way while you read
 * down and comes back the moment you scroll up. A hairline under it fills with
 * the page's scroll progress. Under `lg` the links move into a full-screen menu
 * of large type.
 *
 * The site-wide film grain lives here too: the header is the one component of
 * this variant mounted on every page.
 */
const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

interface NavItem {
  label: string
  to: string
  icon: string
  active: boolean
  badge?: string
}

const items = computed<NavItem[]>(() => [
  { label: 'Champions', to: '/champions', icon: 'i-lucide-swords', active: isActive('/champions') && !isActive('/champions/tierlist') },
  { label: 'Tier List', to: '/champions/tierlist', icon: 'i-lucide-trending-up', active: isActive('/champions/tierlist') },
  { label: 'Matchup', to: '/matchup', icon: 'i-lucide-wand-sparkles', active: isActive('/matchup') },
  { label: 'Truemains', to: '/truemains', icon: 'i-lucide-trophy', active: route.path === '/truemains' },
  { label: 'Favorites', to: '/truemains/favorites', icon: 'i-lucide-star', active: route.path === '/truemains/favorites' },
  { label: 'Desktop app', to: '/download', icon: 'i-lucide-monitor-down', active: route.path === '/download', badge: 'Beta' },
])

const pad = (n: number) => String(n).padStart(2, '0')

// ─── Hide on scroll down, return on scroll up ─────────────────────────────
const hidden = ref(false)
const scrolled = ref(false)
const menuOpen = ref(false)
const focusInside = ref(false)
let lastY = 0
let travel = 0

useScrollScene({
  update({ y }) {
    const dy = y - lastY
    lastY = y
    scrolled.value = y > 8
    // Accumulate travel in one direction so a trackpad's jitter doesn't flicker it.
    travel = Math.sign(dy) === Math.sign(travel) ? travel + dy : dy
    if (y < 96 || menuOpen.value || focusInside.value) hidden.value = false
    else if (travel > 24) hidden.value = true
    else if (travel < -12) hidden.value = false
  },
})

watch(() => route.fullPath, () => {
  menuOpen.value = false
  hidden.value = false
})

// ─── Letterbox: two black bars retract each time a new page lands ─────────
// Client navigations only (the first paint has the hero's own entrance), and
// never under reduced motion (the CSS keeps it hidden there).
const letterboxOn = ref(false)
const letterboxKey = ref(0)
let letterboxTimer: ReturnType<typeof setTimeout> | undefined
const nuxtApp = useNuxtApp()
let firstPage = true
const offPageFinish = nuxtApp.hook('page:finish', () => {
  if (firstPage) {
    firstPage = false
    return
  }
  // A new key re-creates the bars, so the animation restarts from the top
  // even when two navigations land back to back.
  letterboxKey.value++
  letterboxOn.value = true
  clearTimeout(letterboxTimer)
  letterboxTimer = setTimeout(() => (letterboxOn.value = false), 1100)
})
onBeforeUnmount(() => {
  offPageFinish()
  clearTimeout(letterboxTimer)
})
</script>

<template>
  <header
    class="c-header"
    :class="{ 'is-hidden': hidden, 'is-scrolled': scrolled }"
    @focusin="focusInside = true; hidden = false"
    @focusout="focusInside = false"
  >
    <div class="c-header__bar">
      <NuxtLink
        to="/"
        class="c-header__logo"
        aria-label="TrueMain, home"
      >
        <AppLogo class="text-[17px]" />
      </NuxtLink>

      <nav
        class="c-header__nav"
        aria-label="Main"
      >
        <NuxtLink
          v-for="(item, index) in items"
          :key="item.to"
          :to="item.to"
          class="c-navlink"
          :class="{ 'is-active': item.active }"
          :aria-current="item.active ? 'page' : undefined"
        >
          <span
            class="c-navlink__idx"
            aria-hidden="true"
          >{{ pad(index + 1) }}</span>
          <span
            class="c-roll"
            :data-text="item.label"
          ><span>{{ item.label }}</span></span>
          <span
            v-if="item.badge"
            class="c-navlink__badge"
          >{{ item.badge }}</span>
        </NuxtLink>
      </nav>

      <div class="c-header__right">
        <AppSearch
          variant="button"
          shortcut
        />
        <button
          type="button"
          class="c-menu-btn"
          :aria-expanded="menuOpen"
          aria-haspopup="dialog"
          @click="menuOpen = true"
        >
          <span class="c-menu-btn__label">Menu</span>
          <span
            class="c-menu-btn__lines"
            aria-hidden="true"
          ><i /><i /></span>
        </button>
      </div>
    </div>
    <span
      class="c-header__progress"
      aria-hidden="true"
    />
  </header>

  <UModal
    v-model:open="menuOpen"
    fullscreen
    :transition="false"
    title="Menu"
    description="Site navigation"
    :ui="{ content: 'c-menu' }"
  >
    <template #content="{ close }">
      <div class="c-menu__inner">
        <div class="c-menu__top">
          <NuxtLink
            to="/"
            class="c-header__logo"
            aria-label="TrueMain, home"
          >
            <AppLogo class="text-[17px]" />
          </NuxtLink>
          <button
            type="button"
            class="c-menu-btn is-open"
            @click="close"
          >
            <span class="c-menu-btn__label">Close</span>
            <span
              class="c-menu-btn__lines"
              aria-hidden="true"
            ><i /><i /></span>
          </button>
        </div>

        <nav
          class="c-menu__nav"
          aria-label="Main"
        >
          <NuxtLink
            v-for="(item, index) in items"
            :key="item.to"
            :to="item.to"
            class="c-menu__link"
            :class="{ 'is-active': item.active }"
            :style="{ '--i': index }"
            :aria-current="item.active ? 'page' : undefined"
          >
            <span
              class="c-menu__idx"
              aria-hidden="true"
            >{{ pad(index + 1) }}</span>
            <span class="c-menu__label"><span>{{ item.label }}</span></span>
            <span
              v-if="item.badge"
              class="c-navlink__badge"
            >{{ item.badge }}</span>
            <UIcon
              name="i-lucide-arrow-up-right"
              class="c-menu__arrow"
            />
          </NuxtLink>
        </nav>

        <div class="c-menu__foot">
          <span class="c-mono">Champion builds from real mains</span>
          <span class="c-menu__legal">
            <NuxtLink to="/about">About</NuxtLink>
            <NuxtLink to="/privacy">Privacy</NuxtLink>
            <NuxtLink to="/terms">Terms</NuxtLink>
          </span>
        </div>
      </div>
    </template>
  </UModal>

  <div
    class="c-grain"
    aria-hidden="true"
  />
  <div
    v-if="letterboxOn"
    :key="letterboxKey"
    class="c-letterbox is-playing"
    aria-hidden="true"
  >
    <i /><i />
  </div>
</template>
