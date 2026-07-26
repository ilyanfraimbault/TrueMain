<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Searchable champion select with avatar thumbnails. Wraps Nuxt UI's
// USelectMenu so the parent can bind through a numeric championId without
// converting to/from option shapes.
// `withDefaults` is required for the boolean default: Vue 3 coerces
// undeclared boolean props to `false` (not `undefined`), so `?? true`
// fallbacks silently break unless we pin the default here.
const props = withDefaults(
  defineProps<{
    champions: ChampionStaticListItem[]
    championId: number | null
    /** Placeholder shown when no champion is selected. */
    placeholder?: string
    /**
     * Tailwind classes applied to the picker. Sits on the wrapper so the
     * inline clear button can absolute-position itself inside the same
     * bounding box. Defaults to a compact `w-44`; pass `w-64` etc. for
     * wider variants.
     */
    triggerClass?: string
    /**
     * Render an inline X button that clears the selection without going
     * through the dropdown. Defaults to true — the global "Clear all"
     * affordance still exists in the filter strips but per-field clearing
     * is much faster when only one filter needs to change.
     */
    clearable?: boolean
    /** Control size forwarded to the underlying USelectMenu. */
    size?: 'xs' | 'sm' | 'md' | 'lg' | 'xl'
    /** Lock the picker: no dropdown, no clear button (read-only display). */
    disabled?: boolean
  }>(),
  { clearable: true },
)

const emit = defineEmits<{
  'update:championId': [value: number | null]
}>()

const championItems = computed(() =>
  [...props.champions]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(c => ({
      label: c.name,
      value: c.championId,
      iconUrl: c.iconUrl,
    })),
)

const selectedChampion = computed(() =>
  championItems.value.find(c => c.value === props.championId))

// Icon box matching USelectMenu's own avatar sizing per control size (3xs =
// size-4, 2xs = size-5, xs = size-6), so swapping UAvatar for SkeletonImage
// doesn't change the layout.
const iconSizeClass = computed(() => {
  switch (props.size) {
    case 'xs':
    case 'sm': return 'size-4'
    case 'xl': return 'size-6'
    default: return 'size-5'
  }
})

// Still passed to USelectMenu even though the `#leading` slot draws the icon
// itself: the trigger's start padding comes from the theme's `leading` variant,
// which is keyed off this prop (`!!slots.leading` is read once, non-reactively,
// so a slot alone leaves the label sliding under the icon). The slot replaces
// what this renders, so only its presence matters here.
const triggerAvatar = computed(() =>
  selectedChampion.value ? { src: selectedChampion.value.iconUrl } : undefined)

const showClear = computed(() => props.clearable && !props.disabled && props.championId !== null)

function onChange(value: { value: number } | undefined) {
  emit('update:championId', value?.value ?? null)
}

function clear(event: Event) {
  // Stop propagation so the parent USelectMenu trigger doesn't pop open
  // the listbox on the same click that meant to clear the field.
  event.stopPropagation()
  emit('update:championId', null)
}
</script>

<template>
  <div :class="['relative inline-flex', triggerClass ?? 'w-44']">
    <USelectMenu
      :model-value="selectedChampion"
      :items="championItems"
      :placeholder="placeholder ?? 'Any champion'"
      :avatar="triggerAvatar"
      :size="size"
      :disabled="disabled"
      searchable
      searchable-placeholder="Search champion…"
      class="w-full"
      @update:model-value="onChange"
    >
      <!-- SkeletonImage instead of the built-in UAvatar on both the trigger and
           the list rows: the menu re-uses the same row DOM as the search
           narrows, so a plain <img> keeps painting the previous champion until
           the new icon decodes (typing "kai" quickly leaves Aatrox's icon on
           Kai'Sa's row). SkeletonImage blanks to a skeleton on every src
           change, so a row never shows the wrong champion. -->
      <template v-if="selectedChampion" #leading>
        <SkeletonImage
          :src="selectedChampion.iconUrl"
          :alt="''"
          :class="[iconSizeClass, 'shrink-0 rounded-full']"
        />
      </template>
      <template #item-leading="{ item }">
        <SkeletonImage
          :src="item.iconUrl"
          :alt="''"
          :class="[iconSizeClass, 'shrink-0 rounded-full']"
        />
      </template>
    </USelectMenu>
    <button
      v-if="showClear"
      type="button"
      class="absolute inset-y-0 end-7 inline-flex items-center px-1 text-dimmed transition-colors hover:text-default focus:outline-none focus-visible:text-default"
      aria-label="Clear champion filter"
      @click.stop="clear"
    >
      <UIcon name="i-lucide-x" class="size-4" />
    </button>
  </div>
</template>
