<script setup lang="ts">
// Animated emerald "aurora" backdrop for the homepage hero. A tiny WebGL
// fragment shader draws domain-warped noise folds that drift slowly behind
// the headline; everything degrades gracefully:
//   - no WebGL            → the static CSS glow below is all that shows
//   - prefers-reduced-motion → a single shader frame is rendered, no loop
//   - tab hidden / hero scrolled away → the rAF loop pauses
// The canvas is transparent (alpha-only output) so the same component works
// on both the dark and light surfaces — the glow tints whatever sits behind.

const canvasRef = ref<HTMLCanvasElement | null>(null)

const VERTEX_SHADER = `
attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

// Three emerald stops (Tailwind emerald-900 / 500 / 400) layered by glow
// intensity. `band` concentrates the folds toward the top of the hero and
// fades them to zero before the canvas bottom edge, so the effect blends
// into the page background without a hard seam.
const FRAGMENT_SHADER = `
precision mediump float;
uniform vec2 u_resolution;
uniform float u_time;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
    mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x),
    u.y);
}

float fbm(vec2 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 4; i++) {
    v += a * noise(p);
    p = p * 2.0 + vec2(17.0, 9.0);
    a *= 0.5;
  }
  return v;
}

void main() {
  vec2 uv = gl_FragCoord.xy / u_resolution;
  vec2 p = uv;
  p.x *= u_resolution.x / u_resolution.y;
  float t = u_time * 0.04;

  vec2 q = vec2(
    fbm(p * 1.4 + vec2(0.0, t)),
    fbm(p * 1.4 + vec2(5.2, -t * 0.8)));
  float field = fbm(p * vec2(1.6, 2.4) + q * 1.6 + vec2(t * 0.5, -t * 0.3));

  float band = smoothstep(0.12, 0.85, uv.y);
  float glow = smoothstep(0.35, 0.9, field) * band;
  float spot = 1.0 - smoothstep(0.0, 0.9, distance(uv, vec2(0.5, 0.95)));

  vec3 deep = vec3(0.024, 0.306, 0.231);
  vec3 mid = vec3(0.063, 0.725, 0.506);
  vec3 bright = vec3(0.204, 0.827, 0.600);

  vec3 color = deep * (glow * 0.9 + spot * 0.35)
    + mid * pow(glow, 2.0) * 0.55
    + bright * pow(glow, 4.0) * 0.35;

  float alpha = clamp(glow * 0.55 + spot * 0.22, 0.0, 0.8);
  gl_FragColor = vec4(color, alpha);
}
`

// 30fps is plenty for drift this slow and halves the GPU wakeups of a
// vanilla rAF loop.
const FRAME_INTERVAL_MS = 1000 / 30
// IPX-style DPR cap: retina sharpness is wasted on a blurry glow field.
const MAX_DPR = 1.5

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
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return
  gl.useProgram(program)

  // One fullscreen triangle — fewer vertices than a quad, no index buffer.
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
  let visible = true

  function loop(now: number) {
    rafId = requestAnimationFrame(loop)
    if (now - lastFrameAt < FRAME_INTERVAL_MS) return
    lastFrameAt = now
    draw(now / 1000)
  }

  function start() {
    if (rafId || reducedMotion.matches || document.hidden || !visible) return
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
      // A fixed mid-animation timestamp picks a frame with developed folds —
      // t=0 renders an almost-empty field.
      draw(120)
    }
    else {
      start()
    }
  }

  const resizeObserver = new ResizeObserver(() => {
    // Repaint immediately on resize when the loop is parked, so a
    // reduced-motion static frame doesn't stretch.
    if (!rafId) draw(reducedMotion.matches ? 120 : performance.now() / 1000)
  })
  resizeObserver.observe(canvas)

  const intersectionObserver = new IntersectionObserver(([entry]) => {
    visible = entry?.isIntersecting ?? true
    if (visible) start()
    else stop()
  })
  intersectionObserver.observe(canvas)

  function onVisibilityChange() {
    if (document.hidden) stop()
    else start()
  }
  document.addEventListener('visibilitychange', onVisibilityChange)
  reducedMotion.addEventListener('change', syncMotion)

  syncMotion()

  onBeforeUnmount(() => {
    stop()
    document.removeEventListener('visibilitychange', onVisibilityChange)
    reducedMotion.removeEventListener('change', syncMotion)
    resizeObserver.disconnect()
    intersectionObserver.disconnect()
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  })
})
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none absolute inset-0 overflow-hidden opacity-70 dark:opacity-100"
  >
    <!-- Static glow: the no-WebGL / pre-first-frame baseline, same hue family
         as the shader so the canvas fading in over it reads as one effect. -->
    <div
      class="absolute inset-x-0 top-0 h-[80%]"
      style="background: radial-gradient(ellipse 80% 60% at 50% 0%, color-mix(in oklch, var(--ui-color-primary-500) 16%, transparent), transparent 65%);"
    />

    <!-- Faint dot grid, masked so it only textures the upper hero. -->
    <div
      class="absolute inset-0 [background-image:radial-gradient(color-mix(in_oklch,var(--ui-color-primary-400)_14%,transparent)_1px,transparent_1px)] [background-size:26px_26px] [mask-image:radial-gradient(ellipse_70%_60%_at_50%_0%,black,transparent_75%)]"
    />

    <canvas
      ref="canvasRef"
      class="absolute inset-0 size-full"
    />
  </div>
</template>
