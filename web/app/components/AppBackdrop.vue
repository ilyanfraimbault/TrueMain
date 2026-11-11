<script setup lang="ts">
// App-wide animated backdrop. One fixed WebGL layer behind every page: a
// rose-gold eclipse. A dark orb drifts slowly around the upper-middle of the
// viewport on two superposed oscillations — never still, never going
// anywhere. Its rim burns champagne-rose, a corona of fbm streams breathes
// around it, a whisper of limb light keeps the sphere reading as a body, and
// sparse stars twinkle behind (masked by the disc). A soft elliptical
// "reading shield" dims whatever crosses the hero text zone so the title
// keeps its contrast on any screen.
//
// Cursor-reactive: the corona surges on the side of the orb facing the
// mouse while it moves, and settles a few seconds after it rests.
//
// `variant="stars"`: the star field alone, at the hero's density on any height —
// the home page under its hero, so its cards' blur has something to show.
//
// Degrades gracefully: no WebGL → the static CSS wash below; reduced-motion or
// a very small viewport → a single rendered frame, no loop; tab hidden /
// off-screen → the loop parks.
// Output is premultiplied additive-over-transparent (colour scaled to its
// luminance-derived alpha) so only the light composites onto the page surface.

import { BACKDROP_FRAGMENT_SHADER, BACKDROP_VERTEX_SHADER } from '~/utils/backdrop-shaders'

const props = withDefaults(defineProps<{ variant?: 'eclipse' | 'stars' }>(), { variant: 'eclipse' })
const canvasRef = ref<HTMLCanvasElement | null>(null)

// Set by onMounted once WebGL is up; null when it never was (nothing to tear down).
let teardown: (() => void) | null = null

