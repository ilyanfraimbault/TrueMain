<script setup lang="ts">
import type { OverlayPanel, OverlaySettings } from '~/types/overlay'
import type { Point, Size } from '~/utils/overlay-layout'
import { OVERLAY_PANEL_INFO, OVERLAY_PANELS, PANEL_KEY } from '~/types/overlay'
import { currentScreen, customAt, panelBox, panelSize } from '~/utils/overlay-layout'

/**
 * The overlay's layout (#1819): the screen, small, with each panel that is on
 * where it sits over the game, and beside it the panels that are off.
 *
 * A panel is dragged on the screen to move it, and its × takes it off; a
 * panel that is off is dragged from the side onto the screen — or clicked, to
 * come back where it last was. A panel dragged off the screen onto the side
 * goes off too. Arrow keys nudge a focused panel, Delete takes it off. Each
 * drop is one change for the page to save.
 */
const props = defineProps<{ settings: OverlaySettings }>()
const emit = defineEmits<{ place: [panel: OverlayPanel, custom: Point], show: [panel: OverlayPanel], hide: [panel: OverlayPanel] }>()

const screen = currentScreen()
const canvas = ref<HTMLElement | null>(null)
const tray = ref<HTMLElement | null>(null)
const canvasWidth = ref(0)
let observer: ResizeObserver | undefined
onMounted(() => {
  observer = new ResizeObserver(([entry]) => (canvasWidth.value = entry?.contentRect.width ?? 0))
  if (canvas.value) observer.observe(canvas.value)
})
onBeforeUnmount(() => observer?.disconnect())

/** Canvas pixels per screen point. */
const ratio = computed(() => canvasWidth.value / screen.width)

const shown = computed(() => OVERLAY_PANELS.filter(panel => props.settings[PANEL_KEY[panel]].enabled))
const hidden = computed(() => OVERLAY_PANELS.filter(panel => !props.settings[PANEL_KEY[panel]].enabled))

/** Each panel's size as drawn with its sample, before the overlay's scale: what it takes in game. */
const measured = reactive<Partial<Record<OverlayPanel, Size>>>({})
function measure(panel: OverlayPanel, size: Size) {
  if (size.width > 0 && size.height > 0) measured[panel] = size
}
const sizeOf = (panel: OverlayPanel) => panelSize(panel, props.settings.scale, measured[panel])

/** How much a panel is shrunk to be drawn on the canvas: the screen's ratio and the overlay's scale. */
const zoom = computed(() => ratio.value * props.settings.scale)

/** A panel's box on the canvas: its box on the screen, at the canvas's scale — nothing enlarged, so its edges are its edges. */
function drawnSize(panel: OverlayPanel) {
  const size = sizeOf(panel)
  return { width: size.width * ratio.value, height: size.height * ratio.value }
}
function boxStyle(panel: OverlayPanel) {
  const box = panelBox(props.settings[PANEL_KEY[panel]], sizeOf(panel), screen)
  const r = ratio.value
  return { left: `${box.x * r}px`, top: `${box.y * r}px`, width: `${box.width * r}px`, height: `${box.height * r}px` }
}

// The drag, by pointer events so it works the same from the screen and from the side.
interface Drag { panel: OverlayPanel, from: 'screen' | 'tray', grab: Point, start: Point, pointer: Point, moved: boolean }
const drag = ref<Drag | null>(null)

const inside = (element: HTMLElement | null, point: Point) => {
  const rect = element?.getBoundingClientRect()
  return !!rect && point.x >= rect.left && point.x <= rect.right && point.y >= rect.top && point.y <= rect.bottom
}

