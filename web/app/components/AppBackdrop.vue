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
// Degrades gracefully: no WebGL → the static CSS wash below; reduced-motion →
// a single rendered frame, no loop; tab hidden / off-screen → the loop parks.
// Output is additive-over-transparent (colour normalised by luminance, alpha
// = luminance) so only the light composites onto the page surface.

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
uniform vec2 u_pointer;   // smoothed cursor, viewport fractions (0..1, y up)
uniform float u_pointerK; // 0 = ambient drift, 1 = cursor-driven

float hash2(vec2 p) {
  return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float vnoise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  float a = hash2(i), b = hash2(i + vec2(1.0, 0.0));
  float c = hash2(i + vec2(0.0, 1.0)), d = hash2(i + vec2(1.0, 1.0));
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 5; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(7.3, 3.1); a *= 0.5; }
  return v;
}

// Viewport fractions -> the same height-normalised space as p, so anchors
// keep their on-screen position on any aspect ratio. Normalising by height
// (not min(w,h)) is deliberate: the hero text is vertically centred and
// height-driven, so tying the orb's size and vertical position to height too
// keeps them locked together — resizing width no longer shrinks or shifts
// the eclipse relative to the text. In landscape this equals min(w,h), so
// the common case is unchanged.
vec2 anchor(vec2 uv) {
  return (uv * 2.0 - 1.0) * u_resolution / u_resolution.y;
}

vec2 starPos(vec2 g) {
  return g + 0.2 + 0.6 * vec2(hash2(g), hash2(g + 17.3));
}

