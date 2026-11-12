<script setup lang="ts">
import { isNavigationFailure } from 'vue-router'
import { startDockLens } from './dock-lens'
import { dialogOpen, isEditableTarget } from './search-context'

// Variant F ("Dock"): the site's only navigation, floating at the bottom of the
// viewport. On a desktop it is a macOS dock — tiles that magnify under the
// cursor, a label above the one you point at, a dot under the current page,
// the search at the end. Under 768px the same list becomes a tab bar.
//
// Keyboard-first: every page tile answers a digit (1–6) anywhere outside a
// text field, and the search tile is the one `AppSearch` that owns ⌘K.
defineProps<{
  /** Scrolled down: the dock sinks a little (the tab bar slides away). */
  receded: boolean
}>()

const route = useRoute()
const router = useRouter()

function isUnder(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

interface DockItem {
  label: string
  /** The tab bar's caption, where the full label would not fit. */
  caption: string
  icon: string
  to: string
  key: string
  active: boolean
  beta?: boolean
}

const items = computed<DockItem[]>(() => [
  {
    label: 'Champions',
    caption: 'Champions',
    icon: 'i-lucide-swords',
    to: '/champions',
    key: '1',
    active: isUnder('/champions') && !isUnder('/champions/tierlist'),
  },
  { label: 'Tier List', caption: 'Tier list', icon: 'i-lucide-trending-up', to: '/champions/tierlist', key: '2', active: isUnder('/champions/tierlist') },
  { label: 'Matchup', caption: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', key: '3', active: isUnder('/matchup') },
  // Exact matches: a player's profile under /truemains/… is not the leaderboard.
  { label: 'Truemains', caption: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', key: '4', active: route.path === '/truemains' },
  { label: 'Favorites', caption: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites', key: '5', active: route.path === '/truemains/favorites' },
  { label: 'Desktop app', caption: 'Desktop', icon: 'i-lucide-monitor-down', to: '/download', key: '6', active: route.path === '/download', beta: true },
])

const homeActive = computed(() => route.path === '/')

const listEl = ref<HTMLElement | null>(null)
const shelfEl = ref<HTMLElement | null>(null)
const searchHost = ref<HTMLElement | null>(null)

/** Click the palette's own trigger: one instance, one modal, one ⌘K. */
function openPalette() {
  searchHost.value?.querySelector('button')?.click()
}
defineExpose({ openPalette })

function onKeydown(event: KeyboardEvent) {
  if (event.defaultPrevented || event.repeat || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return
  const item = items.value.find(entry => entry.key === event.key)
  if (!item || isEditableTarget(event.target) || dialogOpen()) return
  event.preventDefault()
  router.push(item.to).catch((error) => {
    if (!isNavigationFailure(error)) throw error
  })
}

let stopLens: (() => void) | null = null
let lensQuery: MediaQueryList | null = null

function syncLens() {
  const wanted = lensQuery?.matches === true
    && !window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (wanted && !stopLens && listEl.value && shelfEl.value) {
    stopLens = startDockLens({ list: listEl.value, shelf: shelfEl.value })
  }
  else if (!wanted && stopLens) {
    stopLens()
    stopLens = null
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
  lensQuery = window.matchMedia('(min-width: 768px) and (hover: hover) and (pointer: fine)')
  lensQuery.addEventListener('change', syncLens)
  syncLens()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  lensQuery?.removeEventListener('change', syncLens)
  stopLens?.()
  stopLens = null
})
</script>

<template>
  <nav
    class="f-dock"
    :class="{ 'is-receded': receded }"
    aria-label="Main"
  >
    <div class="f-dock__body">
      <span
        ref="shelfEl"
        class="f-dock__shelf"
        aria-hidden="true"
      />
      <ul
        ref="listEl"
        class="f-dock__list"
      >
        <li
          class="f-dock__slot f-dock__slot--home"
          :class="{ 'is-active': homeActive }"
          data-dock-slot
        >
          <NuxtLink
            to="/"
            class="f-dock__tile"
            data-dock-tile
            aria-label="Home"
            :aria-current="homeActive ? 'page' : undefined"
          >
            <DesignFMark class="f-dock__mark" />
          </NuxtLink>
          <span
            class="f-dock__label"
            data-dock-label
            aria-hidden="true"
          >Home</span>
          <span
            class="f-dock__dot"
            aria-hidden="true"
          />
        </li>

        <li
          class="f-dock__sep"
          data-dock-slot
          data-dock-sep
          aria-hidden="true"
        />

        <li
          v-for="item in items"
          :key="item.to"
          class="f-dock__slot"
          :class="{ 'is-active': item.active }"
          data-dock-slot
        >
          <NuxtLink
            :to="item.to"
            class="f-dock__tile"
            data-dock-tile
            :aria-label="item.beta ? `${item.label} (beta)` : item.label"
            :aria-current="item.active ? 'page' : undefined"
            :aria-keyshortcuts="item.key"
          >
            <UIcon
              :name="item.icon"
              class="f-dock__icon"
            />
            <span
              class="f-dock__caption"
              aria-hidden="true"
            >{{ item.caption }}</span>
            <span
              v-if="item.beta"
              class="f-dock__beta"
              aria-hidden="true"
            >Beta</span>
          </NuxtLink>
          <span
            class="f-dock__label"
            data-dock-label
            aria-hidden="true"
          >
            {{ item.label }}
            <span
              v-if="item.beta"
              class="f-dock__label-beta"
            >Beta</span>
            <kbd class="f-key">{{ item.key }}</kbd>
          </span>
          <span
            class="f-dock__dot"
            aria-hidden="true"
          />
        </li>

        <li
          class="f-dock__sep f-dock__sep--end"
          data-dock-slot
          data-dock-sep
          aria-hidden="true"
        />

        <li
          class="f-dock__slot f-dock__slot--search"
          data-dock-slot
        >
          <div
            ref="searchHost"
            class="f-dock__tile f-dock__search"
            data-dock-tile
          >
            <AppSearch
              variant="button"
              shortcut
            />
          </div>
          <span
            class="f-dock__label"
            data-dock-label
            aria-hidden="true"
          >
            Search
            <ClientOnly>
              <span class="f-key-group">
                <UKbd
                  value="meta"
                  size="sm"
                  class="f-key"
                />
                <UKbd
                  value="K"
                  size="sm"
                  class="f-key"
                />
              </span>
            </ClientOnly>
          </span>
        </li>
      </ul>
    </div>
  </nav>
</template>
