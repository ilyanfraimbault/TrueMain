<script setup lang="ts">
import type { KitNode } from '#shared/types/kit'
import type { StaticItemData } from '#shared/types/static-data'
import type { KitTreeSlot, PlacedKitNode } from '~/utils/kit-tree-layout'
import { layoutKitTree, siblingAt } from '~/utils/kit-tree-layout'
import { maxDepth } from '~/utils/kit-tree'

/**
 * A kit's item tree, drawn as the build tree is (`BuildTreeBlock`): the
 * default path — the first item and each first child — as the solid line, the
 * other branches dashed. Editable, every node is a button that asks for
 * another item, with a × to drop it and what follows, and every row ends on a
 * "+" slot while the path has room: under a leaf the item that follows it,
 * beside items already there another branch, on the first row another first
 * item. A node with siblings is dragged onto one of them (or moved with
 * Alt+←/→) to trade places, each with what follows it: the leftmost is the
 * default, the solid line followed in game.
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
  /** Trades the node at `at` with its sibling at `index`. */
  swap: [at: number[], index: number]
}>()

const H_GAP = 12
const V_GAP = 26

const layout = computed(() => layoutKitTree(props.tree, {
  size: props.size,
  hGap: H_GAP,
  vGap: V_GAP,
  slotDepth: props.editable ? maxDepth(props.boots) : 0,
}))
const width = computed(() => layout.value.width)
const height = computed(() => Math.max(layout.value.height, props.size))

const name = (itemId: number) => props.items[itemId]?.name ?? `Item ${itemId}`
function slotLabel(slot: KitTreeSlot): string {
  if (slot.after === null) return props.tree.length ? 'Add another first item' : 'Add the first item'
  return slot.branch ? `Add another branch after ${name(slot.after)}` : `Add an item after ${name(slot.after)}`
}
/** The nodes that have siblings: the ones that can trade places. */
const movable = computed(() => {
  const counts = new Map<string, number>()
  const parentOf = (node: PlacedKitNode) => node.at.slice(0, -1).join('.')
  for (const node of layout.value.nodes) counts.set(parentOf(node), (counts.get(parentOf(node)) ?? 0) + 1)
  return new Set(layout.value.nodes.filter(node => (counts.get(parentOf(node)) ?? 0) > 1).map(node => node.at.join('.')))
})
const canMove = (node: PlacedKitNode) => movable.value.has(node.at.join('.'))

// The drag, by pointer events: the webview's native drag and drop is the shell's, for files.
interface Drag { at: number[], origin: { x: number, y: number }, start: { x: number, y: number }, offset: { x: number, y: number }, moved: boolean, target: number | null }
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
  if (event.button !== 0 || !canMove(node)) return
  // Held by the node, so the drag follows the pointer off it. A pointer the
  // platform no longer tracks refuses the capture; the drag then ends where it is.
  try {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  }
  catch {
    // Untracked pointer: no capture, the drag still runs.
  }
  drag.value = { at: node.at, origin: { x: node.x, y: node.y }, start: local(event), offset: { x: 0, y: 0 }, moved: false, target: null }
}

function follow(event: PointerEvent) {
  const current = drag.value
  if (!current) return
  const point = local(event)
  const offset = { x: point.x - current.start.x, y: point.y - current.start.y }
  const moved = current.moved || Math.hypot(offset.x, offset.y) > 4
  // Aimed by the icon's centre, wherever on it the pointer took hold.
  const target = moved ? siblingAt(layout.value, current.at, current.origin.x + offset.x, current.origin.y + offset.y, props.size) : null
  drag.value = { ...current, offset, moved, target }
}

function release() {
  const current = drag.value
  drag.value = null
  if (!current?.moved) return
  dragged = true
  if (current.target !== null) emit('swap', current.at, current.target)
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
  if (index === undefined || !canMove(node)) return
  const sibling = layout.value.nodes.find(other => other.at.join('.') === [...node.at.slice(0, -1), index + step].join('.'))
  if (sibling) emit('swap', node.at, index + step)
}

const dragging = (node: PlacedKitNode) => drag.value?.moved && drag.value.at.join('.') === node.at.join('.')
const dropping = (node: PlacedKitNode) => {
  const current = drag.value
  return current?.target != null && node.at.join('.') === [...current.at.slice(0, -1), current.target].join('.')
}

const edgePath = ({ parent, child }: { parent: { x: number, y: number }, child: { x: number, y: number } }) =>
  `M ${parent.x} ${parent.y + props.size / 2} V ${parent.y + props.size / 2 + V_GAP / 2} H ${child.x} V ${child.y - props.size / 2}`
</script>

<template>
  <div class="overflow-x-auto" :class="editable && 'p-2'">
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
        class="group absolute rounded"
        :class="dragging(node) ? 'z-10 scale-110 opacity-90 shadow-lg' : 'transition-[left,top,transform,scale] duration-200 ease-out'"
        :style="{
          left: `${node.x - size / 2}px`,
          top: `${node.y - size / 2}px`,
          width: `${size}px`,
          height: `${size}px`,
          transform: dragging(node) ? `translate(${drag!.offset.x}px, ${drag!.offset.y}px)` : undefined,
        }"
      >
        <button
          v-if="editable"
          type="button"
          class="block size-full rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary"
          :class="[canMove(node) && 'cursor-grab touch-none active:cursor-grabbing', dropping(node) && 'outline-2 outline-dashed outline-offset-2 outline-primary']"
          :aria-label="`Replace ${name(node.itemId)}`"
          :aria-keyshortcuts="canMove(node) ? 'Alt+ArrowLeft Alt+ArrowRight' : undefined"
          @pointerdown="grab(node, $event)"
          @pointermove="follow"
          @pointerup="release"
          @pointercancel="drag = null"
          @dragstart.prevent
          @keydown.alt.left.prevent="nudge(node, -1)"
          @keydown.alt.right.prevent="nudge(node, 1)"
          @click="choose(node)"
        >
          <GameTooltipItemIcon :item="items[node.itemId] ?? null" :width="size" :height="size" class="size-full rounded" />
        </button>
        <GameTooltipItemIcon v-else :item="items[node.itemId] ?? null" :width="size" :height="size" class="size-full rounded" />

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
        :style="{ left: `${slot.x - size / 2}px`, top: `${slot.y - size / 2}px`, width: `${size}px`, height: `${size}px` }"
        :aria-label="slotLabel(slot)"
        :title="slotLabel(slot)"
        @click="emit('pick', { at: slot.parentAt, mode: 'child' })"
      >
        <UIcon name="i-lucide-plus" class="size-4" />
      </button>
    </div>
  </div>
</template>
