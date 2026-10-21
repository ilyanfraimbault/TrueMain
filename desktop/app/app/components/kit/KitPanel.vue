<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import { positionLabel } from '#common/utils/positions'
import { cloneKit, freeName, KIT_NAME_MAX, kitFromBuild } from '~/utils/kit'
import { defaultRunePage } from '~/utils/kit-runes'

/**
 * "My kits" on the app's champion page (#2185): the player's kits for the
 * champion and lane on the left, as the build view lists builds; on the right
 * the kit picked, or the editor. A new kit starts from TrueMain's build on the
 * lane — its starter, boots, core path, rune page and spells — so the player
 * swaps rather than fills. One kit per champion and lane is the one champion
 * select and the overlay use.
 */
const props = defineProps<{ championId: number, position: string }>()

const { kitsFor, activeIdFor, save, remove, duplicate, rename, setActive, error } = useKits()
const { response, pending: buildsPending } = useChampionBuilds(toRef(props, 'championId'), toRef(props, 'position'))
const { runeTree } = useStaticData()
const toast = useToast()

const kits = computed(() => kitsFor(props.championId, props.position))
const activeId = computed(() => activeIdFor(props.championId, props.position))

const selected = ref<string | null>(null)
/** The kit in the editor: a new one, or a copy of the one picked. */
const editing = ref<Kit | null>(null)
const saving = ref(false)

watch(() => `${props.championId}:${props.position}`, () => {
  editing.value = null
  selected.value = activeId.value ?? kits.value[0]?.id ?? null
}, { immediate: true })
watch(kits, (list) => {
  if (!list.some(kit => kit.id === selected.value)) selected.value = activeId.value ?? list[0]?.id ?? null
})

const shown = computed(() => kits.value.find(kit => kit.id === selected.value) ?? null)
const lane = computed(() => positionLabel(props.position, { short: true }))
const canCreate = computed(() => runeTree.value !== null && !buildsPending.value)

function create() {
  const core = response.value?.builds[0]?.core ?? null
  const name = freeName(`My ${lane.value || 'kit'}`, kits.value.map(kit => kit.name))
  const kit = kitFromBuild({ championId: props.championId, position: props.position, name }, core, runeTree.value ? defaultRunePage(runeTree.value) : null)
  if (kit) editing.value = kit
}

async function run(action: () => Promise<unknown>, failure: string) {
  try {
    await action()
  }
  catch {
    toast.add({ title: failure, description: error.value ?? undefined, icon: 'i-lucide-circle-x', color: 'error' })
  }
}

async function saveEdited(kit: Kit) {
  saving.value = true
  await run(async () => {
    await save(kit)
    selected.value = kit.id
    editing.value = null
  }, 'Could not save the kit')
  saving.value = false
}

const toggleActive = (kit: Kit) => run(() => setActive(kit.championId, kit.position, activeId.value === kit.id ? null : kit.id), 'Could not change the active kit')
const copy = (kit: Kit) => run(async () => {
  const made = await duplicate(kit.id)
  if (made) selected.value = made.id
}, 'Could not duplicate the kit')

const renaming = ref<{ id: string, name: string } | null>(null)
const deleting = ref<Kit | null>(null)

const menu = (kit: Kit) => [[
  { label: 'Duplicate', icon: 'i-lucide-copy', onSelect: () => copy(kit) },
  { label: 'Rename', icon: 'i-lucide-pencil-line', onSelect: () => (renaming.value = { id: kit.id, name: kit.name }) },
  { label: 'Delete', icon: 'i-lucide-trash-2', color: 'error' as const, onSelect: () => (deleting.value = kit) },
]]

async function confirmRename() {
  const target = renaming.value
  if (!target?.name.trim()) return
  await run(() => rename(target.id, target.name), 'Could not rename the kit')
  renaming.value = null
}

async function confirmDelete() {
  const target = deleting.value
  if (!target) return
  await run(() => remove(target.id), 'Could not delete the kit')
  deleting.value = null
}
</script>

<template>
  <div class="surface grid h-full min-h-0 grid-cols-[18rem_minmax(0,1fr)] overflow-hidden rounded-xl">
    <aside class="flex min-h-0 flex-col border-r border-default bg-muted/50">
      <div class="flex items-center justify-between gap-2 border-b border-default px-3 py-2.5">
        <span class="text-sm font-semibold text-highlighted">Your kits{{ lane ? ` · ${lane}` : '' }}</span>
        <UButton label="New kit" icon="i-lucide-plus" size="xs" color="neutral" variant="soft" :disabled="!canCreate || editing !== null" @click="create" />
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto p-2">
        <KitList v-if="kits.length" v-model="selected" :kits="kits" :active-id="activeId" />
        <p v-else class="px-2 py-1 text-xs text-dimmed">No kit on this lane yet. A new kit starts from TrueMain's build here.</p>
      </div>
    </aside>

    <div class="min-h-0 overflow-y-auto p-3">
      <KitEditor v-if="editing" :kit="editing" :saving="saving" @save="saveEdited" @cancel="editing = null" />

      <div v-else-if="shown" class="space-y-4">
        <div class="flex items-center gap-2">
          <h2 class="min-w-0 truncate text-base font-semibold text-highlighted">{{ shown.name }}</h2>
          <UBadge v-if="shown.id === activeId" label="Active" color="primary" variant="soft" size="sm" />
          <span class="flex-1" />
          <UButton
            :label="shown.id === activeId ? 'Used in champion select' : 'Use in champion select'"
            :icon="shown.id === activeId ? 'i-lucide-check' : 'i-lucide-swords'"
            size="xs"
            :color="shown.id === activeId ? 'primary' : 'neutral'"
            :variant="shown.id === activeId ? 'soft' : 'outline'"
            :title="shown.id === activeId ? 'Click to go back to TrueMain\'s build' : 'Champion select and the overlay follow this kit'"
            @click="toggleActive(shown)"
          />
          <UButton label="Edit" icon="i-lucide-pencil" size="xs" color="neutral" variant="ghost" @click="editing = cloneKit(shown)" />
          <UDropdownMenu :items="menu(shown)" :content="{ align: 'end' }">
            <UButton icon="i-lucide-ellipsis" size="xs" color="neutral" variant="ghost" aria-label="More actions" />
          </UDropdownMenu>
        </div>
        <KitBuild :kit="shown" />
      </div>

      <div v-else class="flex h-full flex-col items-center justify-center gap-2 p-8 text-center">
        <UIcon name="i-lucide-swords" class="size-6 text-dimmed" />
        <p class="text-sm text-muted">Your own build: an item tree, a rune page and two spells</p>
        <p class="max-w-xs text-xs text-dimmed">Mark one as used and champion select imports it, and the overlay follows its items.</p>
      </div>
    </div>

    <UModal :open="renaming !== null" title="Rename the kit" @update:open="renaming = $event ? renaming : null">
      <template #body>
        <form class="flex items-center gap-2" @submit.prevent="confirmRename">
          <UInput v-if="renaming" v-model="renaming.name" :maxlength="KIT_NAME_MAX" class="flex-1" aria-label="Kit name" />
          <UButton type="submit" label="Rename" :disabled="!renaming?.name.trim()" />
        </form>
      </template>
    </UModal>

    <UModal :open="deleting !== null" title="Delete the kit?" :description="deleting ? `${deleting.name} is removed from this computer.` : undefined" @update:open="deleting = $event ? deleting : null">
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <UButton label="Keep it" color="neutral" variant="ghost" @click="deleting = null" />
          <UButton label="Delete" color="error" @click="confirmDelete" />
        </div>
      </template>
    </UModal>
  </div>
</template>
