<script setup lang="ts">
/**
 * Design exploration (prototype): Apple-style liquid glass.
 *
 * Chromium draws `backdrop-filter: url(#svg-filter)`, so the backdrop behind the
 * element goes through an SVG filter: a slight frost, then an `feDisplacementMap`
 * whose map encodes how a glass slab with a rounded bezel bends light — nothing
 * in the flat middle, a strong inward pull along the rim (the "lensing" edge),
 * optionally a whole-area magnification (`zoom`, a lens). Three passes at
 * slightly different strengths, one per colour channel, give the rim its faint
 * chromatic fringe.
 *
 * The map is generated on a canvas at the element's size and regenerated when
 * it resizes. Everywhere else (WebKit, Gecko, `lite` FX) the same element
 * renders as frosted glass: blur + saturation, same rim light.
 */
const props = withDefaults(defineProps<{
  as?: string
  /** Corner radius in px; anything ≥ half the height gives a capsule. */
  radius?: number
  /** Width of the curved rim, px. */
  bezel?: number
  /** Strongest rim displacement, px. */
  depth?: number
  /** > 1 magnifies the whole interior like a lens. */
  zoom?: number
  /** Frost under the refraction (feGaussianBlur stdDeviation). */
  blur?: number
  saturation?: number
  /** 0..1 — chromatic split along the rim. */
  aberration?: number
}>(), {
  as: 'div',
  radius: 24,
  bezel: 22,
  depth: 26,
  zoom: 1,
  blur: 0.6,
  saturation: 1.6,
  aberration: 0.4,
})

const fx = useFx()
const root = ref<HTMLElement | null>(null)
const mounted = ref(false)
const filterId = `lg-${useId().replace(/[^\w-]/g, '')}`
const map = shallowRef<{ href: string, width: number, height: number, scale: number } | null>(null)

const MAP_MAX_SIDE = 640

function buildMap(width: number, height: number) {
  const W = Math.max(2, Math.round(width))
  const H = Math.max(2, Math.round(height))
  const k = Math.min(1, MAP_MAX_SIDE / Math.max(W, H))
  const mw = Math.max(2, Math.round(W * k))
  const mh = Math.max(2, Math.round(H * k))
  const r = Math.min(props.radius, W / 2, H / 2)
  const bezel = Math.max(1, Math.min(props.bezel, Math.min(W, H) / 2))
  const dxs = new Float32Array(mw * mh)
  const dys = new Float32Array(mw * mh)
  const inside = new Uint8Array(mw * mh)
  const zoomK = 1 - 1 / props.zoom
  let max = 0.0001

  for (let y = 0; y < mh; y++) {
    for (let x = 0; x < mw; x++) {
      const px = (x + 0.5) / k
      const py = (y + 0.5) / k
      const cx = px - W / 2
      const cy = py - H / 2
      const qx = Math.abs(cx) - (W / 2 - r)
      const qy = Math.abs(cy) - (H / 2 - r)
      const ox = Math.max(qx, 0)
      const oy = Math.max(qy, 0)
      const outside = Math.hypot(ox, oy)
      const sd = outside + Math.min(Math.max(qx, qy), 0) - r
      if (sd > 0) continue
      const i = y * mw + x
      inside[i] = 1
      const dist = -sd
      let nx = 0
      let ny = 0
      if (qx > 0 && qy > 0 && outside > 0) {
        nx = (ox / outside) * Math.sign(cx)
        ny = (oy / outside) * Math.sign(cy)
      }
      else if (qx > qy) {
        nx = Math.sign(cx)
      }
      else {
        ny = Math.sign(cy)
      }
      let dx = 0
      let dy = 0
      if (dist < bezel) {
        // Convex rim: the surface is steepest at the very edge, so the bend is too.
        const t = dist / bezel
        const bend = (1 - t) ** 2.2
        dx -= nx * bend * props.depth
        dy -= ny * bend * props.depth
      }
      if (zoomK !== 0) {
        dx -= cx * zoomK
        dy -= cy * zoomK
      }
      dxs[i] = dx
      dys[i] = dy
      max = Math.max(max, Math.abs(dx), Math.abs(dy))
    }
  }

  const canvas = document.createElement('canvas')
  canvas.width = mw
  canvas.height = mh
  const ctx = canvas.getContext('2d')
  if (!ctx) return null
  const image = ctx.createImageData(mw, mh)
  for (let i = 0; i < mw * mh; i++) {
    const o = i * 4
    image.data[o] = inside[i] ? 128 + Math.round((127 * dxs[i]!) / max) : 128
    image.data[o + 1] = inside[i] ? 128 + Math.round((127 * dys[i]!) / max) : 128
    image.data[o + 2] = 128
    image.data[o + 3] = 255
  }
  ctx.putImageData(image, 0, 0)
  return { href: canvas.toDataURL(), width: W, height: H, scale: max * 2 }
}

