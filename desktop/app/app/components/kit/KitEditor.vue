<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import type { KitItemGroup } from '~/utils/kit-items'
import { cloneKit, KIT_NAME_MAX, kitIssues, withBoots } from '~/utils/kit'
import { addChild, blockedItems, removeSubtree, replaceItem } from '~/utils/kit-tree'

/**
 * The kit editor (#2185): a draft of the kit, saved as a whole or dropped.
 * Spells, starter and boots in their own slots over the rune page, then the
 * item tree, every node editable. What keeps the kit from being saved is
 * listed under it, in the player's words (`kitIssues`).
 */
const props = defineProps<{ kit: Kit, saving?: boolean }>()
const emit = defineEmits<{ save: [kit: Kit], cancel: [] }>()

const { items, summoners, runeTree } = useStaticData()

const draft = ref<Kit>(cloneKit(props.kit))
watch(() => props.kit.id, () => (draft.value = cloneKit(props.kit)))

const issues = computed(() => kitIssues(draft.value, runeTree.value))

/** What the item picker is open for. */
type Target
  = | { kind: 'tree', at: number[], mode: 'replace' | 'child' }
    | { kind: 'starter', index: number | null }
    | { kind: 'boots' }
const target = ref<Target | null>(null)
const pickerOpen = computed({
  get: () => target.value !== null,
  set: (open) => {
    if (!open) target.value = null
  },
})

const STARTER_MAX = 6
const pickerGroups = computed<KitItemGroup[]>(() => {
  switch (target.value?.kind) {
    case 'starter': return ['starter', 'component']
    case 'boots': return ['boots']
    default: return ['legendary', 'component']
  }
})
const pickerTitle = computed(() => {
  switch (target.value?.kind) {
    case 'starter': return 'Starter item'
    case 'boots': return 'Boots'
    default: return target.value?.mode === 'replace' ? 'Replace the item' : 'Add an item'
  }
})
const blocked = computed(() => {
  const at = target.value
  if (at?.kind !== 'tree') return []
  return [...blockedItems(draft.value.tree, at.at, at.mode), ...(draft.value.boots ? [draft.value.boots] : [])]
})

function picked(itemId: number) {
  const at = target.value
  if (!at) return
  const kit = draft.value
  if (at.kind === 'boots') draft.value = withBoots(kit, itemId)
  else if (at.kind === 'starter') {
    const starter = [...kit.starter]
    if (at.index === null) starter.push(itemId)
    else starter[at.index] = itemId
    draft.value = { ...kit, starter }
  }
  else {
    draft.value = { ...kit, tree: at.mode === 'replace' ? replaceItem(kit.tree, at.at, itemId) : addChild(kit.tree, at.at, itemId) }
  }
}

const removeNode = (at: number[]) => (draft.value = { ...draft.value, tree: removeSubtree(draft.value.tree, at) })
const removeStarter = (index: number) => (draft.value = { ...draft.value, starter: draft.value.starter.filter((_, i) => i !== index) })
const name = (itemId: number) => items.value[itemId]?.name ?? `Item ${itemId}`
</script>

<template>
  <div class="space-y-5">
    <UFormField label="Name">
      <UInput v-model="draft.name" :maxlength="KIT_NAME_MAX" placeholder="Into tanks" class="w-full max-w-xs" />
    </UFormField>

    <div class="flex flex-wrap items-start gap-x-8 gap-y-4">
      <div>
        <p class="stat-label mb-2">Summoners</p>
        <KitSpells v-model="draft.spells" :summoners="summoners" />
      </div>

      <div>
        <p class="stat-label mb-2">Starter</p>
        <div class="flex gap-1.5">
          <div v-for="(itemId, index) in draft.starter" :key="`${itemId}-${index}`" class="group relative">
            <button type="button" class="block rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary" :aria-label="`Replace ${name(itemId)}`" @click="target = { kind: 'starter', index }">
              <GameTooltipItemIcon :item="items[itemId] ?? null" :width="36" :height="36" class="size-9 rounded" />
            </button>
            <button type="button" class="absolute -right-1.5 -top-1.5 hidden size-4 items-center justify-center rounded-full bg-elevated text-dimmed ring-1 ring-default hover:text-error group-hover:flex group-focus-within:flex" :aria-label="`Remove ${name(itemId)}`" @click="removeStarter(index)">
              <UIcon name="i-lucide-x" class="size-3" />
            </button>
          </div>
          <button v-if="draft.starter.length < STARTER_MAX" type="button" class="flex size-9 items-center justify-center rounded border border-dashed border-default text-dimmed hover:border-primary/60 hover:text-primary" aria-label="Add a starter item" @click="target = { kind: 'starter', index: null }">
            <UIcon name="i-lucide-plus" class="size-4" />
          </button>
        </div>
      </div>

      <div>
        <p class="stat-label mb-2">Boots</p>
        <div class="group relative">
          <button v-if="draft.boots" type="button" class="block rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary" :aria-label="`Replace ${name(draft.boots)}`" @click="target = { kind: 'boots' }">
            <GameTooltipItemIcon :item="items[draft.boots] ?? null" :width="36" :height="36" class="size-9 rounded" />
          </button>
          <button v-else type="button" class="flex size-9 items-center justify-center rounded border border-dashed border-default text-dimmed hover:border-primary/60 hover:text-primary" aria-label="Choose boots" @click="target = { kind: 'boots' }">
            <UIcon name="i-lucide-plus" class="size-4" />
          </button>
          <button v-if="draft.boots" type="button" class="absolute -right-1.5 -top-1.5 hidden size-4 items-center justify-center rounded-full bg-elevated text-dimmed ring-1 ring-default hover:text-error group-hover:flex group-focus-within:flex" aria-label="No boots" @click="draft = withBoots(draft, null)">
            <UIcon name="i-lucide-x" class="size-3" />
          </button>
        </div>
      </div>
    </div>

    <div>
      <p class="stat-label mb-2">Runes</p>
      <KitRunePage v-if="runeTree" v-model="draft.runes" :tree="runeTree" />
      <USkeleton v-else class="h-40 w-72" />
    </div>

    <div>
      <p class="stat-label mb-1">Items</p>
      <p class="mb-3 text-xs text-dimmed">Click an item to change it, a + to add one: under an item, the one that follows it; beside items, another branch. The leftmost branch is the one followed in game.</p>
      <KitTree :tree="draft.tree" :items="items" :boots="draft.boots" editable @pick="target = { kind: 'tree', ...$event }" @remove="removeNode" />
    </div>

    <ul v-if="issues.length" class="space-y-1 text-sm text-warning" aria-live="polite">
      <li v-for="issue in issues" :key="issue" class="flex items-center gap-1.5">
        <UIcon name="i-lucide-triangle-alert" class="size-3.5 shrink-0" />{{ issue }}
      </li>
    </ul>

    <div class="flex items-center justify-end gap-2 border-t border-default pt-3">
      <UButton label="Cancel" color="neutral" variant="ghost" @click="emit('cancel')" />
      <UButton label="Save kit" icon="i-lucide-check" :disabled="issues.length > 0" :loading="saving" @click="emit('save', draft)" />
    </div>

    <KitItemPicker v-model:open="pickerOpen" :items="items" :groups="pickerGroups" :blocked="blocked" :title="pickerTitle" @pick="picked" />
  </div>
</template>
