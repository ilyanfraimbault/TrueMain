<!--
  Twin of `web/app/components/SkeletonImage.vue`, with two app-specific
  differences, both deliberate:

  - No `_ipx` URL: the app has no image server, so the source is used as given.
  - No opacity gate on `load`: an image with a source is drawn as soon as it
    decodes. WKWebView, in the packaged app, never reported `load` for images
    inserted after the first one (see `ChampionArt.vue`), so an icon waiting
    for it to fade in would stay invisible for good. The loading skeleton is
    painted *behind* the picture instead (`img-skeleton`, theme.css), which the
    picture covers once decoded — except for a `transparent` one (lane glyphs,
    runes), where it would show through and is left out.

  Same props, same placeholder states (`pending`, `settled`, `fallback`), so the
  twinned tooltip and core-view components use it exactly as the site does.
-->
<script setup lang="ts">
import { iconPlaceholderClass, isIconUnresolved } from '#common/utils/icon-placeholder'
import { ICON_REMOUNT_KEY, isIconRepaint, markIconPainted } from '#common/utils/icon-repaint'

defineOptions({ inheritAttrs: false })

const props = defineProps<{
  src?: string | null
  alt?: string
  title?: string
  width?: number | string
  height?: number | string
  loading?: 'lazy' | 'eager'
  /** Label drawn instead of the skeleton when there is no `src` and nothing is `pending`. */
  fallback?: string
  /** The caller is still fetching whatever supplies `src`: keep the loading box. */
  pending?: boolean
  /** The caller's source is final: an empty slot draws the hollow "no icon" box. */
  settled?: boolean
  /** Transparent pixels (a lane glyph, a rune): no skeleton behind the picture. */
  transparent?: boolean
}>()

const failed = ref(false)

const reservedStyle = computed(() => {
  const { width, height } = props
  if (width == null || height == null) return undefined
  const toDimension = (value: number | string) => (typeof value === 'number' ? `${value}px` : value)
  return { width: toDimension(width), height: toDimension(height) }
})

watch(() => props.src, () => (failed.value = false))

// Re-created under the pointer by its tooltip's first hover (utils/icon-repaint.ts):
// decoded synchronously, or WKWebView paints the skeleton behind it for a frame.
// No `load` here to mark a source painted (see above), so mounting does.
const repaint = isIconRepaint(inject(ICON_REMOUNT_KEY, null)?.value === true, props.src)
onMounted(() => markIconPainted(props.src))
</script>

<template>
  <span v-bind="$attrs" class="inline-block overflow-hidden" :style="reservedStyle">
    <span class="relative block size-full">
      <span
        v-if="!src && fallback && !pending"
        class="absolute inset-0 flex items-center justify-center rounded border border-default text-xs"
      >
        {{ fallback }}
      </span>
      <span
        v-else-if="!src || failed"
        class="absolute inset-0 size-full rounded-md"
        :class="iconPlaceholderClass(failed || isIconUnresolved(Boolean(src), Boolean(settled)))"
      />
      <img
        v-if="src && !failed"
        :decoding="repaint ? 'sync' : undefined"
        :src="src"
        :alt="alt"
        :title="title"
        :loading="loading"
        class="relative size-full"
        :class="!transparent && 'img-skeleton'"
        @error="failed = true"
      >
    </span>
  </span>
</template>
