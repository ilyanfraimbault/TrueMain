<script setup lang="ts">
import type { KitNode } from '#shared/types/kit'
import type { StaticItemData } from '#shared/types/static-data'
import type { KitTreeSlot, PlacedKitNode } from '~/utils/kit-tree-layout'
import { dropTargetAt, layoutKitTree } from '~/utils/kit-tree-layout'
import { maxDepth, swapNodes } from '~/utils/kit-tree'
import { onBeforeUnmount, onMounted } from 'vue'

/**
 * A kit's item tree, drawn as the build tree is (`BuildTreeBlock`): the
 * default path — the first item and each first child — as the solid line, the
 * other branches dashed. Editable, every node is a button that asks for
 * another item, with a × to drop it and what follows, and every row ends on a
 * "+" slot while the path has room: under a leaf the item that follows it,
 * beside items already there another branch, on the first row another first
 * item. A node is dragged onto any other it can trade places with (or moved
 * along its siblings with Alt+←/→), each with what follows it: the leftmost
 * sibling is the default, the solid line followed in game.
 *
 * The tree fits its width: past it, every icon and gap shrinks together, down
 * to `MIN_SIZE`, rather than the tree scrolling.
 */
const props = withDefaults(defineProps<{
  tree: KitNode[]
  items: Record<number, StaticItemData>
  boots: number[]
  editable?: boolean
  size?: number
}>(), { editable: false, size: 32 })

const emit = defineEmits<{
  /** Asks for an item: to replace the node at `at`, or as a new child of it (a new first item for `[]`). */
  pick: [{ at: number[], mode: 'replace' | 'child' }]
  remove: [at: number[]]
  /** Trades the nodes at `a` and `b`, each with what follows it (a trade `swapNodes` allows). */
  swap: [a: number[], b: number[]]
}>()

const MIN_SIZE = 16
/** The gaps, in proportion to the icons so a shrunk tree keeps its shape: 12 and 26 px at 32. */
const gaps = (size: number) => ({ hGap: size * 0.375, vGap: size * 0.8125 })

