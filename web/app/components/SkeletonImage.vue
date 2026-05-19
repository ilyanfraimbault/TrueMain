<script setup lang="ts">
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  src?: string | null
  alt?: string
  title?: string
  width?: number | string
  height?: number | string
}>()

const loaded = ref(false)
const failed = ref(false)

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
        class="size-full transition-opacity duration-150"
        :class="loaded && !failed ? 'opacity-100' : 'opacity-0'"
        @load="loaded = true"
        @error="failed = true"
      />
    </span>
  </span>
</template>