void main() {
  vec2 p = (gl_FragCoord.xy * 2.0 - u_resolution) / u_resolution.y;
  float t = u_time;

  const vec3 WINE = vec3(0.30, 0.09, 0.15);
  const vec3 ROSE = vec3(0.80, 0.36, 0.32);
  const vec3 CHAMPAGNE = vec3(0.95, 0.70, 0.48);

  // The orb drifts on two superposed slow oscillations — never still, never
  // going anywhere. The reading shield below stays put, so the text zone is
  // protected wherever the rim wanders.
  vec2 c = anchor(vec2(
    0.5 + 0.020 * sin(t * 0.19) + 0.006 * sin(t * 0.53 + 1.3),
    0.66 + 0.024 * sin(t * 0.14 + 2.1) + 0.006 * sin(t * 0.47)));
  vec2 d2c = p - c;
  float d = length(d2c);
  float R = 0.38;
  vec2 dirN = d2c / max(d, 1e-4);

  // Rim + corona (fbm sampled on the unit circle so there is no angle seam).
  float rim = exp(-abs(d - R) * 30.0);
  float outer = max(d - R, 0.0);
  float streams = pow(max(fbm(dirN * 1.9 + outer * 2.0 + vec2(0.0, t * 0.04)) - 0.28, 0.0), 1.8);
  float cor = exp(-outer * 3.4) * streams * step(R, d);

  // Cursor flare: the corona surges on the side of the orb facing the mouse.
  vec2 cpDir = anchor(u_pointer) - c;
  cpDir /= max(length(cpDir), 1e-4);
  float facing = pow(max(dot(dirN, cpDir), 0.0), 5.0);
  cor *= 0.6 + 1.5 * u_pointerK * facing;
  rim *= 0.75 + 0.8 * u_pointerK * facing;

  float inside = smoothstep(R, R - 0.025, d);
  vec3 col = (ROSE * 0.7 + CHAMPAGNE * 0.5) * rim * (1.0 - inside * 0.6);
  col += (WINE * 0.7 + ROSE * 0.55) * cor * 1.5;

  // Natural aura: a wide, very soft atmosphere breathing out from the disc,
  // slower and calmer than the corona streams. Outward only — the disc's
  // interior is the hero's reading surface and must stay dark.
  float breathe = 0.85 + 0.15 * sin(t * 0.25);
  col += (WINE * 0.75 + ROSE * 0.3) * exp(-outer * 5.5) * step(R, d) * 0.2 * breathe;
  // Earthshine: the sphere's limb catches a whisper of that light just
  // inside the rim — hugging the edge so it never veils the text.
  col += (WINE * 0.6 + ROSE * 0.3) * exp(-(R - d) * 18.0) * inside * 0.3 * breathe;

  // Sparse stars, hidden behind the disc.
  vec2 q = p * 4.0 + 11.0;
  vec2 g = floor(q);
  float st = 0.0;
  for (int dy = -1; dy <= 1; dy++) {
    for (int dx = -1; dx <= 1; dx++) {
      vec2 gg = g + vec2(float(dx), float(dy));
      float h = hash2(gg + 5.1);
      float tw = 0.5 + 0.5 * sin(t * (0.5 + h) + h * 40.0);
      st += exp(-dot(q - starPos(gg), q - starPos(gg)) * 1000.0) * tw * step(0.55, h);
    }
  }
  col += mix(ROSE, CHAMPAGNE, 0.6) * st * 0.5 * (1.0 - inside);

  // Reading shield: gently dim whatever crosses the hero text zone, so the
  // title keeps its contrast whichever way the rim lands on this screen.
  vec2 hz = (p - anchor(vec2(0.5, 0.55))) / vec2(0.72, 0.34);
  col *= 1.0 - 0.6 * (1.0 - smoothstep(0.75, 1.3, length(hz)));

  // Fine animated grain keeps the soft gradients from banding.
  col += (hash2(gl_FragCoord.xy + fract(t) * 61.0) - 0.5) * 0.02;
  col = max(col, 0.0);

  // Additive-over-transparent: normalise colour by luminance and carry the
  // luminance as alpha, so compositing over the page reproduces col + bg.
  float lum = max(col.r, max(col.g, col.b));
  float alpha = clamp(lum, 0.0, 0.95);
  gl_FragColor = vec4(col / max(lum, 1e-4), alpha);
}
`

const FRAME_INTERVAL_MS = 1000 / 30
// Everything is soft gradients + grain; capped so 4K/retina doesn't
// quadruple the fill cost for detail that isn't there.
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
  const uPointer = gl.getUniformLocation(program, 'u_pointer')
  const uPointerK = gl.getUniformLocation(program, 'u_pointerK')

  // Cursor state, in viewport fractions (0..1, y up) to match the shader.
  // `pointer` trails `pointerTarget` and `pointerK` eases toward 1 while the
  // mouse is moving, back to 0 a few seconds after it rests — both smoothed
  // per frame in draw(), so the light glides instead of jumping.
  const pointerTarget = { x: 0.7, y: 0.62 }
  const pointer = { ...pointerTarget }
  let pointerK = 0
  let lastPointerMoveAt = -Infinity

  function onPointerMove(event: PointerEvent) {
    pointerTarget.x = event.clientX / window.innerWidth
    pointerTarget.y = 1 - event.clientY / window.innerHeight
    lastPointerMoveAt = performance.now()
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
    // Runs at ~30fps (loop is frame-gated), so these fixed factors act as
    // time constants: ~0.3s for the position trail, ~1s in / ~2s out for the
    // cursor's influence.
    pointer.x += (pointerTarget.x - pointer.x) * 0.12
    pointer.y += (pointerTarget.y - pointer.y) * 0.12
    const pointerActive = performance.now() - lastPointerMoveAt < 3000
    pointerK += ((pointerActive ? 1 : 0) - pointerK) * (pointerActive ? 0.1 : 0.02)
    gl!.uniform2f(uResolution, canvas!.width, canvas!.height)
    gl!.uniform1f(uTime, timeSeconds)
    gl!.uniform2f(uPointer, pointer.x, pointer.y)
    gl!.uniform1f(uPointerK, pointerK)
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
