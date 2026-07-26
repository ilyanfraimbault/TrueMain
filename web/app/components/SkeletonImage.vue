<script setup lang="ts">
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  src?: string | null
  alt?: string
  title?: string
  width?: number | string
  height?: number | string
  /**
   * Native browser lazy-loading hint forwarded to the underlying <img>. Set to
   * `'lazy'` for off-screen icons (e.g. match-history rows below the fold) so
   * the browser defers their fetch until they scroll near the viewport; leave
   * unset for above-the-fold icons that should load immediately.
   */
  loading?: 'lazy' | 'eager'
}>()

const loaded = ref(false)
const failed = ref(false)

// Reserve the box before the image loads so the skeleton (and the later image)
// occupy their final size from first paint — no layout shift / forced reflow as
// each of the ~95 icons on a champion page resolves. Callers usually also pass
// a Tailwind `size-*` class; this inline reservation is the robust fallback for
// the few that size via width/height alone. Emitted only when both dimensions
// are known so class-only call sites (e.g. `size-full`) keep governing layout.
const reservedStyle = computed(() => {
  const { width, height } = props
  if (width == null || height == null) return undefined
  const toDimension = (value: number | string) =>
    typeof value === 'number' ? `${value}px` : value
  return { width: toDimension(width), height: toDimension(height) }
})

// Reset the placeholder when the source changes — without this, switching
// builds/tabs keeps the previous "loaded" state and the new image flashes
// straight in without ever showing a skeleton.
watch(() => props.src, () => {
  loaded.value = false
  failed.value = false
})

// Canonical IPX fetch size. DDragon ships item icons at 64×64; CDragon perk
// icons are larger but downscale cleanly. Funneling every <NuxtImg> request
// through this one size makes the same asset share a single browser cache
// entry no matter the CSS display size of its instances (build path 36 px,
// runes 32 px, tabs 28 px, shards 16 px — all hit the same `_ipx/s_64x64/…`
// URL). Display size still comes from the caller's wrapper class (`size-9`,
// `size-7`, etc.) via the `size-full` rule on the inner img.
const FETCH_SIZE = 64
</script>

<template>
  <!--
    Nested wrappers so the user-supplied class on the outer span (size,
    position, rounded, etc.) keeps full control of layout while the inner
    span owns the positioning context for the skeleton + image overlay.
    Putting `relative` on the outer would lose to a caller-supplied
    `absolute` in Tailwind's utility order, so the inner wrapper carries it.
  -->
  <span
    v-bind="$attrs"
    class="inline-block overflow-hidden"
    :style="reservedStyle"
  >
    <span class="relative block size-full">
      <USkeleton
        v-if="!src || !loaded || failed"
        class="absolute inset-0 size-full"
      />
      <NuxtImg
        v-if="src"
        :src="src"
        :alt="alt"
        :title="title"
        :width="FETCH_SIZE"
        :height="FETCH_SIZE"
        :loading="loading"
        densities="1x"
        class="size-full transition-opacity duration-150"
        :class="loaded && !failed ? 'opacity-100' : 'opacity-0'"
        @load="loaded = true"
        @error="failed = true"
      />
    </span>
  </span>
</template>

