<script setup lang="ts">
const props = withDefaults(defineProps<{
  src: string
  alt: string
  width?: string | number
  height?: string | number
  sizeClass?: string
  imageClass?: string
  wrapperClass?: string
  loading?: 'lazy' | 'eager'
}>(), {
  width: undefined,
  height: undefined,
  sizeClass: '',
  imageClass: '',
  wrapperClass: '',
  loading: 'lazy'
})

const isLoaded = ref(false)

watch(() => props.src, () => {
  isLoaded.value = false
}, {
  immediate: true
})

function handleLoad() {
  isLoaded.value = true
}
</script>

<template>
  <div
    class="relative overflow-hidden"
    :class="[sizeClass, wrapperClass]"
  >
    <USkeleton
      v-if="!isLoaded"
      class="absolute inset-0"
    />
    <NuxtImg
      :src="src"
      :alt="alt"
      :width="width"
      :height="height"
      :loading="loading"
      class="block h-full w-full object-cover transition-opacity duration-200"
      :class="[sizeClass, imageClass, isLoaded ? 'opacity-100' : 'opacity-0']"
      @load="handleLoad"
    />
  </div>
</template>
