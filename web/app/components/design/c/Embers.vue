<script setup lang="ts">
/**
 * Design exploration, variant C — embers: rose-gold and champagne dust drifting
 * up through a scene, parting around the cursor, rising faster and dissolving as
 * the scene scrolls away (`progress`, 0..1).
 *
 * Canvas 2D, additive sprites, a few dozen particles. The loop parks when the
 * canvas is off-screen or the tab is hidden, and never starts in the `lite`
 * FX level or under reduced motion (the canvas then stays empty).
 */
const props = withDefaults(defineProps<{
  /** Particles on a wide screen; scaled down on narrow ones. */
  count?: number
  /** Scroll progress of the host scene, read every frame. */
  progress?: () => number
  /** Cursor parting. */
  interactive?: boolean
}>(), {
  count: 110,
  progress: () => 0,
  interactive: true,
})

const canvasRef = ref<HTMLCanvasElement | null>(null)
const fx = useFx()
let teardown: (() => void) | null = null

interface Particle {
  x: number
  y: number
  vx: number
  vy: number
  /** Resting upward speed (px per 60 Hz frame, negative). */
  rise: number
  size: number
  alpha: number
  twinkle: number
  phase: number
  sprite: number
  depth: number
  /** 0..1 fade-in after a respawn. */
  life: number
}

const MAX_DPR = 1.5
const PALETTE: [number, number, number][] = [
  [242, 207, 166], // champagne
  [229, 143, 131], // rosegold-400
  [217, 182, 118], // gold
  [246, 207, 199], // rosegold-200
]

