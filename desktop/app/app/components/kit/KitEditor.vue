<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import type { ChampionPosition } from '#common/utils/positions'
import type { KitItemGroup } from '~/utils/kit-items'
import { cloneKit, KIT_BOOTS_MAX, KIT_NAME_MAX, kitIssues, withBoots } from '~/utils/kit'
import { addChild, blockedItems, maxDepth, removeSubtree, replaceItem, swapNodes } from '~/utils/kit-tree'

/**
 * The kit editor (#2185): a draft of the kit, saved as a whole or dropped —
 * its name and the positions it is offered on (#2205) first.
 * Spells, starter and boots in their own slots over the rune page, then the
 * item tree, every node editable — legendaries only: what a build completes.
 * Several boots are the player's choice between them, the first the default. What keeps the kit from being saved is
 * listed on the Save button's tooltip, in the player's words (`kitIssues`).
 */
const props = defineProps<{ kit: Kit, saving?: boolean }>()
const emit = defineEmits<{ save: [kit: Kit], cancel: [] }>()

const { items, summoners, runeTree } = useStaticData()
const { classes: itemClasses } = useItemClasses()

const draft = ref<Kit>(cloneKit(props.kit))
watch(() => props.kit.id, () => (draft.value = cloneKit(props.kit)))

const issues = computed(() => kitIssues(draft.value, runeTree.value))

/** What the item picker is open for. */
type Target
  = | { kind: 'tree', at: number[], mode: 'replace' | 'child' }
    | { kind: 'starter', index: number | null }
    | { kind: 'boots', index: number | null }
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
    default: return ['legendary']
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
  if (at?.kind === 'boots') return draft.value.boots.filter((_, i) => i !== at.index)
  if (at?.kind !== 'tree') return []
  return [...blockedItems(draft.value.tree, at.at, at.mode), ...draft.value.boots]
})

/** `list` with `itemId` at `index`, or added for `null`. */
const placed = (list: number[], index: number | null, itemId: number) =>
  index === null ? [...list, itemId] : list.map((id, i) => (i === index ? itemId : id))

function picked(itemId: number) {
  const at = target.value
  if (!at) return
  const kit = draft.value
  if (at.kind === 'boots') draft.value = withBoots(kit, placed(kit.boots, at.index, itemId))
  else if (at.kind === 'starter') draft.value = { ...kit, starter: placed(kit.starter, at.index, itemId) }
  else {
    draft.value = { ...kit, tree: at.mode === 'replace' ? replaceItem(kit.tree, at.at, itemId) : addChild(kit.tree, at.at, itemId) }
  }
}

function swap(a: number[], b: number[]) {
  const tree = swapNodes(draft.value.tree, a, b, maxDepth(draft.value.boots))
  if (tree) draft.value = { ...draft.value, tree }
}
const removeNode = (at: number[]) => (draft.value = { ...draft.value, tree: removeSubtree(draft.value.tree, at) })
const removeStarter = (index: number) => (draft.value = { ...draft.value, starter: draft.value.starter.filter((_, i) => i !== index) })
const removeBoots = (index: number) => (draft.value = withBoots(draft.value, draft.value.boots.filter((_, i) => i !== index)))
</script>

<template>
  <div class="space-y-5">
    <div class="flex flex-wrap items-end gap-x-6 gap-y-3">
      <UFormField label="Name">
        <UInput v-model="draft.name" :maxlength="KIT_NAME_MAX" placeholder="Into tanks" class="w-72" />
      </UFormField>
      <UFormField label="Positions">
        <RolePicker :positions="draft.positions as ChampionPosition[]" @update:positions="draft.positions = $event" />
      </UFormField>
    </div>

    <div class="flex flex-wrap items-start gap-x-8 gap-y-4">
      <div>
        <p class="stat-label mb-2">Summoners</p>
        <KitSpells v-model="draft.spells" :summoners="summoners" />
      </div>

      <div>
        <p class="stat-label mb-2">Starter</p>
        <KitItemSlots :item-ids="draft.starter" :items="items" :max="STARTER_MAX" add-label="Add a starter item" @pick="index => (target = { kind: 'starter', index })" @remove="removeStarter" />
      </div>

      <div>
        <p class="stat-label mb-2">Boots</p>
        <KitItemSlots :item-ids="draft.boots" :items="items" :max="KIT_BOOTS_MAX" add-label="Add boots" @pick="index => (target = { kind: 'boots', index })" @remove="removeBoots" />
      </div>
    </div>

    <div>
      <p class="stat-label mb-2">Runes</p>
      <KitRunePage v-if="runeTree" v-model="draft.runes" :tree="runeTree" />
      <USkeleton v-else class="h-40 w-72" />
    </div>

    <div>
      <p class="stat-label mb-2">Items</p>
      <KitTree :tree="draft.tree" :items="items" :boots="draft.boots" editable @pick="target = { kind: 'tree', ...$event }" @remove="removeNode" @swap="swap" />
    </div>

    <div class="flex items-center justify-end gap-2 border-t border-default pt-3">
      <UButton label="Cancel" color="neutral" variant="ghost" @click="emit('cancel')" />
      <!-- What keeps the kit from being saved, on the button it disables. The
           tooltip hangs on a wrapper: a disabled button takes no pointer. -->
      <UTooltip :disabled="!issues.length" :content="{ side: 'top', align: 'end' }">
        <span class="inline-flex" :tabindex="issues.length ? 0 : undefined">
          <UButton label="Save" icon="i-lucide-check" :disabled="issues.length > 0" :loading="saving" @click="emit('save', draft)" />
        </span>
        <template #content>
          <ul class="space-y-1 text-xs" aria-label="Before saving">
            <li v-for="issue in issues" :key="issue" class="flex items-center gap-1.5">
              <UIcon name="i-lucide-triangle-alert" class="size-3.5 shrink-0 text-warning" />{{ issue }}
            </li>
          </ul>
        </template>
      </UTooltip>
    </div>

    <KitItemPicker v-model:open="pickerOpen" :items="items" :groups="pickerGroups" :blocked="blocked" :blocked-note="target?.kind === 'boots' ? 'already chosen' : 'already on this path'" :classes="itemClasses" :title="pickerTitle" @pick="picked" />
  </div>
</template>
