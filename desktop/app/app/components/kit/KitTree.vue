<script setup lang="ts">
import type { KitNode } from '#shared/types/kit'
import type { StaticItemData } from '#shared/types/static-data'
import type { KitTreeSlot } from '~/utils/kit-tree-layout'
import { layoutKitTree } from '~/utils/kit-tree-layout'
import { maxDepth } from '~/utils/kit-tree'

/**
 * A kit's item tree, drawn as the build tree is (`BuildTreeBlock`): the
 * default path — the first item and each first child — as the solid line, the
 * other branches dashed. Editable, every node is a button that asks for
 * another item, with a × to drop it and what follows, and every row ends on a
 * "+" slot while the path has room: under a leaf the item that follows it,
 * beside items already there another branch, on the first row another first
 * item.
 */
const props = withDefaults(defineProps<{
  tree: KitNode[]
  items: Record<number, StaticItemData>
  boots: number | null
  editable?: boolean
  size?: number
}>(), { editable: false, size: 32 })

const emit = defineEmits<{
  /** Asks for an item: to replace the node at `at`, or as a new child of it (a new first item for `[]`). */
  pick: [{ at: number[], mode: 'replace' | 'child' }]
  remove: [at: number[]]
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
const edgePath = ({ parent, child }: { parent: { x: number, y: number }, child: { x: number, y: number } }) =>
  `M ${parent.x} ${parent.y + props.size / 2} V ${parent.y + props.size / 2 + V_GAP / 2} H ${child.x} V ${child.y - props.size / 2}`
</script>

<template>
  <div class="overflow-x-auto" :class="editable && 'p-2'">
    <div class="relative mx-auto" :style="{ width: `${width}px`, height: `${height}px` }">
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
        :key="node.at.join('.')"
        class="group absolute"
        :style="{ left: `${node.x - size / 2}px`, top: `${node.y - size / 2}px`, width: `${size}px`, height: `${size}px` }"
      >
        <button
          v-if="editable"
          type="button"
          class="block size-full rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary"
          :aria-label="`Replace ${name(node.itemId)}`"
          @click="emit('pick', { at: node.at, mode: 'replace' })"
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
        :key="`slot-${slot.parentAt.join('.')}`"
        type="button"
        class="absolute flex items-center justify-center rounded border border-dashed border-default text-dimmed transition hover:border-primary/60 hover:text-primary focus-visible:outline-2 focus-visible:outline-primary"
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