function begin(panel: OverlayPanel, from: Drag['from'], event: PointerEvent) {
  if (event.button !== 0) return
  event.preventDefault()
  const size = drawnSize(panel)
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  // From the screen the panel is held where it was grabbed; from the side, by its middle.
  const grab = from === 'screen'
    ? { x: event.clientX - rect.left, y: event.clientY - rect.top }
    : { x: size.width / 2, y: size.height / 2 }
  const point = { x: event.clientX, y: event.clientY }
  drag.value = { panel, from, grab, start: point, pointer: point, moved: false }
  window.addEventListener('pointermove', move)
  window.addEventListener('pointerup', end)
  window.addEventListener('pointercancel', cancel)
}

function move(event: PointerEvent) {
  const current = drag.value
  if (!current) return
  current.pointer = { x: event.clientX, y: event.clientY }
  if (Math.hypot(current.pointer.x - current.start.x, current.pointer.y - current.start.y) > 3) current.moved = true
}

function cancel() {
  window.removeEventListener('pointermove', move)
  window.removeEventListener('pointerup', end)
  window.removeEventListener('pointercancel', cancel)
  drag.value = null
}

function end() {
  const current = drag.value
  cancel()
  if (!current) return
  if (!current.moved) {
    if (current.from === 'tray') emit('show', current.panel)
    return
  }
  if (inside(canvas.value, current.pointer)) {
    // Where the drawn box's corner lands is where the panel's corner goes.
    const rect = canvas.value!.getBoundingClientRect()
    const origin = {
      x: (current.pointer.x - current.grab.x - rect.left) / ratio.value,
      y: (current.pointer.y - current.grab.y - rect.top) / ratio.value,
    }
    emit('place', current.panel, customAt(origin, sizeOf(current.panel), screen))
  }
  else if (current.from === 'screen' && inside(tray.value, current.pointer)) {
    emit('hide', current.panel)
  }
}
onBeforeUnmount(cancel)

/** The panel under the pointer: held on the screen while over it, so it shows where it lands. */
const ghost = computed(() => {
  const current = drag.value
  if (!current?.moved) return null
  const { width, height } = drawnSize(current.panel)
  let left = current.pointer.x - current.grab.x
  let top = current.pointer.y - current.grab.y
  const over = inside(canvas.value, current.pointer)
  if (over) {
    const rect = canvas.value!.getBoundingClientRect()
    left = Math.min(Math.max(left, rect.left), rect.right - width)
    top = Math.min(Math.max(top, rect.top), rect.bottom - height)
  }
  const dropping = !over && current.from === 'screen' && inside(tray.value, current.pointer)
  return { panel: current.panel, over, dropping, style: { left: `${left}px`, top: `${top}px`, width: `${width}px`, height: `${height}px` } }
})

/** Arrow keys move a focused panel by 1 % of the screen (Shift: 5 %). */
function nudge(panel: OverlayPanel, event: KeyboardEvent) {
  if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault()
    emit('hide', panel)
    return
  }
  const steps: Record<string, Point> = { ArrowLeft: { x: -1, y: 0 }, ArrowRight: { x: 1, y: 0 }, ArrowUp: { x: 0, y: -1 }, ArrowDown: { x: 0, y: 1 } }
  const step = steps[event.key]
  if (!step) return
  event.preventDefault()
  const size = sizeOf(panel)
  const box = panelBox(props.settings[PANEL_KEY[panel]], size, screen)
  const by = event.shiftKey ? 0.05 : 0.01
  emit('place', panel, customAt({ x: box.x + step.x * by * screen.width, y: box.y + step.y * by * screen.height }, size, screen))
}
</script>

