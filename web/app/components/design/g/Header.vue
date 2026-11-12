<script setup lang="ts">
// Variant G header: one floating pill, centred — the logo, the six destinations
// with a light that glides under the hovered (else the active) one, and the
// search (⌘K is owned by this instance, mounted on every page). Below `lg` the
// pill keeps the logo and search, and grows downward into a small grid of
// destination tiles when the menu opens: the bento, in miniature.
const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

interface NavItem {
  label: string
  icon: string
  to: string
  active: boolean
  badge?: string
}

const items = computed<NavItem[]>(() => [
  {
    label: 'Champions',
    icon: 'i-lucide-swords',
    to: '/champions',
    active: isActive('/champions') && !isActive('/champions/tierlist'),
  },
  { label: 'Tier List', icon: 'i-lucide-trending-up', to: '/champions/tierlist', active: isActive('/champions/tierlist') },
  { label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', active: isActive('/matchup') },
  { label: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', active: route.path === '/truemains' },
  { label: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites', active: route.path === '/truemains/favorites' },
  { label: 'Desktop app', icon: 'i-lucide-monitor-down', to: '/download', active: route.path === '/download', badge: 'Beta' },
])

// ─── The gliding light ──────────────────────────────────────────────────────
const navEl = ref<HTMLElement | null>(null)
const hovered = ref<number | null>(null)
const activeIndex = computed(() => items.value.findIndex(item => item.active))
const targetIndex = computed(() => hovered.value ?? (activeIndex.value >= 0 ? activeIndex.value : null))
const light = ref({ x: 0, w: 0, shown: false, placed: false })

function measure() {
  const links = navEl.value?.querySelectorAll<HTMLElement>('[data-nav-link]')
  const index = targetIndex.value
  const link = index === null ? null : links?.[index]
  if (!link || link.offsetWidth === 0) {
    light.value = { ...light.value, shown: false }
    return
  }
  light.value = { ...light.value, x: link.offsetLeft, w: link.offsetWidth, shown: true }
}

watch(targetIndex, () => nextTick(measure))
watch(() => route.path, () => nextTick(measure))

const lightStyle = computed(() => ({
  transform: `translate3d(${light.value.x}px, 0, 0)`,
  width: `${light.value.w}px`,
}))

// ─── Menu (below lg) ────────────────────────────────────────────────────────
const menuOpen = ref(false)
const menuButton = ref<HTMLButtonElement | null>(null)

function closeMenu(returnFocus = false) {
  if (!menuOpen.value) return
  menuOpen.value = false
  if (returnFocus) menuButton.value?.focus()
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') closeMenu(true)
}

watch(menuOpen, (open) => {
  if (open) document.addEventListener('keydown', onKeydown)
  else document.removeEventListener('keydown', onKeydown)
})
watch(() => route.fullPath, () => closeMenu())

// ─── Bar state: denser once the page scrolls under it ──────────────────────
const sentinel = ref<HTMLElement | null>(null)
const scrolled = ref(false)

let resizeObserver: ResizeObserver | null = null
let scrollObserver: IntersectionObserver | null = null
let wide: MediaQueryList | null = null
const onWideChange = (event: MediaQueryListEvent) => {
  if (event.matches) closeMenu()
  nextTick(measure)
}

onMounted(() => {
  measure()
  // Place the light under the active page first, then let it glide.
  requestAnimationFrame(() => {
    light.value = { ...light.value, placed: true }
  })
  if (navEl.value) {
    resizeObserver = new ResizeObserver(() => measure())
    resizeObserver.observe(navEl.value)
  }
  document.fonts?.ready.then(() => measure()).catch(() => {})
  if (sentinel.value) {
    scrollObserver = new IntersectionObserver(([entry]) => {
      scrolled.value = entry ? !entry.isIntersecting : false
    })
    scrollObserver.observe(sentinel.value)
  }
  wide = window.matchMedia('(min-width: 1024px)')
  wide.addEventListener('change', onWideChange)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  scrollObserver?.disconnect()
  wide?.removeEventListener('change', onWideChange)
  document.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <div
    ref="sentinel"
    class="g-header-sentinel"
    aria-hidden="true"
  />
  <header
    class="g-header"
    :class="{ 'is-scrolled': scrolled, 'is-open': menuOpen }"
  >
    <Transition name="g-scrim">
      <div
        v-if="menuOpen"
        class="g-scrim"
        aria-hidden="true"
        @click="closeMenu()"
      />
    </Transition>

    <div
      class="g-pill"
      data-g-spot
    >
      <div class="g-pill__row">
        <NuxtLink
          to="/"
          class="g-logo"
          aria-label="TrueMain home"
        >
          <AppLogo class="text-[17px]" />
        </NuxtLink>

        <span
          class="g-pill__rule"
          aria-hidden="true"
        />

        <nav
          ref="navEl"
          aria-label="Main"
          class="g-nav"
          @pointerleave="hovered = null"
        >
          <span
            aria-hidden="true"
            class="g-nav__light"
            :class="{
              'is-shown': light.shown,
              'is-placed': light.placed,
              'is-resting': hovered === null || hovered === activeIndex,
            }"
            :style="lightStyle"
          />
          <NuxtLink
            v-for="(item, index) in items"
            :key="item.to"
            :to="item.to"
            data-nav-link
            class="g-nav__link"
            :class="{ 'is-active': item.active }"
            :aria-current="item.active ? 'page' : undefined"
            @pointerenter="hovered = index"
            @focus="hovered = index"
            @blur="hovered = null"
          >
            {{ item.label }}
            <span
              v-if="item.badge"
              class="g-badge"
            >{{ item.badge }}</span>
          </NuxtLink>
        </nav>

        <span
          class="g-pill__rule g-pill__rule--end"
          aria-hidden="true"
        />

        <div class="g-pill__end">
          <AppSearch
            variant="button"
            shortcut
            class="g-pill__search"
          />
          <button
            ref="menuButton"
            type="button"
            class="g-burger"
            :aria-expanded="menuOpen"
            aria-controls="g-menu"
            :aria-label="menuOpen ? 'Close menu' : 'Open menu'"
            @click="menuOpen = !menuOpen"
          >
            <span
              class="g-burger__bars"
              aria-hidden="true"
            />
          </button>
        </div>
      </div>

      <div
        id="g-menu"
        class="g-menu"
        :inert="!menuOpen || undefined"
      >
        <div class="g-menu__clip">
          <nav
            aria-label="Main"
            class="g-menu__nav"
          >
            <ul class="g-menu__grid">
              <li
                v-for="(item, index) in items"
                :key="item.to"
                :style="{ '--i': index }"
              >
                <NuxtLink
                  :to="item.to"
                  class="g-menu__tile"
                  :class="{ 'is-active': item.active }"
                  :aria-current="item.active ? 'page' : undefined"
                >
                  <UIcon
                    :name="item.icon"
                    class="g-menu__icon"
                  />
                  <span class="g-menu__row">
                    <span class="g-menu__label">{{ item.label }}</span>
                    <span
                      v-if="item.badge"
                      class="g-badge"
                    >{{ item.badge }}</span>
                  </span>
                </NuxtLink>
              </li>
            </ul>
          </nav>
        </div>
      </div>
    </div>
  </header>
</template>
