<script setup lang="ts">
// App-wide animated backdrop. One fixed WebGL layer behind every page (the
// hero included) so the surface is a single, uniform, slowly-drifting emerald
// haze rather than a patchwork of CSS radial gradients. Organic (domain-warped
// fbm noise) so it reads as a shader, not a flat colour ramp, with a gentle
// top-weighted falloff for a little depth.
//
// Degrades gracefully: no WebGL → the static CSS wash below; reduced-motion →
// a single rendered frame, no loop; tab hidden / off-screen → the loop parks.
// Emerald-only, transparent output, so it tints whatever sits behind on both
// colour modes.

const canvasRef = ref<HTMLCanvasElement | null>(null)

const VERTEX_SHADER = `
attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

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
  for (int i = 0; i < 5; i++) {
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
  float t = u_time * 0.025;

  // Domain-warp the noise so the folds curl and drift instead of sliding.
  vec2 q = vec2(
    fbm(p * 1.1 + vec2(0.0, t)),
    fbm(p * 1.1 + vec2(4.0, -t * 0.8)));
  float field = fbm(p * 1.5 + q * 1.3 + vec2(t * 0.4, -t * 0.25));

  // Gentle top-weighted gradient — present everywhere, a touch stronger up top.
  float vgrad = mix(0.55, 1.0, 1.0 - uv.y);
  float glow = smoothstep(0.22, 0.9, field);

  vec3 deep = vec3(0.024, 0.306, 0.231);
  vec3 mid = vec3(0.063, 0.725, 0.506);
  vec3 color = deep * 0.65 + mid * pow(glow, 2.2) * 0.5;

  // Keep it subtle; the small base term ensures even the dark troughs carry a
  // faint tint so the field reads as uniform rather than blotchy.
  float alpha = clamp((glow * 0.30 + 0.05) * vgrad, 0.0, 0.42);
  gl_FragColor = vec4(color, alpha);
}
`

const FRAME_INTERVAL_MS = 1000 / 30
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

  function loop(now: number) {
    rafId = requestAnimationFrame(loop)
    if (now - lastFrameAt < FRAME_INTERVAL_MS) return
    lastFrameAt = now
    draw(now / 1000)
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
      draw(120)
    }
    else {
      start()
    }
  }

  const resizeObserver = new ResizeObserver(() => {
    if (!rafId) draw(reducedMotion.matches ? 120 : performance.now() / 1000)
  })
  resizeObserver.observe(canvas)

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
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  })
})
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-0 -z-10 overflow-hidden opacity-80 dark:opacity-100"
  >
    <!-- Static wash: the no-WebGL / pre-first-frame baseline, same emerald
         family as the shader so the canvas fading in over it reads as one. -->
    <div
      class="absolute inset-0"
      style="background: linear-gradient(180deg, color-mix(in oklch, var(--ui-color-primary-500) 9%, transparent), transparent 55%);"
    />
    <canvas
      ref="canvasRef"
      class="absolute inset-0 size-full"
    />
  </div>
</template>