<template>
  <div class="flex min-h-0 flex-1 gap-5">
    <div class="flex min-w-0 flex-1 flex-col gap-2">
      <div
        ref="canvas"
        class="relative w-full overflow-hidden rounded-lg bg-[radial-gradient(ellipse_at_40%_35%,var(--ui-bg-accented),var(--ui-bg-muted)_70%)] ring-1 ring-default"
        :class="ghost?.over && 'ring-primary/60'"
        :style="{ aspectRatio: `${screen.width} / ${screen.height}` }"
        aria-label="Your screen"
      >
        <!-- The game's own HUD, so the panels can keep clear of it. -->
        <div class="pointer-events-none absolute bottom-[1.5%] right-[1%] aspect-square w-[14%] rounded border border-dashed border-default/80" />
        <div class="pointer-events-none absolute bottom-[1%] left-1/2 h-[12%] w-[30%] -translate-x-1/2 rounded border border-dashed border-default/80" />
        <span class="pointer-events-none absolute bottom-[3%] right-[3%] text-[10px] text-dimmed">Minimap</span>
        <span class="pointer-events-none absolute bottom-[5%] left-1/2 -translate-x-1/2 text-[10px] text-dimmed">Abilities and items</span>

        <div
          v-for="panel in shown"
          :key="panel"
          class="group absolute cursor-grab touch-none rounded-sm outline-none transition-opacity focus-visible:ring-2 focus-visible:ring-primary"
          :class="drag?.panel === panel && drag.moved ? 'opacity-25' : 'hover:ring-1 hover:ring-primary/60'"
          :style="boxStyle(panel)"
          tabindex="0"
          role="button"
          :aria-label="`${OVERLAY_PANEL_INFO[panel].label}: drag to move, arrow keys to nudge, Delete to remove`"
          @pointerdown="begin(panel, 'screen', $event)"
          @keydown="nudge(panel, $event)"
        >
          <OverlayPanelMock :panel="panel" :zoom="zoom" removable @remove="emit('hide', panel)" @measure="measure(panel, $event)" />
        </div>

        <p v-if="!shown.length" class="absolute inset-0 flex items-center justify-center text-sm text-muted">
          Drag a panel here from the right.
        </p>
      </div>
      <p class="text-xs text-dimmed">
        Your screen, {{ screen.width }} × {{ screen.height }}. Each panel is drawn as it shows in game, with sample values, at the size it takes on this screen.
      </p>
    </div>

    <aside class="flex w-60 shrink-0 flex-col gap-6">
      <section
        ref="tray"
        class="flex flex-col gap-2 rounded-lg p-2 ring-1 transition-colors"
        :class="ghost?.dropping ? 'bg-error/5 ring-error/50' : 'ring-transparent'"
      >
        <h3 class="stat-label">Hidden panels</h3>
        <p v-if="!hidden.length" class="text-xs text-muted">
          {{ ghost?.dropping ? 'Drop to hide it.' : 'Every panel is on your screen. Remove one with its × or drag it here.' }}
        </p>
        <button
          v-for="panel in hidden"
          :key="panel"
          type="button"
          class="flex cursor-grab touch-none items-start gap-2.5 rounded-md bg-elevated/60 p-2.5 text-left ring-1 ring-default transition hover:ring-primary/60"
          :class="drag?.panel === panel && drag.moved && 'opacity-40'"
          :title="`Drag onto the screen, or click to show ${OVERLAY_PANEL_INFO[panel].label} where it was`"
          @pointerdown="begin(panel, 'tray', $event)"
          @keydown.enter.prevent="emit('show', panel)"
          @keydown.space.prevent="emit('show', panel)"
        >
          <OverlayPanelIcon :panel="panel" class="mt-0.5 size-4 shrink-0 text-primary" />
          <span class="flex min-w-0 flex-col gap-0.5">
            <span class="text-sm font-medium text-highlighted">{{ OVERLAY_PANEL_INFO[panel].label }}</span>
            <span class="text-xs text-muted">{{ OVERLAY_PANEL_INFO[panel].description }}</span>
          </span>
        </button>
      </section>
      <slot />
    </aside>

    <Teleport to="body">
      <div v-if="ghost" class="pointer-events-none fixed z-50 rounded-sm shadow-lg ring-1 ring-primary" :class="ghost.dropping && 'opacity-60'" :style="ghost.style">
        <OverlayPanelMock :panel="ghost.panel" :zoom="zoom" @measure="measure(ghost.panel, $event)" />
      </div>
    </Teleport>
  </div>
</template>
