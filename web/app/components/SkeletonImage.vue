<script setup lang="ts">
import { ICON_FETCH_SIZE } from '~/utils/icon-fetch'

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

// Canonical IPX fetch size, shared with every other icon call site through
// `useCanonicalIcon()` — see utils/icon-fetch.ts for why one size matters.
// Display size still comes from the caller's wrapper class (`size-9`,
// `size-7`, etc.) via the `size-full` rule on the inner img.
const FETCH_SIZE = ICON_FETCH_SIZE

// Plain <img> + a URL built once, instead of <NuxtImg>. This component backs
// every icon on the site (hundreds per page), and profiling showed the long
// main-thread tasks on data arrival were dominated by NuxtImg's responsive
// machinery — a component instance per icon computing and writing
// srcset/sizes attributes that a fixed 64×64 fetch (`densities="1x"`) never
// needed. `useCanonicalIcon()` yields the identical `_ipx/…` URL, so browser
// and server IPX caches are unaffected.
// WebP for every icon that reaches this component. Measured on the live
// assets at the 64×64 fetch size: champion 10194 B -> 1100 B, perk
// 8933 B -> 3396 B, item 6096 B -> 2130 B, all indistinguishable from the PNG
// at display size (the perk icons — thin bright line art on transparency —
// are the demanding case and survive it). A champion page moves ~600 KB of
// icons, so this is the cheapest large cut available to it.
//
// Deliberately NOT applied globally: RankIcon's sources are `.svg`, which IPX
// currently passes straight through as `image/svg+xml`. Forcing a raster
// format there would trade a vector that stays crisp at any DPR for a 20 px
// bitmap, so that component builds its own URL and keeps the SVG.
const canonicalIcon = useCanonicalIcon()
const optimizedSrc = computed(() => canonicalIcon(props.src))

// A cached image can finish loading before hydration attaches the @load
// listener; without this check the skeleton would cover it forever.
const imgEl = ref<HTMLImageElement | null>(null)
onMounted(() => {
  if (imgEl.value?.complete && imgEl.value.naturalWidth > 0) loaded.value = true
})
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
      <!-- Plain span mirroring USkeleton's look (animate-pulse + rounded-md +
           the fill set on `ui.skeleton.base` in app.config.ts) — one fewer
           component instance per icon, which matters at hundreds of icons per
           page. **Keep the fill in sync with that override**: this is the most
           numerous skeleton on the site, so it is what "the page is loading"
           actually looks like. It used to be `bg-elevated`, i.e. the exact fill
           of every card it sits in — invisible.
           The pulse is dropped once the image has actually failed: a 404 is a
           final state, and animating it made dead icons (e.g. a profile icon
           Data Dragon doesn't ship) read as loading forever. -->
      <span
        v-if="!src || !loaded || failed"
        class="absolute inset-0 size-full rounded-md bg-ink-700"
        :class="failed ? '' : 'animate-pulse'"
      />
      <img
        v-if="src"
        ref="imgEl"
        :src="optimizedSrc"
        :alt="alt"
        :title="title"
        :width="FETCH_SIZE"
        :height="FETCH_SIZE"
        :loading="loading"
        class="size-full transition-opacity duration-150"
        :class="loaded && !failed ? 'opacity-100' : 'opacity-0'"
        @load="loaded = true"
        @error="failed = true"
      >
    </span>
  </span>
</template>

