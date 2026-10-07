<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type { RuneSheet } from '#shared/types/static-data'
import { runeSheetIconStyle } from '#shared/utils/rune-sheet'
import { iconPlaceholderClass } from '#common/utils/icon-placeholder'
import { loadSheetImage, sheetImageState } from '#common/utils/sheet-image'

/**
 * One rune cut out of its tree's sprite sheet (#999) — the rune tree's stand-in
 * for `SkeletonImage`, with the same box and the same skeleton.
 *
 * A background has no `alt`, so the accessible name is put back with
 * `role="img"` + `aria-label`. If the sheet fails to load, the rune is drawn the
 * usual way, from its own icon, inside the same root element (a tooltip trigger
 * keeps its DOM element).
 */
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  sheet: RuneSheet
  perkId: number
  /** The rune's own icon, drawn instead if the sheet fails. */
  src?: string | null
  alt?: string
  /** Display size, px. */
  size: number
}>()

const mounted = ref(false)
onMounted(() => {
  mounted.value = true
  loadSheetImage(props.sheet.url)
})
watch(() => props.sheet.url, url => loadSheetImage(url))

const state = computed(() => (mounted.value ? sheetImageState(props.sheet.url) : 'loading'))
const iconStyle = computed(() =>
  state.value === 'loaded' ? runeSheetIconStyle(props.sheet, props.perkId, props.size) : null,
)
</script>

<template>
  <span
    v-bind="$attrs"
    class="inline-block overflow-hidden"
    :style="{ width: `${size}px`, height: `${size}px` }"
  >
    <SkeletonImage
      v-if="state === 'failed'"
      :src="src"
      :alt="alt"
      :width="size"
      :height="size"
      transparent
      class="size-full"
    />
    <span
      v-else
      role="img"
      :aria-label="alt"
      class="relative block size-full"
      :style="iconStyle ?? undefined"
    >
      <span
        v-if="state === 'loading'"
        class="absolute inset-0 size-full rounded-md"
        :class="iconPlaceholderClass(false)"
      />
    </span>
  </span>
</template>
