<script setup lang="ts">
// Variant B header: a floating bar whose edge catches the cursor light (`.b-spot`,
// see spotlight-engine.ts), with one light that glides under the nav — it rests
// on the active page and follows the pointer across the others. The mobile menu
// is a full-screen sheet of large, staggered links. Same destinations as
// `AppHeader`, same ⌘K search (owned by the desktop field instance).
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
const indicator = ref({ x: 0, w: 0, shown: false, placed: false })

function measure() {
  const links = navEl.value?.querySelectorAll<HTMLElement>('[data-nav-link]')
  const index = targetIndex.value
  if (!links || index === null || !links[index]) {
    indicator.value = { ...indicator.value, shown: false }
    return
  }
  const link = links[index]
  indicator.value = { ...indicator.value, x: link.offsetLeft, w: link.offsetWidth, shown: true }
}

watch(targetIndex, () => nextTick(measure))
watch(() => route.path, () => nextTick(measure))

const indicatorStyle = computed(() => ({
  transform: `translate3d(${indicator.value.x}px, 0, 0)`,
  width: `${indicator.value.w}px`,
}))

// ─── Bar state ──────────────────────────────────────────────────────────────
const scrolled = ref(false)
const menuOpen = ref(false)
let scrollFrame = 0
function onScroll() {
  if (scrollFrame) return
  scrollFrame = requestAnimationFrame(() => {
    scrollFrame = 0
    scrolled.value = window.scrollY > 12
  })
}

let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  measure()
  // Place the light under the active page first, then let it glide.
  requestAnimationFrame(() => {
    indicator.value = { ...indicator.value, placed: true }
  })
  if (navEl.value) {
    resizeObserver = new ResizeObserver(() => measure())
    resizeObserver.observe(navEl.value)
  }
  onScroll()
  window.addEventListener('scroll', onScroll, { passive: true })
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  window.removeEventListener('scroll', onScroll)
  cancelAnimationFrame(scrollFrame)
})
</script>

<template>
  <DesignBAmbient />
  <UHeader
    v-model:open="menuOpen"
    title="TrueMain"
    class="b-header"
    :class="{ 'is-scrolled': scrolled }"
    :ui="{
      root: 'bg-transparent backdrop-blur-none border-b-0 h-auto pt-3 px-3',
      container: 'b-bar b-spot h-(--ui-header-height) max-w-6xl rounded-2xl px-3 sm:px-4 lg:px-4',
      left: 'lg:flex-none',
      center: 'hidden lg:flex flex-1 justify-center',
      right: 'lg:flex-none gap-2',
      title: 'b-logo rounded-lg px-1.5 py-1 -mx-1.5',
    }"
  >
    <template #title>
      <AppLogo class="text-lg" />
    </template>

    <nav
      ref="navEl"
      aria-label="Main"
      class="b-nav"
      @pointerleave="hovered = null"
    >
      <span
        aria-hidden="true"
        class="b-nav__light"
        :class="{
          'is-shown': indicator.shown,
          'is-placed': indicator.placed,
          'is-resting': hovered === null || hovered === activeIndex,
        }"
        :style="indicatorStyle"
      />
      <NuxtLink
        v-for="(item, index) in items"
        :key="item.to"
        :to="item.to"
        data-nav-link
        class="b-nav__link"
        :class="{ 'is-active': item.active }"
        :aria-current="item.active ? 'page' : undefined"
        @pointerenter="hovered = index"
        @focus="hovered = index"
        @blur="hovered = null"
      >
        {{ item.label }}
        <span
          v-if="item.badge"
          class="b-nav__badge"
        >{{ item.badge }}</span>
      </NuxtLink>
    </nav>

    <template #right>
      <AppSearch
        variant="field"
        size="sm"
        shortcut
        placeholder="Search…"
        class="b-search-mini hidden lg:block"
      />
      <AppSearch
        variant="button"
        class="lg:hidden"
      />
    </template>

    <template #content="{ close }">
      <div class="b-menu">
        <div class="b-menu__top">
          <NuxtLink
            to="/"
            class="b-logo rounded-lg px-1.5 py-1 -mx-1.5"
            aria-label="TrueMain home"
            @click="close"
          >
            <AppLogo class="text-lg" />
          </NuxtLink>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            aria-label="Close menu"
            @click="close"
          />
        </div>

        <AppSearch
          variant="field"
          class="b-menu__search"
        />

        <nav
          aria-label="Main"
          class="b-menu__nav"
        >
          <ul>
            <li
              v-for="(item, index) in items"
              :key="item.to"
              :style="{ '--i': index }"
            >
              <NuxtLink
                :to="item.to"
                class="b-menu__link"
                :class="{ 'is-active': item.active }"
                :aria-current="item.active ? 'page' : undefined"
              >
                <UIcon
                  :name="item.icon"
                  class="b-menu__icon"
                />
                <span class="b-menu__label">{{ item.label }}</span>
                <span
                  v-if="item.badge"
                  class="b-nav__badge"
                >{{ item.badge }}</span>
                <UIcon
                  name="i-lucide-arrow-up-right"
                  class="b-menu__arrow"
                />
              </NuxtLink>
            </li>
          </ul>
        </nav>

        <p class="b-menu__foot">
          Champion builds from real mains.
        </p>
      </div>
    </template>
  </UHeader>
</template>