const frame = ref<HTMLElement | null>(null)
const room = ref(Number.POSITIVE_INFINITY)
let observer: ResizeObserver | undefined
onMounted(() => {
  if (!frame.value || typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(([entry]) => {
    if (entry) room.value = entry.contentRect.width
  })
  observer.observe(frame.value)
})
onBeforeUnmount(() => observer?.disconnect())

const slotDepth = computed(() => (props.editable ? maxDepth(props.boots) : 0))
/** The icon size the tree fits its width at: its width grows with it in proportion. */
const cell = computed(() => {
  const natural = layoutKitTree(props.tree, { size: props.size, ...gaps(props.size), slotDepth: slotDepth.value }).width
  if (!natural || room.value >= natural) return props.size
  return Math.max(MIN_SIZE, Math.floor(props.size * room.value / natural))
})
const keys = new WeakMap<KitNode, string>()
const layout = computed(() => layoutKitTree(props.tree, { size: cell.value, ...gaps(cell.value), slotDepth: slotDepth.value, keys }))
const width = computed(() => layout.value.width)
const height = computed(() => Math.max(layout.value.height, cell.value))

const name = (itemId: number) => props.items[itemId]?.name ?? `Item ${itemId}`
function slotLabel(slot: KitTreeSlot): string {
  if (slot.after === null) return props.tree.length ? 'Add another first item' : 'Add the first item'
  return slot.branch ? `Add another branch after ${name(slot.after)}` : `Add an item after ${name(slot.after)}`
}
const allows = (a: number[], b: number[]) => swapNodes(props.tree, a, b, maxDepth(props.boots)) !== null
const canMove = computed(() => layout.value.nodes.length > 1)

// The drag, by pointer events: the webview's native drag and drop is the shell's, for files.
interface Drag {
  at: number[]
  /** Where the trade is allowed to, set on the grab: the tree does not change during a drag. */
  allowed: Set<string>
  origin: { x: number, y: number }
  start: { x: number, y: number }
  offset: { x: number, y: number }
  moved: boolean
  target: number[] | null
}
const canvas = ref<HTMLElement | null>(null)
const drag = ref<Drag | null>(null)
/** Set when a drag ends, so the click it ends with does not ask for an item. */
let dragged = false

function local(event: PointerEvent) {
  const box = canvas.value?.getBoundingClientRect()
  return { x: event.clientX - (box?.left ?? 0), y: event.clientY - (box?.top ?? 0) }
}

function grab(node: PlacedKitNode, event: PointerEvent) {
  dragged = false
  if (event.button !== 0 || !canMove.value) return
  // Held by the node, so the drag follows the pointer off it. A pointer the
  // platform no longer tracks refuses the capture; the drag then ends where it is.
  try {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  }
  catch {
    // Untracked pointer: no capture, the drag still runs.
  }
  const allowed = new Set(layout.value.nodes.filter(other => allows(node.at, other.at)).map(other => other.at.join('.')))
  drag.value = { at: node.at, allowed, origin: { x: node.x, y: node.y }, start: local(event), offset: { x: 0, y: 0 }, moved: false, target: null }
}

function follow(event: PointerEvent) {
  const current = drag.value
  if (!current) return
  const point = local(event)
  const offset = { x: point.x - current.start.x, y: point.y - current.start.y }
  const moved = current.moved || Math.hypot(offset.x, offset.y) > 4
  // Aimed by the icon's centre, wherever on it the pointer took hold.
  const centre = { x: current.origin.x + offset.x, y: current.origin.y + offset.y }
  const target = moved ? dropTargetAt(layout.value, current.at, centre.x, centre.y, cell.value, to => current.allowed.has(to.join('.'))) : null
  drag.value = { ...current, offset, moved, target }
}

function release() {
  const current = drag.value
  drag.value = null
  if (!current?.moved) return
  dragged = true
  if (current.target) emit('swap', current.at, current.target)
}

function choose(node: PlacedKitNode) {
  if (dragged) {
    dragged = false
    return
  }
  emit('pick', { at: node.at, mode: 'replace' })
}

/** Alt+←/→: the same trade, with the sibling beside it. */
function nudge(node: PlacedKitNode, step: -1 | 1) {
  const index = node.at.at(-1)
  if (index === undefined) return
  const sibling = [...node.at.slice(0, -1), index + step]
  if (layout.value.nodes.some(other => other.at.join('.') === sibling.join('.')) && allows(node.at, sibling)) emit('swap', node.at, sibling)
}

const dragging = (node: PlacedKitNode) => drag.value?.moved && drag.value.at.join('.') === node.at.join('.')
const dropping = (node: PlacedKitNode) => drag.value?.target?.join('.') === node.at.join('.')

const edgePath = ({ parent, child }: { parent: { x: number, y: number }, child: { x: number, y: number } }) => {
  const half = cell.value / 2
  return `M ${parent.x} ${parent.y + half} V ${parent.y + half + gaps(cell.value).vGap / 2} H ${child.x} V ${child.y - half}`
}
</script>

<template>
  <!-- Scrolls only a tree still too wide at the smallest icons. -->
  <div ref="frame" :class="[editable && 'p-2', width > room && 'overflow-x-auto']">
    <!-- Editable, a press on the tree is a drag of ours: not a text selection,
         and not the webview's own drag of the icon's image, which takes the
         pointer away mid-drag (a `pointercancel`) and drops the node where it was. -->
    <div
      ref="canvas"
      class="relative mx-auto"
      :class="editable && 'select-none'"
      :style="{ width: `${width}px`, height: `${height}px` }"
    >
      <svg class="absolute inset-0 overflow-visible" aria-hidden="true" :width="width" :height="height">
        <path
          v-for="(edge, index) in layout.edges"
          :key="`edge-${index}`"
          :d="edgePath(edge)"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          :class="edge.child.main ? 'text-default' : 'text-muted/60'"
          :stroke-dasharray="edge.child.main ? undefined : '4 4'"
        />
      </svg>

      <div
        v-for="node in layout.nodes"
        :key="node.key"
        class="group absolute flex rounded"
        :class="dragging(node) ? 'z-10 scale-110 opacity-90 shadow-lg' : 'transition-[left,top,transform,scale] duration-200 ease-out'"
        :style="{
          left: `${node.x - cell / 2}px`,
          top: `${node.y - cell / 2}px`,
          width: `${cell}px`,
          height: `${cell}px`,
          transform: dragging(node) ? `translate(${drag!.offset.x}px, ${drag!.offset.y}px)` : undefined,
        }"
      >
        <button
          v-if="editable"
          type="button"
          class="flex size-full rounded transition-[filter] hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary"
          :class="[canMove && 'cursor-grab touch-none active:cursor-grabbing', dropping(node) && 'outline-2 outline-dashed outline-offset-2 outline-primary']"
          :aria-label="`Replace ${name(node.itemId)}`"
          :aria-keyshortcuts="canMove ? 'Alt+ArrowLeft Alt+ArrowRight' : undefined"
          @pointerdown="grab(node, $event)"
          @pointermove="follow"
          @pointerup="release"
          @pointercancel="drag = null"
          @dragstart.prevent
          @keydown.alt.left.prevent="nudge(node, -1)"
          @keydown.alt.right.prevent="nudge(node, 1)"
          @click="choose(node)"
        >
          <GameTooltipItemIcon :item="items[node.itemId] ?? null" :width="cell" :height="cell" class="size-full rounded" />
        </button>
        <GameTooltipItemIcon v-else :item="items[node.itemId] ?? null" :width="cell" :height="cell" class="size-full rounded" />

        <button
          v-if="editable"
          type="button"
          class="absolute -right-1.5 -top-1.5 hidden size-4 items-center justify-center rounded-full bg-elevated text-dimmed ring-1 ring-default hover:text-error group-hover:flex group-focus-within:flex"
          :aria-label="`Remove ${name(node.itemId)} and what follows`"
          @click="emit('remove', node.at)"
        >
          <UIcon name="i-lucide-x" class="size-3" />
        </button>
      </div>

      <button
        v-for="slot in layout.slots"
        :key="`slot-${slot.parentKey}`"
        type="button"
        class="absolute flex items-center justify-center rounded border border-dashed border-default text-dimmed transition-[left,top,color,border-color] duration-200 ease-out hover:border-primary/60 hover:text-primary focus-visible:outline-2 focus-visible:outline-primary"
        :style="{ left: `${slot.x - cell / 2}px`, top: `${slot.y - cell / 2}px`, width: `${cell}px`, height: `${cell}px` }"
        :aria-label="slotLabel(slot)"
        :title="slotLabel(slot)"
        @click="emit('pick', { at: slot.parentAt, mode: 'child' })"
      >
        <UIcon name="i-lucide-plus" class="size-4" />
      </button>
    </div>
  </div>
</template>
