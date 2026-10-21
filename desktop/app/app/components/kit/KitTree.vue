<script setup lang="ts">
import type { KitNode } from '#shared/types/kit'
import type { StaticItemData } from '#shared/types/static-data'
import { layoutKitTree } from '~/utils/kit-tree-layout'
import { maxDepth } from '~/utils/kit-tree'

/**
 * A kit's item tree, drawn as the build tree is (`BuildTreeBlock`): the
 * default path — the first item and each first child — as the solid line, the
 * other branches dashed. Editable, every node is a button that asks for
 * another item, with a "+" under it for a branch while the path has room and
 * a × to drop it and what follows; a last slot on the first row adds another
 * first item.
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

const layout = computed(() => layoutKitTree(props.tree, { size: props.size, hGap: H_GAP, vGap: V_GAP }))
const depthCap = computed(() => maxDepth(props.boots))
/** The tree's width, plus the "new first item" slot when editable — after a gap when it follows a root. */
const width = computed(() => {
  if (!props.editable) return layout.value.width
  const gap = props.tree.length > 0 ? H_GAP : 0
  return layout.value.width + gap + props.size
})
const height = computed(() => Math.max(layout.value.height, props.size))

const name = (itemId: number) => props.items[itemId]?.name ?? `Item ${itemId}`
const edgePath = ({ parent, child }: { parent: { x: number, y: number }, child: { x: number, y: number } }) =>
  `M ${parent.x} ${parent.y + props.size / 2} V ${parent.y + props.size / 2 + V_GAP / 2} H ${child.x} V ${child.y - props.size / 2}`
</script>

<template>
  <div class="overflow-x-auto pb-1">
    <div class="relative mx-auto" :style="{ width: `${width}px`, height: `${height + (editable ? 14 : 0)}px` }">
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
          class="block size-full rounded ring-1 ring-transparent transition hover:ring-primary/60 focus-visible:ring-primary focus-visible:outline-none"
          :aria-label="`Replace ${name(node.itemId)}`"
          @click="emit('pick', { at: node.at, mode: 'replace' })"
        >
          <GameTooltipItemIcon :item="items[node.itemId] ?? null" :width="size" :height="size" class="size-full rounded" />
        </button>
        <GameTooltipItemIcon v-else :item="items[node.itemId] ?? null" :width="size" :height="size" class="size-full rounded" />

        <template v-if="editable">
          <button
            type="button"
            class="absolute -right-1.5 -top-1.5 hidden size-4 items-center justify-center rounded-full bg-elevated text-dimmed ring-1 ring-default hover:text-error group-hover:flex group-focus-within:flex"
            :aria-label="`Remove ${name(node.itemId)} and what follows`"
            @click="emit('remove', node.at)"
          >
            <UIcon name="i-lucide-x" class="size-3" />
          </button>
          <button
            v-if="node.at.length < depthCap"
            type="button"
            class="absolute left-1/2 top-full mt-1 flex size-4 -translate-x-1/2 items-center justify-center rounded-full bg-elevated text-dimmed opacity-60 ring-1 ring-default transition hover:text-primary hover:opacity-100 focus-visible:opacity-100 group-hover:opacity-100"
            :aria-label="`Add an item after ${name(node.itemId)}`"
            @click="emit('pick', { at: node.at, mode: 'child' })"
          >
            <UIcon name="i-lucide-plus" class="size-3" />
          </button>
        </template>
      </div>

      <button
        v-if="editable"
        type="button"
        class="absolute top-0 flex items-center justify-center rounded border border-dashed border-default text-dimmed transition hover:border-primary/60 hover:text-primary"
        :style="{ left: `${width - size}px`, width: `${size}px`, height: `${size}px` }"
        :aria-label="tree.length ? 'Add another first item' : 'Add the first item'"
        @click="emit('pick', { at: [], mode: 'child' })"
      >
        <UIcon name="i-lucide-plus" class="size-4" />
      </button>
    </div>
  </div>
</template>
