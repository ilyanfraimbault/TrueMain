<script setup lang="ts">
// Variant A — a floating liquid-glass capsule. Content scrolls under it and is
// bent along its rim; a glass droplet glides to whichever link is hovered and
// settles back on the active one. Compacts once the page has scrolled.
const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

const items = computed(() => [
  { label: 'Champions', icon: 'i-lucide-swords', to: '/champions', active: isActive('/champions') && !isActive('/champions/tierlist') },
  { label: 'Tier List', icon: 'i-lucide-trending-up', to: '/champions/tierlist', active: isActive('/champions/tierlist') },
  { label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', active: isActive('/matchup') },
  { label: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', active: route.path === '/truemains' },
  { label: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites', active: route.path === '/truemains/favorites' },
  { label: 'Desktop app', icon: 'i-lucide-monitor-down', to: '/download', active: route.path === '/download', badge: 'Beta' },
])

// ─── Scroll compaction ─────────────────────────────────────────────────────
const scrolled = ref(false)
onMounted(() => {
  const onScroll = () => (scrolled.value = window.scrollY > 24)
  onScroll()
  window.addEventListener('scroll', onScroll, { passive: true })
  onBeforeUnmount(() => window.removeEventListener('scroll', onScroll))
})

// ─── The droplet ───────────────────────────────────────────────────────────
const navEl = ref<HTMLElement | null>(null)
const linkEls = ref<HTMLElement[]>([])
const hovered = ref<number | null>(null)
const droplet = reactive({ x: 0, w: 0, visible: false })

function place(index: number | null) {
  const nav = navEl.value
  const target = index === null ? null : linkEls.value[index]
  if (!nav || !target) {
    droplet.visible = false
    return
  }
  const navRect = nav.getBoundingClientRect()
  const rect = target.getBoundingClientRect()
  droplet.x = rect.left - navRect.left
  droplet.w = rect.width
  droplet.visible = true
}

const activeIndex = computed(() => items.value.findIndex(item => item.active))

function settle() {
  place(hovered.value ?? (activeIndex.value >= 0 ? activeIndex.value : null))
}

watch([hovered, activeIndex], () => nextTick(settle))
onMounted(() => {
  nextTick(settle)
  // Fonts and the compaction both move the links.
  const ro = new ResizeObserver(() => settle())
  if (navEl.value) ro.observe(navEl.value)
  onBeforeUnmount(() => ro.disconnect())
})

// ─── Mobile sheet ──────────────────────────────────────────────────────────
const menuOpen = ref(false)
watch(() => route.fullPath, () => (menuOpen.value = false))
</script>

<template>
  <header
    class="dsa-header sticky top-0 z-50 flex justify-center px-3 pt-3"
    :data-scrolled="scrolled || undefined"
  >
    <FxLiquidGlass
      :radius="999"
      :bezel="16"
      :depth="18"
      :blur="scrolled ? 2.4 : 0.8"
      class="dsa-capsule flex w-full items-center gap-2 pl-4 pr-2"
    >
      <NuxtLink
        to="/"
        class="flex shrink-0 items-center rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        aria-label="TrueMain home"
      >
        <AppLogo class="text-lg" />
      </NuxtLink>

      <nav
        ref="navEl"
        class="relative mx-auto hidden items-center lg:flex"
        aria-label="Main"
        @pointerleave="hovered = null"
      >
        <span
          aria-hidden="true"
          class="dsa-droplet"
          :style="{ transform: `translateX(${droplet.x}px)`, width: `${droplet.w}px`, opacity: droplet.visible ? 1 : 0 }"
        />
        <NuxtLink
          v-for="(item, index) in items"
          :key="item.to"
          :ref="(el: any) => { if (el?.$el) linkEls[index] = el.$el }"
          :to="item.to"
          class="dsa-navlink relative z-[1] flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-[13px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          :class="item.active ? 'text-highlighted' : 'text-muted hover:text-highlighted'"
          :aria-current="item.active ? 'page' : undefined"
          @pointerenter="hovered = index"
          @focus="hovered = index"
          @blur="hovered = null"
        >
          {{ item.label }}
          <span
            v-if="item.badge"
            class="rounded-full bg-primary/15 px-1.5 py-px text-[10px] font-semibold text-primary"
          >{{ item.badge }}</span>
        </NuxtLink>
      </nav>

      <div class="ml-auto flex items-center gap-1 lg:ml-0">
        <AppSearch
          variant="button"
          shortcut
        />
        <button
          type="button"
          class="flex size-9 items-center justify-center rounded-full text-muted transition hover:bg-white/5 hover:text-highlighted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:hidden"
          :aria-expanded="menuOpen"
          aria-controls="dsa-mobile-menu"
          aria-label="Menu"
          @click="menuOpen = !menuOpen"
        >
          <UIcon
            :name="menuOpen ? 'i-lucide-x' : 'i-lucide-menu'"
            class="size-5"
          />
        </button>
      </div>
    </FxLiquidGlass>

    <Transition name="dsa-sheet">
      <FxLiquidGlass
        v-if="menuOpen"
        id="dsa-mobile-menu"
        :radius="28"
        :bezel="20"
        :depth="20"
        class="absolute inset-x-3 top-[4.75rem] p-2 lg:hidden"
      >
        <nav
          class="flex flex-col"
          aria-label="Main"
        >
          <NuxtLink
            v-for="item in items"
            :key="item.to"
            :to="item.to"
            class="flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-medium transition hover:bg-white/5"
            :class="item.active ? 'bg-white/[0.06] text-highlighted' : 'text-muted'"
          >
            <UIcon
              :name="item.icon"
              class="size-4"
              :class="item.active ? 'text-primary' : ''"
            />
            {{ item.label }}
            <span
              v-if="item.badge"
              class="ml-auto rounded-full bg-primary/15 px-1.5 py-px text-[10px] font-semibold text-primary"
            >{{ item.badge }}</span>
          </NuxtLink>
        </nav>
      </FxLiquidGlass>
    </Transition>
  </header>
</template>
