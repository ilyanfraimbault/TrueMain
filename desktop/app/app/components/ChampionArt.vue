<script setup lang="ts">
import { loadingOfAlias, splashOfAlias } from '~/composables/useChampionStatics'

/**
 * Champion art as a background: fills its positioned parent and carries the
 * scrim the text needs. Either a champion id (resolved through Data Dragon) or
 * an alias (for the screens that have no champion of their own).
 *
 * The art is drawn as soon as it decodes, with **no fade in**. Two attempts at
 * one were reverted for the same reason: both started the image at opacity 0
 * and waited for something to reveal it — first the `load` event, then a CSS
 * animation. In the packaged app neither reached art inserted after the first
 * image, and WKWebView left every later splash invisible for good while a
 * browser showed all of them. A picture that is either there or not cannot
 * fail that way, and a pick landing is abrupt in champion select anyway.
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

const scrim = computed(() => ({
  'x': 'art-fade-x',
  'y': 'art-fade-y',
  'vignette': 'art-vignette',
  'none': '',
}[props.fade]))
</script>

<template>
  <div class="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden="true">
    <img
      v-if="source"
      :key="source"
      :src="source"
      alt=""
      class="size-full object-cover"
      :style="{ objectPosition: position }"
      decoding="async"
    >
    <div v-if="scrim" class="absolute inset-0" :class="scrim" />
  </div>
</template>
