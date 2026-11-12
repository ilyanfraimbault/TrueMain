<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { LAUNCHER_DESKTOP, LAUNCHER_PRIMARY, activeNavKey } from './nav-items'

// The launcher's icon rail, ≥768px: logo on top, the destinations, then search
// and the desktop app at the foot. A light slides between the destinations to
// mark the active one (its offset is the item's index × the fixed row pitch, so
// it is right from the server-rendered first paint and never measured).
//
// Wide hover screens (≥1024px): resting on the rail for a moment, or tabbing
// into it, opens it over the page with the labels — it overlays the content,
// nothing reflows. Narrower ones keep it closed and name each icon in a tooltip
// that glides between the items.
const route = useRoute()
const activeKey = computed(() => activeNavKey(route.path))
const activeIndex = computed(() => LAUNCHER_PRIMARY.findIndex(item => item.key === activeKey.value))

const canExpand = useMediaQuery('(min-width: 1024px) and (hover: hover) and (pointer: fine)')
const expanded = ref(false)
const railEl = ref<HTMLElement | null>(null)

let enterTimer: ReturnType<typeof setTimeout> | undefined
let leaveTimer: ReturnType<typeof setTimeout> | undefined
// After a click the rail closes and stays closed until the cursor leaves it:
// the search palette or the next page is what the reader is looking at now.
let suppressed = false

function clearTimers() {
  clearTimeout(enterTimer)
  clearTimeout(leaveTimer)
}

function collapse() {
  clearTimers()
  expanded.value = false
}

function onPointerEnter(event: PointerEvent) {
  if (event.pointerType === 'touch' || !canExpand.value || suppressed) return
  clearTimeout(leaveTimer)
  enterTimer = setTimeout(() => (expanded.value = true), 200)
}

function onPointerLeave() {
  suppressed = false
  clearTimeout(enterTimer)
  leaveTimer = setTimeout(() => (expanded.value = false), 140)
  hideTip()
}

function onFocusIn(event: FocusEvent) {
  const target = event.target as HTMLElement
  if (!target.matches(':focus-visible')) return
  // Focus handed back by the search palette as it closes: the reader is done
  // with the rail, it must not open (nothing would close it but a click).
  const from = event.relatedTarget as HTMLElement | null
  if (from?.closest?.('[role="dialog"]')) return
  if (canExpand.value) {
    clearTimers()
    expanded.value = true
  }
  else {
    showTipFor(target.closest<HTMLElement>('[data-tip]'))
  }
}

function onFocusOut(event: FocusEvent) {
  const next = event.relatedTarget as Node | null
  if (next && railEl.value?.contains(next)) return
  collapse()
  hideTip()
}

function onClick() {
  suppressed = true
  collapse()
  hideTip()
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && expanded.value) {
    collapse()
  }
}

watch(() => route.path, () => {
  collapse()
  hideTip()
})
watch(canExpand, (value) => {
  if (value) hideTip()
  else collapse()
})

// ─── Tooltip (closed rail, narrower screens) ──────────────────────────────
const tip = reactive({ text: '', y: 0, visible: false, placed: false })

function showTipFor(el: HTMLElement | null) {
  if (!el || expanded.value || canExpand.value) return
  const rect = el.getBoundingClientRect()
  tip.text = el.dataset.tip ?? ''
  // The rail is fixed at the top of the viewport: client y is rail y.
  tip.y = rect.top + rect.height / 2
  tip.visible = true
  // The first tip appears in place; later ones glide from the last.
  requestAnimationFrame(() => (tip.placed = true))
}

function hideTip() {
  tip.visible = false
  tip.placed = false
}

function onItemEnter(event: PointerEvent) {
  if (event.pointerType === 'touch') return
  showTipFor(event.currentTarget as HTMLElement)
}

onBeforeUnmount(clearTimers)
</script>