function makeSprite(rgb: [number, number, number], soft: boolean): HTMLCanvasElement {
  const size = 64
  const sprite = document.createElement('canvas')
  sprite.width = size
  sprite.height = size
  const ctx = sprite.getContext('2d')!
  const g = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2)
  const [r, gr, b] = rgb
  if (soft) {
    g.addColorStop(0, `rgba(${r},${gr},${b},0.55)`)
    g.addColorStop(0.5, `rgba(${r},${gr},${b},0.22)`)
    g.addColorStop(1, `rgba(${r},${gr},${b},0)`)
  }
  else {
    g.addColorStop(0, 'rgba(255,248,240,1)')
    g.addColorStop(0.12, `rgba(${r},${gr},${b},0.95)`)
    g.addColorStop(0.35, `rgba(${r},${gr},${b},0.28)`)
    g.addColorStop(1, `rgba(${r},${gr},${b},0)`)
  }
  ctx.fillStyle = g
  ctx.fillRect(0, 0, size, size)
  return sprite
}

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return
  if (fx.value.level === 'lite' || fx.value.reducedMotion) return
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  const ctx = canvas.getContext('2d', { alpha: true })
  if (!ctx) return

  const sharp = PALETTE.map(c => makeSprite(c, false))
  const soft = PALETTE.map(c => makeSprite(c, true))

  let width = 0
  let height = 0
  let dpr = 1
  const particles: Particle[] = []

  function spawn(p: Partial<Particle>, anywhere: boolean): Particle {
    const depth = Math.random() ** 1.6
    const bokeh = Math.random() < 0.1
    return Object.assign(p, {
      x: Math.random() * width,
      y: anywhere ? Math.random() * height : height + 10 + Math.random() * 40,
      vx: 0,
      vy: 0,
      rise: -(0.12 + depth * 0.55 + (bokeh ? 0.1 : 0)),
      size: bokeh ? 5 + Math.random() * 9 : 0.5 + depth * 2.1,
      alpha: bokeh ? 0.08 + Math.random() * 0.1 : 0.35 + depth * 0.6,
      twinkle: 0.6 + Math.random() * 2.2,
      phase: Math.random() * Math.PI * 2,
      sprite: Math.floor(Math.random() * PALETTE.length),
      depth: bokeh ? 1.2 : depth,
      life: anywhere ? 1 : 0,
    }) as Particle
  }

  function populate() {
    const narrow = width < 640
    const target = Math.round(props.count * (narrow ? 0.4 : width < 1100 ? 0.75 : 1))
    while (particles.length < target) particles.push(spawn({}, true))
    particles.length = target
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR)
    width = canvas!.clientWidth
    height = canvas!.clientHeight
    canvas!.width = Math.max(1, Math.round(width * dpr))
    canvas!.height = Math.max(1, Math.round(height * dpr))
    for (const p of particles) {
      if (p.x > width) p.x = Math.random() * width
      if (p.y > height + 50) p.y = Math.random() * height
    }
    populate()
  }

  // Pointer in canvas space. The rect is read in the event handler, where the
  // layout is clean, never inside the frame.
  const pointer = { x: -9999, y: -9999, k: 0, lastMove: -Infinity }
  function onPointerMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    const rect = canvas!.getBoundingClientRect()
    pointer.x = event.clientX - rect.left
    pointer.y = event.clientY - rect.top
    pointer.lastMove = performance.now()
  }

  let raf = 0
  let last = 0
  let visible = false
  let time = 0

  function draw(now: number) {
    raf = requestAnimationFrame(draw)
    const dt = last ? Math.min(3, (now - last) / 16.667) : 1
    last = now
    time += dt

    const progress = Math.min(1, Math.max(0, props.progress()))
    const dissolve = 1 - progress * 0.92
    const lift = 1 + progress * 7
    const active = props.interactive && now - pointer.lastMove < 2500
    pointer.k += ((active ? 1 : 0) - pointer.k) * (active ? 0.12 : 0.03) * dt
    const radius = 170
    const radius2 = radius * radius
    const cx = width / 2

    ctx!.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx!.clearRect(0, 0, width, height)
    ctx!.globalCompositeOperation = 'lighter'

    for (const p of particles) {
      // Cursor parting: pushed out of a soft disc around the pointer, and
      // flaring a little while disturbed.
      let flare = 0
      if (pointer.k > 0.01) {
        const dx = p.x - pointer.x
        const dy = p.y - pointer.y
        const d2 = dx * dx + dy * dy
        if (d2 < radius2 && d2 > 0.01) {
          const d = Math.sqrt(d2)
          const f = (1 - d / radius) ** 2 * 1.9 * pointer.k
          p.vx += (dx / d) * f * dt
          p.vy += (dy / d) * f * dt
          flare = f * 0.6
        }
      }
      // Ease back to the resting drift.
      const damp = 0.9 ** dt
      p.vx *= damp
      p.vy = p.vy * damp + p.rise * lift * (1 - damp)
      const sway = Math.sin(time * 0.012 * p.twinkle + p.phase) * 0.22 * (0.3 + p.depth)
      // As the scene leaves, the dust spreads outward and climbs.
      const spread = (p.x - cx) * progress * 0.004
      p.x += (p.vx + sway + spread) * dt
      p.y += p.vy * dt
      if (p.life < 1) p.life = Math.min(1, p.life + 0.015 * dt)

      if (p.y < -30 || p.x < -40 || p.x > width + 40) spawn(p, false)

      const twinkle = 0.55 + 0.45 * Math.sin(time * 0.05 * p.twinkle + p.phase)
      // Fade near the top third so the dust dissolves rather than exits.
      const topFade = Math.min(1, Math.max(0, p.y / (height * 0.35)))
      const a = Math.min(1, p.alpha * twinkle * dissolve * p.life * (0.35 + 0.65 * topFade) + flare)
      if (a < 0.01) continue
      ctx!.globalAlpha = a
      const isBokeh = p.depth > 1
      const s = isBokeh ? p.size * 2.4 : p.size * 5.5
      ctx!.drawImage(isBokeh ? soft[p.sprite]! : sharp[p.sprite]!, p.x - s / 2, p.y - s / 2, s, s)
    }
    ctx!.globalAlpha = 1
  }

  function startLoop() {
    if (raf || !visible || document.hidden) return
    last = 0
    raf = requestAnimationFrame(draw)
  }
  function stopLoop() {
    cancelAnimationFrame(raf)
    raf = 0
  }

  const intersection = new IntersectionObserver(([entry]) => {
    visible = entry?.isIntersecting ?? false
    if (visible) startLoop()
    else stopLoop()
  })
  intersection.observe(canvas)
  const sizeObserver = new ResizeObserver(() => resize())
  sizeObserver.observe(canvas)
  const onVisibility = () => (document.hidden ? stopLoop() : startLoop())
  document.addEventListener('visibilitychange', onVisibility)
  if (props.interactive && fx.value.finePointer) window.addEventListener('pointermove', onPointerMove, { passive: true })

  resize()

  teardown = () => {
    stopLoop()
    intersection.disconnect()
    sizeObserver.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
    window.removeEventListener('pointermove', onPointerMove)
  }
})

onBeforeUnmount(() => teardown?.())
</script>

<template>
  <canvas
    ref="canvasRef"
    aria-hidden="true"
    class="c-embers"
  />
</template>