const active = computed(() => mounted.value && fx.value.refraction && map.value !== null)

let frame = 0
let observer: ResizeObserver | null = null

function rebuild() {
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(() => {
    const el = root.value
    if (!el || !fx.value.refraction) return
    // Layout size, not getBoundingClientRect: a scale or tilt transform on the
    // element (an entrance, a hover) would otherwise bake into the map.
    const width = el.offsetWidth
    const height = el.offsetHeight
    if (width === 0 || height === 0) return
    if (map.value && Math.abs(map.value.width - width) < 1 && Math.abs(map.value.height - height) < 1) return
    map.value = buildMap(width, height)
  })
}

onMounted(() => {
  mounted.value = true
  if (!root.value) return
  observer = new ResizeObserver(rebuild)
  observer.observe(root.value)
  rebuild()
})

watch(() => fx.value.refraction, (on) => {
  if (on) rebuild()
})

onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  observer?.disconnect()
})

const channelScales = computed(() => {
  const s = map.value?.scale ?? 0
  const a = props.aberration
  return [s, s * (1 - a * 0.1), s * (1 - a * 0.2)]
})
</script>

<template>
  <component
    :is="as"
    ref="root"
    v-spotlight
    class="fx-glass"
    :class="{ 'fx-glass--refract': active }"
    :style="{
      '--lg-radius': `${radius}px`,
      ...(active ? { backdropFilter: `url(#${filterId})` } : {}),
    }"
  >
    <svg
      v-if="active && map"
      aria-hidden="true"
      class="pointer-events-none absolute size-0"
      color-interpolation-filters="sRGB"
    >
      <filter
        :id="filterId"
        x="0"
        y="0"
        :width="map.width"
        :height="map.height"
        filterUnits="userSpaceOnUse"
        color-interpolation-filters="sRGB"
      >
        <feGaussianBlur
          in="SourceGraphic"
          :stdDeviation="blur"
          result="frost"
        />
        <feImage
          :href="map.href"
          x="0"
          y="0"
          :width="map.width"
          :height="map.height"
          preserveAspectRatio="none"
          result="map"
        />
        <template v-if="aberration > 0">
          <feDisplacementMap
            in="frost"
            in2="map"
            :scale="channelScales[0]"
            xChannelSelector="R"
            yChannelSelector="G"
            result="dr"
          />
          <feColorMatrix
            in="dr"
            type="matrix"
            values="1 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0"
            result="r"
          />
          <feDisplacementMap
            in="frost"
            in2="map"
            :scale="channelScales[1]"
            xChannelSelector="R"
            yChannelSelector="G"
            result="dg"
          />
          <feColorMatrix
            in="dg"
            type="matrix"
            values="0 0 0 0 0  0 1 0 0 0  0 0 0 0 0  0 0 0 1 0"
            result="g"
          />
          <feDisplacementMap
            in="frost"
            in2="map"
            :scale="channelScales[2]"
            xChannelSelector="R"
            yChannelSelector="G"
            result="db"
          />
          <feColorMatrix
            in="db"
            type="matrix"
            values="0 0 0 0 0  0 0 0 0 0  0 0 1 0 0  0 0 0 1 0"
            result="b"
          />
          <feBlend
            in="r"
            in2="g"
            mode="screen"
            result="rg"
          />
          <feBlend
            in="rg"
            in2="b"
            mode="screen"
            result="rgb"
          />
          <feColorMatrix
            in="rgb"
            type="saturate"
            :values="saturation"
          />
        </template>
        <template v-else>
          <feDisplacementMap
            in="frost"
            in2="map"
            :scale="channelScales[0]"
            xChannelSelector="R"
            yChannelSelector="G"
            result="d"
          />
          <feColorMatrix
            in="d"
            type="saturate"
            :values="saturation"
          />
        </template>
      </filter>
    </svg>
    <slot />
  </component>
</template>
