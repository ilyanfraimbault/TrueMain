<script setup lang="ts">
// A tile's picture: it fades in once decoded instead of painting top-down, and
// keeps its box while it loads (the tile's own fill shows through). Keyed on
// the URL, so a tile whose champion changes never shows the previous one.
const props = defineProps<{
  src: string | null | undefined
  width?: number
  height?: number
  /** `object-position`. */
  position?: string
  eager?: boolean
}>()

const loaded = ref(false)
const img = ref<HTMLImageElement | null>(null)

watch(() => props.src, () => {
  loaded.value = false
})

onMounted(() => {
  if (img.value?.complete && img.value.naturalWidth > 0) loaded.value = true
})
</script>

<template>
  <img
    v-if="src"
    :key="src"
    ref="img"
    :src="src"
    alt=""
    :width="width"
    :height="height"
    :loading="eager ? 'eager' : 'lazy'"
    decoding="async"
    class="g-art"
    :class="{ 'is-loaded': loaded }"
    :style="position ? { objectPosition: position } : undefined"
    @load="loaded = true"
  >
</template>
