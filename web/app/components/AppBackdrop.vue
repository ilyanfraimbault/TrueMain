<script setup lang="ts">
// App-wide animated backdrop. One fixed WebGL layer behind every page (the
// hero included) so the surface reads as a single sheet of slowly-billowing
// rose-gold silk: an iterative sine warp bends space into wide, voluptuous
// folds (deliberately few iterations + large amplitudes — curves, not noise),
// a champagne sheen catches the crest of each fold, and the troughs stay
// transparent so the dark surface shows through as the fabric's shadow.
//
// Degrades gracefully: no WebGL → the static CSS wash below; reduced-motion →
// a single rendered frame, no loop; tab hidden / off-screen → the loop parks.
// Rose-gold-only, transparent output, so it tints whatever sits behind on both
// colour modes.

const canvasRef = ref<HTMLCanvasElement | null>(null)

// Assigned by onMounted once the WebGL context + listeners exist; invoked by
// the setup-level onBeforeUnmount below. Null when WebGL never initialised
// (nothing to tear down).
let teardown: (() => void) | null = null

const VERTEX_SHADER = `
attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

const FRAGMENT_SHADER = `
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
uniform vec2 u_resolution;
uniform float u_time;

void main() {
  vec2 uv = gl_FragCoord.xy / u_resolution;
  // Centered, aspect-corrected space so the folds sweep in wide diagonals
  // instead of stretching with the viewport.
  vec2 p = (gl_FragCoord.xy * 2.0 - u_resolution) / min(u_resolution.x, u_resolution.y);
  float t = u_time * 0.05;

  // Iterative sine warp — each pass bends space along big slow curves. Few
  // iterations x large amplitude reads as draped silk; more/finer would
  // collapse back into noise.
  vec2 q = p;
  for (float i = 1.0; i <= 4.0; i += 1.0) {
    q.x += (0.65 / i) * sin(i * 1.4 * q.y + t + 0.4 * i);
    q.y += (0.55 / i) * cos(i * 1.2 * q.x + 1.3 * t + 0.7 * i);
  }

  // One smooth band field over the warped space: body is the lit face of a
  // fold, sheen the narrow champagne highlight riding its crest. Troughs
  // fall to zero alpha — the dark page shows through as the fabric's shadow.
  float band = 0.5 + 0.5 * sin(1.6 * q.x + 1.1 * q.y);
  float body = smoothstep(0.12, 0.92, band);
  float sheen = pow(band, 6.0);

  // Rose-gold ramp: shadowed mauve-rose folds -> lit rose silk -> champagne
  // sheen, so the fabric warms toward gold exactly where it catches light.
  vec3 deep = vec3(0.42, 0.20, 0.23);
  vec3 mid = vec3(0.85, 0.45, 0.42);
  vec3 gold = vec3(0.96, 0.81, 0.66);
  vec3 color = deep * body * 0.85
    + mid * pow(band, 2.5) * 0.6
    + gold * sheen * 0.5;

  // Top-weighted falloff keeps the hero draped and the reading surface calm.
  float vgrad = mix(0.45, 1.0, 1.0 - uv.y);
  float alpha = clamp((body * 0.26 + sheen * 0.32) * vgrad, 0.0, 0.6);
  gl_FragColor = vec4(color, alpha);
}
`

const FRAME_INTERVAL_MS = 1000 / 30
const MAX_DPR = 1.5
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
    premultipliedAlpha: false,
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

  const vertex = compile(gl.VERTEX_SHADER, VERTEX_SHADER)
  const fragment = compile(gl.FRAGMENT_SHADER, FRAGMENT_SHADER)
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
    gl!.uniform2f(uResolution, canvas!.width, canvas!.height)
    gl!.uniform1f(uTime, timeSeconds)
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

  function start() {
    if (rafId || reducedMotion.matches || document.hidden) return
    rafId = requestAnimationFrame(loop)
  }

  function stop() {
    if (!rafId) return
    cancelAnimationFrame(rafId)
    rafId = 0
  }

  function syncMotion() {
    if (reducedMotion.matches) {
      stop()
      draw(STATIC_FRAME_SECONDS)
    }
    else {
      start()
    }
  }

  const resizeObserver = new ResizeObserver(() => {
    // Only repaint when the loop is parked, and keep the timestamp consistent
    // with the loop (relative to the first frame) rather than raw page time.
    if (rafId) return
    draw(reducedMotion.matches
      ? STATIC_FRAME_SECONDS
      : startTime ? (performance.now() - startTime) / 1000 : 0)
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

  syncMotion()

  teardown = () => {
    stop()
    document.removeEventListener('visibilitychange', onVisibilityChange)
    canvas.removeEventListener('webglcontextlost', onContextLost)
    reducedMotion.removeEventListener('change', syncMotion)
    resizeObserver.disconnect()
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  }
})

onBeforeUnmount(() => teardown?.())
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-0 -z-10 overflow-hidden opacity-80 dark:opacity-100"
  >
    <!-- Static wash: the no-WebGL / pre-first-frame baseline only. Kept very
         faint and short so it doesn't bleed up through the shader's
         transparent troughs and wash out the shadow zones. -->
    <div
      class="absolute inset-0"
      style="background: linear-gradient(180deg, color-mix(in oklch, var(--ui-color-primary-500) 5%, transparent), transparent 35%);"
    />
    <canvas
      ref="canvasRef"
      class="absolute inset-0 size-full"
    />
  </div>
</template>
