<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { Kit } from '#shared/types/kit'
import { KIT_NAME_MAX } from '~/utils/kit'

/**
 * One kit as the player reads it (#2185, #2205): its name, Edit, and
 * Duplicate / Rename / Delete (asks first) in a menu, over the kit drawn as a
 * build (`KitBuild`). Shared by the champion page and the My kits page. Every
 * kit is offered in champion select on its positions; there is nothing to
 * switch on.
 */
const props = defineProps<{ kit: Kit }>()
const emit = defineEmits<{ edit: [], duplicated: [id: string] }>()

const { remove, duplicate, rename } = useKits()
const run = useKitAction()

const renaming = ref<string | null>(null)
const deleting = ref(false)

async function copy(): Promise<void> {
  await run(async () => {
    const made = await duplicate(props.kit.id)
    if (made) emit('duplicated', made.id)
  }, 'Could not duplicate the kit')
}

const menu = computed<DropdownMenuItem[][]>(() => [[
  { label: 'Duplicate', icon: 'i-lucide-copy', onSelect: copy },
  { label: 'Rename', icon: 'i-lucide-pencil-line', onSelect: () => (renaming.value = props.kit.name) },
  { label: 'Delete', icon: 'i-lucide-trash-2', color: 'error', onSelect: () => (deleting.value = true) },
]])

async function confirmRename() {
  const name = renaming.value?.trim()
  if (!name) return
  await run(() => rename(props.kit.id, name), 'Could not rename the kit')
  renaming.value = null
}

async function confirmDelete() {
  await run(() => remove(props.kit.id), 'Could not delete the kit')
  deleting.value = false
}
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center gap-2">
      <h2 class="min-w-0 truncate text-base font-semibold text-highlighted">{{ kit.name }}</h2>
      <span class="flex-1" />
      <UButton label="Edit" icon="i-lucide-pencil" size="xs" color="neutral" variant="ghost" @click="emit('edit')" />
      <UDropdownMenu :items="menu" :content="{ align: 'end' }">
        <UButton icon="i-lucide-ellipsis" size="xs" color="neutral" variant="ghost" aria-label="More actions" />
      </UDropdownMenu>
    </div>
    <KitBuild :kit="kit" />

    <UModal :open="renaming !== null" title="Rename the kit" @update:open="renaming = $event ? renaming : null">
      <template #body>
        <form class="flex items-center gap-2" @submit.prevent="confirmRename">
          <UInput v-if="renaming !== null" v-model="renaming" :maxlength="KIT_NAME_MAX" class="flex-1" aria-label="Kit name" />
          <UButton type="submit" label="Rename" :disabled="!renaming?.trim()" />
        </form>
      </template>
    </UModal>

    <UModal :open="deleting" title="Delete the kit?" :description="`${kit.name} is removed from this computer.`" @update:open="deleting = $event">
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <UButton label="Keep it" color="neutral" variant="ghost" @click="deleting = false" />
          <UButton label="Delete" color="error" @click="confirmDelete" />
        </div>
      </template>
    </UModal>
  </div>
</template>
