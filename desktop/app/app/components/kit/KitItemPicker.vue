<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { KitItemGroup } from '~/utils/kit-items'
import { kitItemGroups, searchKitItems } from '~/utils/kit-items'

/**
 * The item picker of the kit editor: the patch's Rift shop, searched by name,
 * grouped — legendaries, boots, components, starters — as Data Dragon icons.
 * An item that would repeat one on the path is shown but cannot be taken.
 */
const props = withDefaults(defineProps<{
  items: Record<number, StaticItemData>
  /** The groups this slot takes, in picker order. */
  groups: KitItemGroup[]
  /** Items that cannot go here: they are already on the path. */
  blocked?: number[]
  title: string
}>(), { blocked: () => [] })

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ pick: [itemId: number] }>()

const query = ref('')
watch(open, (value) => {
  if (value) query.value = ''
})

const grouped = computed(() => kitItemGroups(props.items))
const sections = computed(() => searchKitItems(props.items, grouped.value, query.value, props.groups))

function pick(itemId: number) {
  if (props.blocked.includes(itemId)) return
  emit('pick', itemId)
  open.value = false
}
</script>

<template>
  <UModal v-model:open="open" :title="title" :ui="{ content: 'max-w-xl', body: 'p-0 sm:p-0' }">
    <template #body>
      <div class="border-b border-default p-3">
        <UInput v-model="query" icon="i-lucide-search" placeholder="Search an item" class="w-full" aria-label="Search an item" />
      </div>
      <div class="max-h-[22rem] space-y-4 overflow-y-auto p-3">
        <section v-for="section in sections" :key="section.group">
          <h3 class="stat-label mb-2">{{ section.label }}</h3>
          <div class="grid grid-cols-[repeat(auto-fill,minmax(2.25rem,1fr))] gap-1.5">
            <button
              v-for="item in section.items"
              :key="item.id"
              type="button"
              class="rounded ring-1 ring-transparent transition hover:ring-primary/60 focus-visible:ring-primary focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-30"
              :disabled="blocked.includes(item.id)"
              :aria-label="item.name"
              :title="blocked.includes(item.id) ? `${item.name} — already on this path` : `${item.name} · ${item.totalGold} gold`"
              @click="pick(item.id)"
            >
              <img :src="item.iconUrl" :alt="item.name" width="36" height="36" class="size-9 rounded img-skeleton">
            </button>
          </div>
        </section>
        <p v-if="sections.length === 0" class="py-6 text-center text-sm text-muted">No item matches “{{ query }}”.</p>
      </div>
    </template>
  </UModal>
</template>
