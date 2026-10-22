<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import { cloneKit } from '~/utils/kit'

/**
 * My kits (#2205): every kit of the player's, grouped by champion, read and
 * edited as on the champion page (`KitDetail`, `KitEditor`), and new ones made
 * here for any champion and one or several positions (`KitNew`). Each is
 * offered in champion select on its positions, beside TrueMain's builds.
 */
const { store, save } = useKits()
const { nameOf, portraitOf } = useChampionStatics()
const run = useKitAction()

const groups = computed(() => {
  const byChampion = new Map<number, Kit[]>()
  for (const kit of store.value.kits) byChampion.set(kit.championId, [...(byChampion.get(kit.championId) ?? []), kit])
  return [...byChampion.entries()]
    .map(([championId, kits]) => ({ championId, name: nameOf(championId) || `Champion ${championId}`, kits: [...kits].sort((a, b) => a.name.localeCompare(b.name)) }))
    .sort((a, b) => a.name.localeCompare(b.name))
})

const selected = ref<string | null>(null)
const editing = ref<Kit | null>(null)
const saving = ref(false)
const creating = ref(false)

watch(groups, (list) => {
  const kits = list.flatMap(group => group.kits)
  if (!kits.some(kit => kit.id === selected.value)) selected.value = kits[0]?.id ?? null
}, { immediate: true })

const shown = computed(() => store.value.kits.find(kit => kit.id === selected.value) ?? null)

async function saveEdited(kit: Kit) {
  saving.value = true
  if (await run(() => save(kit), 'Could not save the kit')) {
    selected.value = kit.id
    editing.value = null
  }
  saving.value = false
}

</script>

<template>
  <div class="flex h-full min-h-0 flex-col gap-4 p-5">
    <div class="flex items-center gap-3">
      <UIcon name="i-lucide-backpack" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">My kits</h1>
      <UButton class="ml-auto" label="New kit" icon="i-lucide-plus" size="sm" :disabled="editing !== null" @click="creating = true" />
    </div>

    <div v-if="groups.length || editing" class="surface grid min-h-0 flex-1 grid-cols-[18rem_minmax(0,1fr)] overflow-hidden rounded-xl">
      <aside class="min-h-0 overflow-y-auto border-r border-default bg-muted/50 p-2">
        <section v-for="group in groups" :key="group.championId" class="mb-3 last:mb-0">
          <h2 class="flex items-center gap-2 px-2.5 py-1.5">
            <img :src="portraitOf(group.championId) ?? undefined" :alt="group.name" width="20" height="20" class="size-5 rounded img-skeleton">
            <span class="stat-label">{{ group.name }}</span>
          </h2>
          <KitList v-model="selected" :kits="group.kits" show-positions />
        </section>
      </aside>

      <div class="min-h-0 overflow-y-auto p-3">
        <KitEditor v-if="editing" :kit="editing" :saving="saving" @save="saveEdited" @cancel="editing = null" />
        <KitDetail
          v-else-if="shown"
          :kit="shown"
          @edit="editing = cloneKit(shown)"
          @duplicated="id => (selected = id)"
        />
      </div>
    </div>

    <div v-else class="flex flex-1 flex-col items-center justify-center gap-2 text-center">
      <UIcon name="i-lucide-backpack" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">Your own builds: an item tree, a rune page and two spells, for a champion on the positions you pick</p>
      <p class="max-w-sm text-xs text-dimmed">Champion select offers it beside TrueMain's builds; picked there, the overlay follows its items.</p>
      <UButton class="mt-2" label="New kit" icon="i-lucide-plus" size="sm" @click="creating = true" />
    </div>

    <KitNew v-model:open="creating" @create="kit => (editing = kit)" />
  </div>
</template>
