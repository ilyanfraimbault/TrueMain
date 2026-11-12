<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
import { startLightEngine } from './light-engine'

// Variant D ("Cockpit"): TrueMain framed as an analytics app. A persistent
// sidebar on the left (collapsible to an icon rail), a slim context bar over the
// content column, the page under it, the footer at its foot. Below 1024px the
// sidebar becomes a drawer behind a top bar.
//
// The rail's state is remembered in `localStorage`. A one-line head script puts
// it on `<html data-d-rail>` before the first paint, and the layout is CSS read
// from that attribute: the markup is identical either way, so there is nothing
// for hydration to disagree with, and a collapsed rail never opens then snaps
// shut. `collapsed` mirrors the attribute after mount, for labels and tooltips.
const RAIL_KEY = 'tm-d-rail'

useHead({
  script: [{
    key: 'd-rail',
    innerHTML: `try{if(localStorage.getItem('${RAIL_KEY}')==='1')document.documentElement.setAttribute('data-d-rail','collapsed')}catch(e){}`,
  }],
})

const collapsed = ref(false)
const drawerOpen = ref(false)
const menuButton = ref<HTMLButtonElement | null>(null)
const route = useRoute()

let animTimer: ReturnType<typeof setTimeout> | undefined
function setRail(next: boolean) {
  const html = document.documentElement
  // Width transitions run for a toggle only — never on load or a resize.
  html.setAttribute('data-d-anim', '')
  collapsed.value = next
  if (next) html.setAttribute('data-d-rail', 'collapsed')
  else html.removeAttribute('data-d-rail')
  try {
    localStorage.setItem(RAIL_KEY, next ? '1' : '0')
  }
  catch {
    // Storage blocked: the rail holds until the next load.
  }
  clearTimeout(animTimer)
  animTimer = setTimeout(() => html.removeAttribute('data-d-anim'), 380)
}

const DESKTOP_QUERY = '(min-width: 1024px)'
const isDesktop = () => window.matchMedia(DESKTOP_QUERY).matches

defineShortcuts({
  '[': () => {
    if (isDesktop()) setRail(!collapsed.value)
  },
})

// ─── Mobile drawer ────────────────────────────────────────────────────────
// A navigation closes it, and the route's own focus handling then owns focus;
// any other close (Escape, the close button, the overlay) hands it back to the
// menu button.
let closedByNavigation = false
watch(() => route.path, () => {
  if (!drawerOpen.value) return
  closedByNavigation = true
  drawerOpen.value = false
})

const drawerContent = {
  onCloseAutoFocus(event: Event) {
    event.preventDefault()
    if (!closedByNavigation) menuButton.value?.focus()
    closedByNavigation = false
  },
}

// ─── Lit edges ────────────────────────────────────────────────────────────
// One pointer listener for the whole frame; a cursor-less device gets none.
const fx = useFx()
let stopLight: (() => void) | null = null
// A window widened past the breakpoint has a sidebar again: no drawer over it.
let desktopQuery: MediaQueryList | null = null
const onBreakpoint = (event: MediaQueryListEvent) => {
  if (event.matches) drawerOpen.value = false
}
onMounted(() => {
  collapsed.value = document.documentElement.getAttribute('data-d-rail') === 'collapsed'
  if (fx.value.finePointer) stopLight = startLightEngine()
  desktopQuery = window.matchMedia(DESKTOP_QUERY)
  desktopQuery.addEventListener('change', onBreakpoint)
})
onBeforeUnmount(() => {
  stopLight?.()
  clearTimeout(animTimer)
  desktopQuery?.removeEventListener('change', onBreakpoint)
})
</script>

<template>
  <div class="d-app">
    <header class="d-top">
      <button
        ref="menuButton"
        type="button"
        class="d-top__btn"
        aria-label="Open menu"
        aria-haspopup="dialog"
        :aria-expanded="drawerOpen"
        @click="drawerOpen = true"
      >
        <UIcon
          name="i-lucide-menu"
          class="size-5"
        />
      </button>
      <NuxtLink
        to="/"
        class="d-top__brand"
        aria-label="TrueMain overview"
      >
        <DesignDLogo class="text-[17px]" />
      </NuxtLink>
      <AppSearch
        variant="button"
        class="d-top__search"
      />
    </header>

    <aside
      id="d-sidebar"
      class="d-side"
      aria-label="Sidebar"
    >
      <DesignDSidebar :collapsed="collapsed" />
    </aside>

    <USlideover
      v-model:open="drawerOpen"
      side="left"
      title="Menu"
      :content="drawerContent"
      :ui="{ content: 'd-drawer max-w-[17.5rem]', overlay: 'd-drawer-overlay' }"
    >
      <template #content="{ close }">
        <DesignDSidebar
          drawer
          @close="close"
        />
      </template>
    </USlideover>

    <div class="d-stage">
      <DesignDContextBar
        :collapsed="collapsed"
        @toggle="setRail(!collapsed)"
      />
      <main
        :id="MAIN_CONTENT_ID"
        tabindex="-1"
        class="d-main outline-none"
      >
        <slot />
      </main>
      <DesignDFooter />
    </div>
  </div>
</template>
