<script setup lang="ts">
/**
 * A sortable column header of the list tables (#1734): the column's label as a
 * ghost button, with the direction it is sorted in — or a dimmed up/down glyph
 * when it is not the sort column. The table sorts manually, so the click only
 * tells the caller which way to go; the caller turns that into the URL, and the
 * URL into the API request (`utils/table-sorting.ts`).
 *
 * Typed structurally on the two TanStack column methods it reads, so it does
 * not import `@tanstack/vue-table`, which the app only reaches through Nuxt UI.
 */
// The idle up/down glyph only shows where the table has room (`@xl`, read off
// the table's container): on a phone it widened every sortable column for a
// hint the header's hover and focus states already give.
const props = withDefaults(defineProps<{
  column: { getIsSorted: () => false | 'asc' | 'desc' }
  label: string
  /** Full name of the column, for the button's accessible label (`WR` → `win rate`). */
  title?: string
  align?: 'start' | 'center' | 'end'
}>(), { title: undefined, align: 'start' })

const emit = defineEmits<{ sort: [] }>()

const sorted = computed(() => props.column.getIsSorted())

const icon = computed(() => {
  if (sorted.value === 'desc') return 'i-lucide-arrow-down-wide-narrow'
  if (sorted.value === 'asc') return 'i-lucide-arrow-up-narrow-wide'
  return 'i-lucide-arrow-up-down'
})

const JUSTIFY = { start: 'justify-start', center: 'justify-center', end: 'justify-end' } as const
</script>

<template>
  <div class="flex" :class="JUSTIFY[align]">
    <UButton
      color="neutral"
      variant="ghost"
      size="xs"
      :trailing-icon="icon"
      :aria-label="`Sort by ${title ?? label}`"
      :title="title"
      class="-mx-1 gap-0.5 px-1 py-1 text-[10px] font-medium uppercase leading-[1.2] tracking-[0.12em]"
      :class="sorted ? 'text-highlighted' : 'text-dimmed hover:text-default'"
      :ui="{ trailingIcon: sorted ? 'size-3' : 'hidden size-3 opacity-50 @xl:inline-flex' }"
      @click="emit('sort')"
    >
      {{ label }}
    </UButton>
  </div>
</template>
