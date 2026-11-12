<script setup lang="ts">
// Variant H — the site's navigation: a liquid-glass tab bar floating at the
// bottom centre of every page, with a round glass search button beside it.
// A glass droplet sits under the active tab and glides to whichever tab the
// pointer (or focus, or a finger) is on: a spring moves it, and it stretches
// along its path while it travels, settling back into a round pill. Same
// destinations as `AppHeader`; ⌘K belongs to the search button here.
const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

interface Tab {
  label: string
  name: string
  icon: string
  to: string
  active: boolean
  beta?: boolean
}

const tabs = computed<Tab[]>(() => [
  { label: 'Champions', name: 'Champions', icon: 'i-lucide-swords', to: '/champions', active: isActive('/champions') && !isActive('/champions/tierlist') },
  { label: 'Tier list', name: 'Tier list', icon: 'i-lucide-trending-up', to: '/champions/tierlist', active: isActive('/champions/tierlist') },
  { label: 'Matchup', name: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', active: isActive('/matchup') },
  { label: 'Truemains', name: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', active: route.path === '/truemains' },
  { label: 'Favorites', name: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites', active: route.path === '/truemains/favorites' },
  { label: 'Desktop', name: 'Desktop app (beta)', icon: 'i-lucide-monitor-down', to: '/download', active: route.path === '/download', beta: true },
])

const activeIndex = computed(() => tabs.value.findIndex(tab => tab.active))
const hovered = ref<number | null>(null)
// A tap moves the droplet at once, before the destination has loaded.
const pressed = ref<number | null>(null)
const targetIndex = computed(() =>
  pressed.value ?? hovered.value ?? (activeIndex.value >= 0 ? activeIndex.value : null))

let pressTimer: ReturnType<typeof setTimeout> | undefined
function onPress(index: number, event: PointerEvent) {
  if (event.pointerType === 'mouse') return
  pressed.value = index
  clearTimeout(pressTimer)
  pressTimer = setTimeout(() => (pressed.value = null), 2500)
}
watch(() => route.path, () => {
  pressed.value = null
  clearTimeout(pressTimer)
})

// ─── The droplet: a spring on x, a stretch from its speed ──────────────────
const listEl = ref<HTMLElement | null>(null)
const dropEl = ref<HTMLElement | null>(null)
const dropBody = ref<HTMLElement | null>(null)
const shown = ref(false)
const fx = useFx()

const STIFFNESS = 520
const DAMPING = 2 * 0.62 * Math.sqrt(STIFFNESS)
let x = 0
let vx = 0
let tx = 0
let placed = false
let frame = 0
let last = 0

function tabOffset(index: number): number | null {
  const link = listEl.value?.querySelectorAll<HTMLElement>('[data-tab]')[index]
  return link ? link.offsetLeft : null
}

function paint() {
  const drop = dropEl.value
  const body = dropBody.value
  if (!drop || !body) return
  drop.style.transform = `translate3d(${x.toFixed(2)}px, 0, 0)`
  const stretch = Math.min(0.26, Math.abs(vx) / 2800)
  body.style.transform = stretch > 0.002 ? `scale(${(1 + stretch).toFixed(3)}, ${(1 - stretch * 0.42).toFixed(3)})` : ''
}

function step(now: number) {
  frame = 0
  const dt = Math.min(0.034, (now - last) / 1000 || 0.016)
  last = now
  // Semi-implicit Euler in small substeps: stable at any frame rate.
  const n = Math.ceil(dt / 0.004)
  const h = dt / n
  for (let i = 0; i < n; i++) {
    vx += (STIFFNESS * (tx - x) - DAMPING * vx) * h
    x += vx * h
  }
  if (Math.abs(tx - x) < 0.15 && Math.abs(vx) < 4) {
    x = tx
    vx = 0
    paint()
    return
  }
  paint()
  frame = requestAnimationFrame(step)
}

function moveTo(index: number | null, instant = false) {
  if (index === null) {
    shown.value = false
    return
  }
  const offset = tabOffset(index)
  if (offset === null) return
  tx = offset
  // Appearing out of nothing: no slide in from the last place it was.
  if (!placed || !shown.value || instant || fx.value.reducedMotion) {
    cancelAnimationFrame(frame)
    frame = 0
    x = tx
    vx = 0
    placed = true
    paint()
  }
  else if (!frame) {
    last = performance.now()
    frame = requestAnimationFrame(step)
  }
  shown.value = true
}

watch(targetIndex, index => moveTo(index))

let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  moveTo(targetIndex.value, true)
  if (!listEl.value) return
  // Breakpoints change the tab widths: re-place without a glide.
  resizeObserver = new ResizeObserver(() => moveTo(targetIndex.value, true))
  resizeObserver.observe(listEl.value)
})

onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  clearTimeout(pressTimer)
  resizeObserver?.disconnect()
})
</script>

<template>
  <div class="h-dock">
    <FxLiquidGlass
      as="nav"
      aria-label="Main"
      :radius="999"
      :bezel="18"
      :depth="20"
      :blur="1.6"
      :saturation="1.7"
      class="h-tabs"
    >
      <div
        ref="listEl"
        class="h-tabs__list"
        @pointerleave="hovered = null"
      >
        <span
          ref="dropEl"
          aria-hidden="true"
          class="h-drop"
          :class="{ 'is-shown': shown, 'is-resting': hovered === null || hovered === activeIndex }"
        >
          <span
            ref="dropBody"
            class="h-drop__body"
          />
        </span>
        <NuxtLink
          v-for="(tab, index) in tabs"
          :key="tab.to"
          :to="tab.to"
          data-tab
          class="h-tab"
          :class="{ 'is-active': tab.active }"
          :aria-label="tab.name"
          :aria-current="tab.active ? 'page' : undefined"
          @pointerenter="(event: PointerEvent) => { if (event.pointerType === 'mouse') hovered = index }"
          @pointerdown="(event: PointerEvent) => onPress(index, event)"
          @focus="hovered = index"
          @blur="hovered = null"
        >
          <span class="h-tab__icon">
            <UIcon
              :name="tab.icon"
              class="size-[1.3rem]"
            />
            <span
              v-if="tab.beta"
              class="h-tab__beta"
              aria-hidden="true"
            >Beta</span>
          </span>
          <span
            class="h-tab__label"
            aria-hidden="true"
          >{{ tab.label }}</span>
        </NuxtLink>
      </div>
    </FxLiquidGlass>

    <FxLiquidGlass
      :radius="999"
      :bezel="16"
      :depth="18"
      :blur="1.6"
      class="h-searchbtn"
    >
      <AppSearch
        variant="button"
        shortcut
      />
    </FxLiquidGlass>
  </div>
</template>