<template>
  <nav
    ref="railEl"
    class="e-rail"
    :class="{ 'is-expanded': expanded }"
    aria-label="Primary"
    @pointerenter="onPointerEnter"
    @pointerleave="onPointerLeave"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
    @keydown="onKeydown"
  >
    <div class="e-rail__panel">
      <NuxtLink
        to="/"
        class="e-rail__logo"
        aria-label="TrueMain home"
        @click="onClick"
      >
        <svg
          viewBox="0 0 64 64"
          class="e-rail__mark"
          aria-hidden="true"
        >
          <defs>
            <linearGradient
              id="e-mark-grad"
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
              <stop
                offset="0"
                stop-color="#f2cfa6"
              />
              <stop
                offset="0.55"
                stop-color="#e2917e"
              />
              <stop
                offset="1"
                stop-color="#8a3b44"
              />
            </linearGradient>
          </defs>
          <path
            d="M13 47V21l15 17L51 15"
            fill="none"
            stroke="url(#e-mark-grad)"
            stroke-width="8"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
        <span
          class="e-rail__word"
          aria-hidden="true"
        ><span class="e-rail__word-true">True</span>Main</span>
      </NuxtLink>

      <div class="e-rail__nav">
        <span
          class="e-rail__light"
          :class="{ 'is-off': activeIndex < 0 }"
          :style="{ '--e-i': Math.max(activeIndex, 0) }"
          aria-hidden="true"
        />
        <ul class="e-rail__list">
          <li
          v-for="item in LAUNCHER_PRIMARY"
          :key="item.key"
        >
            <NuxtLink
              :to="item.to"
              class="e-rail__item"
              :class="{ 'is-active': item.key === activeKey }"
              :aria-current="item.key === activeKey ? 'page' : undefined"
              :data-tip="item.label"
              @pointerenter="onItemEnter"
              @click="onClick"
            >
              <UIcon
                :name="item.icon"
                class="e-rail__icon"
              />
              <span class="e-rail__label">{{ item.label }}</span>
            </NuxtLink>
          </li>
        </ul>
      </div>

      <div class="e-rail__foot">
        <div
          class="e-rail__item e-rail__item--search"
          data-tip="Search"
          @pointerenter="onItemEnter"
        >
          <AppSearch
            variant="button"
            shortcut
            class="e-rail__search"
          />
          <span
            class="e-rail__label"
            aria-hidden="true"
          >Search</span>
          <ClientOnly>
            <span
              class="e-rail__kbd"
              aria-hidden="true"
            >
              <UKbd
                value="meta"
                size="sm"
              />
              <UKbd
                value="K"
                size="sm"
              />
            </span>
          </ClientOnly>
        </div>

        <NuxtLink
          :to="LAUNCHER_DESKTOP.to"
          class="e-rail__item e-rail__item--app"
          :class="{ 'is-active': activeKey === LAUNCHER_DESKTOP.key }"
          :aria-current="activeKey === LAUNCHER_DESKTOP.key ? 'page' : undefined"
          :data-tip="`${LAUNCHER_DESKTOP.label} · ${LAUNCHER_DESKTOP.badge}`"
          @pointerenter="onItemEnter"
          @click="onClick"
        >
          <UIcon
            :name="LAUNCHER_DESKTOP.icon"
            class="e-rail__icon"
          />
          <span class="e-rail__label">{{ LAUNCHER_DESKTOP.label }}</span>
          <span class="e-rail__badge">{{ LAUNCHER_DESKTOP.badge }}</span>
          <span
            class="e-rail__dot"
            aria-hidden="true"
          />
        </NuxtLink>
      </div>
    </div>

    <span
      class="e-rail__tip"
      :class="{ 'is-visible': tip.visible, 'is-placed': tip.placed }"
      :style="{ '--e-tip-y': `${tip.y}px` }"
      aria-hidden="true"
    >{{ tip.text }}</span>
  </nav>

  <div
    class="e-rail-scrim"
    :class="{ 'is-visible': expanded }"
    aria-hidden="true"
  />
</template>
