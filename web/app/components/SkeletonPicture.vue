<script setup lang="ts">
import { iconPlaceholderClass } from '#common/utils/icon-placeholder'

/**
 * A large picture — the app captures on the home and download pages — with a
 * skeleton in its place until it has loaded, as `SkeletonImage` does for icons.
 * The source is served as given: these are the site's own static files, already
 * sized for their box, not icons to funnel through the canonical IPX fetch —
 * hence a root-relative path, never an upstream URL.
 *
 * The `width`/`height` attributes give the <img> its aspect ratio before a
 * byte arrives, so the skeleton fills the picture's final box from first paint.
 * The caller's class (rounding, ring, shadow) lands on the wrapper, so the
 * frame is drawn around the skeleton too, not around an empty box.
 */
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  src: `/${string}`
  alt: string
  width: number
  height: number
  loading?: 'lazy' | 'eager'
}>()

const loaded = ref(false)
const failed = ref(false)
watch(() => props.src, () => {
  loaded.value = false
  failed.value = false
})

// Same two guards as SkeletonImage: a cached picture can finish loading before
// hydration attaches `@load`, and one already complete at mount skips the fade.
const imgEl = ref<HTMLImageElement | null>(null)
const instant = ref(false)
onMounted(() => {
  if (imgEl.value?.complete && imgEl.value.naturalWidth > 0) {
    instant.value = true
    loaded.value = true
  }
})
</script>

<template>
  <div v-bind="$attrs" class="relative overflow-hidden">
    <USkeleton v-if="!loaded && !failed" class="absolute inset-0 size-full rounded-none" />
    <div v-else-if="failed" class="absolute inset-0" :class="iconPlaceholderClass(true)" />
    <img
      ref="imgEl"
      :src="src"
      :alt="alt"
      :width="width"
      :height="height"
      :loading="loading"
      class="block h-auto w-full"
      :class="[loaded && !failed ? 'opacity-100' : 'opacity-0', instant ? '' : 'transition-opacity duration-300']"
      @load="loaded = true"
      @error="failed = true"
    >
  </div>
</template>