const FRAME_INTERVAL_MS = 1000 / 24
// Everything is soft gradients + grain; capped so 4K/retina doesn't
// quadruple the fill cost for detail that isn't there.
const MAX_DPR = 1.5
// Below this CSS-pixel width the continuous fill isn't worth its cost, so the
// loop stays parked on a single static frame (same path as reduced-motion).
const SMALL_VIEWPORT_MAX = 640
// Arbitrary mid-animation timestamp for the single frame drawn when motion is
// off (reduced-motion / pre-loop resize) — far enough in that the folds are
// developed rather than the near-empty t=0 field.
const STATIC_FRAME_SECONDS = 120

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return

  const gl = canvas.getContext('webgl', {
    alpha: true,
    antialias: false,
    depth: false,
    stencil: false,
    premultipliedAlpha: true,
    powerPreference: 'low-power',
  })
  if (!gl) return

  function compile(type: number, source: string): WebGLShader | null {
    const shader = gl!.createShader(type)
    if (!shader) return null
    gl!.shaderSource(shader, source)
    gl!.compileShader(shader)
    if (!gl!.getShaderParameter(shader, gl!.COMPILE_STATUS)) {
      if (import.meta.dev) console.warn('[AppBackdrop] shader compile failed:', gl!.getShaderInfoLog(shader))
      gl!.deleteShader(shader)
      return null
    }
    return shader
  }

  const vertex = compile(gl.VERTEX_SHADER, BACKDROP_VERTEX_SHADER)
  const fragment = compile(gl.FRAGMENT_SHADER, BACKDROP_FRAGMENT_SHADER)
  if (!vertex || !fragment) return

  const program = gl.createProgram()
  if (!program) return
  gl.attachShader(program, vertex)
  gl.attachShader(program, fragment)
  gl.linkProgram(program)
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    // Free everything allocated so far before bailing — symmetric with the
    // deleteShader() compile() does on its own failures.
    gl.deleteProgram(program)
    gl.deleteShader(vertex)
    gl.deleteShader(fragment)
    return
  }
  gl.useProgram(program)
  // The shaders are linked into the program now; free the standalone objects.
  gl.detachShader(program, vertex)
  gl.deleteShader(vertex)
  gl.detachShader(program, fragment)
  gl.deleteShader(fragment)

  const buffer = gl.createBuffer()
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer)
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW)
  const position = gl.getAttribLocation(program, 'a_position')
  gl.enableVertexAttribArray(position)
  gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0)

  const uResolution = gl.getUniformLocation(program, 'u_resolution')
  const uTime = gl.getUniformLocation(program, 'u_time')
  const uPointer = gl.getUniformLocation(program, 'u_pointer')
  const uPointerK = gl.getUniformLocation(program, 'u_pointerK')
  const uStarsOnly = gl.getUniformLocation(program, 'u_starsOnly')
  // The eclipse scales with its section; the stars keep a fixed 900 px height.
  const uRefH = gl.getUniformLocation(program, 'u_refH')

  // Cursor state, in viewport fractions (0..1, y up) to match the shader.
  // `pointer` trails `pointerTarget` and `pointerK` eases toward 1 while the
  // mouse is moving, back to 0 a few seconds after it rests — both smoothed
  // per frame in draw(), so the light glides instead of jumping.
  const pointerTarget = { x: 0.7, y: 0.62 }
  const pointer = { ...pointerTarget }
  // Last raw viewport coordinates seen, converted to canvas space once per
  // frame — see onPointerMove / syncPointerTarget below.
  const pointerClient = { x: 0, y: 0 }
  let pointerK = 0
  let lastPointerMoveAt = -Infinity

  // The handler only records raw viewport coordinates — no layout read:
  // `pointermove` fires at the poll rate and `getBoundingClientRect()` forces a
  // reflow, so the conversion runs once per frame instead. A cached rect would
  // be wrong, not just stale: it is viewport-relative and scrolling moves it.
  function onPointerMove(event: PointerEvent) {
    pointerClient.x = event.clientX
    pointerClient.y = event.clientY
    lastPointerMoveAt = performance.now()
  }

  // Viewport → canvas-normalised, once per frame from draw(): the backdrop is
  // bounded by its section, so viewport fractions would misplace the flare.
  // Clamped so a cursor outside parks it at the nearest edge.
  function syncPointerTarget() {
    if (lastPointerMoveAt === -Infinity) return
    const bounds = canvas!.getBoundingClientRect()
    if (bounds.width === 0 || bounds.height === 0) return
    const clamp01 = (value: number) => Math.min(1, Math.max(0, value))
    pointerTarget.x = clamp01((pointerClient.x - bounds.left) / bounds.width)
    pointerTarget.y = 1 - clamp01((pointerClient.y - bounds.top) / bounds.height)
  }

  function resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR)
    const width = Math.max(1, Math.round(canvas!.clientWidth * dpr))
    const height = Math.max(1, Math.round(canvas!.clientHeight * dpr))
    if (canvas!.width !== width || canvas!.height !== height) {
      canvas!.width = width
      canvas!.height = height
      gl!.viewport(0, 0, width, height)
    }
  }

  function draw(timeSeconds: number) {
    resize()
    syncPointerTarget()
    // Runs at ~24fps (loop is frame-gated). These per-frame factors act as
    // time constants; they're scaled by 30/24 from the previous 30fps loop so
    // the cursor response is unchanged in wall-clock time (~0.3s for the
    // position trail, ~1s in / ~2s out for the cursor's influence).
    pointer.x += (pointerTarget.x - pointer.x) * 0.15
    pointer.y += (pointerTarget.y - pointer.y) * 0.15
    const pointerActive = performance.now() - lastPointerMoveAt < 3000
    pointerK += ((pointerActive ? 1 : 0) - pointerK) * (pointerActive ? 0.125 : 0.025)
    gl!.uniform2f(uResolution, canvas!.width, canvas!.height)
    gl!.uniform1f(uTime, timeSeconds)
    gl!.uniform2f(uPointer, pointer.x, pointer.y)
    gl!.uniform1f(uPointerK, pointerK)
    gl!.uniform1f(uStarsOnly, props.variant === 'stars' ? 1 : 0)
    gl!.uniform1f(uRefH, props.variant === 'stars' ? 900 * Math.min(window.devicePixelRatio || 1, MAX_DPR) : canvas!.height)
    gl!.clearColor(0, 0, 0, 0)
    gl!.clear(gl!.COLOR_BUFFER_BIT)
    gl!.drawArrays(gl!.TRIANGLES, 0, 3)
  }

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
  let rafId = 0
  let lastFrameAt = 0
  // Time fed to the shader is measured from the first frame, not the page's
  // navigation start, so `u_time` stays small — `mediump` builds lose enough
  // precision around ~4h of `performance.now()` to freeze the animation.
  let startTime = 0

  function loop(now: number) {
    rafId = requestAnimationFrame(loop)
    if (now - lastFrameAt < FRAME_INTERVAL_MS) return
    lastFrameAt = now
    if (!startTime) startTime = now
    draw((now - startTime) / 1000)
  }

  // The loop should stay parked (a single static frame instead of a live
  // animation) whenever the user asked for reduced motion or the viewport is
  // too small to be worth the continuous fill cost. `document.hidden` is a
  // separate, transient park handled by start()/onVisibilityChange.
  function motionOff() {
    return reducedMotion.matches || window.innerWidth < SMALL_VIEWPORT_MAX
  }

  function start() {
    if (rafId || motionOff() || document.hidden) return
    rafId = requestAnimationFrame(loop)
  }

  function stop() {
    if (!rafId) return
    cancelAnimationFrame(rafId)
    rafId = 0
  }

  function syncMotion() {
    if (motionOff()) {
      stop()
      draw(STATIC_FRAME_SECONDS)
    }
    else {
      start()
    }
  }

  const resizeObserver = new ResizeObserver(() => {
    if (rafId) return
    // A resize can cross the small-viewport threshold, so re-evaluate whether
    // the loop should now run (e.g. a phone rotated into landscape). When it
    // stays parked, repaint one frame — a developed static frame if motion is
    // off, otherwise timed relative to the first frame (matching the loop),
    // never raw page time.
    if (motionOff()) {
      draw(STATIC_FRAME_SECONDS)
    }
    else if (document.hidden) {
      draw(startTime ? (performance.now() - startTime) / 1000 : 0)
    }
    else {
      start()
    }
  })
  resizeObserver.observe(canvas)

  function onVisibilityChange() {
    if (document.hidden) stop()
    else start()
  }
  // If the GPU reclaims the context, the loop would keep firing no-op GL
  // calls — park it.
  function onContextLost(event: Event) {
    event.preventDefault()
    stop()
  }
  document.addEventListener('visibilitychange', onVisibilityChange)
  canvas.addEventListener('webglcontextlost', onContextLost)
  reducedMotion.addEventListener('change', syncMotion)
  window.addEventListener('pointermove', onPointerMove, { passive: true })

  syncMotion()

  teardown = () => {
    stop()
    document.removeEventListener('visibilitychange', onVisibilityChange)
    canvas.removeEventListener('webglcontextlost', onContextLost)
    reducedMotion.removeEventListener('change', syncMotion)
    window.removeEventListener('pointermove', onPointerMove)
    resizeObserver.disconnect()
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  }
})

onBeforeUnmount(() => teardown?.())
</script>

<template>
  <!-- `absolute`, not `fixed`: bounded by the home section that mounts it. As a
       viewport-fixed layer behind every route the corona passed through the
       data tables and rows changed luminance down their length, which cannot
       be scanned. The host element owns the bounds and `overflow-hidden`. -->
  <div
    aria-hidden="true"
    class="pointer-events-none absolute inset-0 -z-10 overflow-hidden"
  >
    <!-- Static wash: the no-WebGL / pre-first-frame baseline only, faint and
         short so it never bleeds through the shader's transparent troughs. -->
    <div
      v-if="variant === 'eclipse'"
      class="absolute inset-0"
      style="background: linear-gradient(180deg, color-mix(in oklch, var(--ui-color-primary-500) 5%, transparent), transparent 35%);"
    />
    <canvas
      ref="canvasRef"
      class="absolute inset-0 size-full"
    />
  </div>
</template>
