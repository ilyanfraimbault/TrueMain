<!--
  Twin of `web/app/components/SkeletonImage.vue`, with two app-specific
  differences, both deliberate:

  - No `_ipx` URL: the app has no image server, so the source is used as given.
  - No opacity gate on `load`: an image with a source is drawn as soon as it
    decodes, with no skeleton behind it (rune icons are transparent, and one
    would show through). WKWebView, in the packaged app, never reported `load`
    for images inserted after the first one (see `ChampionArt.vue`), so an icon
    waiting for it to fade in would stay invisible for good.

  Same props, same placeholder states (`pending`, `settled`, `fallback`), so the
  twinned tooltip and core-view components use it exactly as the site does.
-->
<script setup lang="ts">
import { iconPlaceholderClass, isIconUnresolved } from '#common/utils/icon-placeholder'

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
}>()

const failed = ref(false)

const reservedStyle = computed(() => {
  const { width, height } = props
  if (width == null || height == null) return undefined
  const toDimension = (value: number | string) => (typeof value === 'number' ? `${value}px` : value)
  return { width: toDimension(width), height: toDimension(height) }
})

watch(() => props.src, () => (failed.value = false))
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
        :src="src"
        :alt="alt"
        :title="title"
        :loading="loading"
        class="relative size-full"
        @error="failed = true"
      >
    </span>
  </span>
</template>
