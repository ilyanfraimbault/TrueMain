<script setup lang="ts">
import { loadingOfAlias, splashOfAlias } from '~/composables/useChampionStatics'

/**
 * Champion art as a background: fills its positioned parent and carries the
 * scrim the text needs. Either a champion id (resolved through Data Dragon) or
 * an alias (for the screens that have no champion of their own).
 *
 * The art fades in once it has decoded: a 1 MB splash arriving mid-draft must
 * not pop into place behind text that is being read.
 */
const props = withDefaults(defineProps<{
  championId?: number | null
  alias?: string | null
  kind?: 'splash' | 'loading'
  /** Which fade to lay over the art — see main.css. */
  fade?: 'x' | 'y' | 'vignette' | 'none'
  /** CSS object-position. Splashes keep their subject right of centre. */
  position?: string
}>(), { championId: null, alias: null, kind: 'splash', fade: 'x', position: '70% 20%' })

const { splashOf, loadingOf } = useChampionStatics()

const source = computed(() => {
  if (props.alias) return props.kind === 'loading' ? loadingOfAlias(props.alias) : splashOfAlias(props.alias)
  if (!props.championId) return null
  return props.kind === 'loading' ? loadingOf(props.championId) : splashOf(props.championId)
})

const decoded = ref(false)
watch(source, () => { decoded.value = false })

const scrim = computed(() => ({
  x: 'art-fade-x',
  y: 'art-fade-y',
  vignette: 'art-vignette',
  none: '',
}[props.fade]))
</script>

<template>
  <div class="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden="true">
    <img
      v-if="source"
      :key="source"
      :src="source"
      alt=""
      class="size-full object-cover transition-opacity duration-700 ease-out"
      :class="decoded ? 'opacity-100' : 'opacity-0'"
      :style="{ objectPosition: position }"
      decoding="async"
      @load="decoded = true"
    >
    <div v-if="scrim" class="absolute inset-0" :class="scrim" />
  </div>
</template>
