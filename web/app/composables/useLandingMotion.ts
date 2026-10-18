import type { Ref } from 'vue'

/**
 * The motion primitives of `/download`'s drawn app scenes (#2072). Every scene is
 * server-rendered in its *final* state — what a visitor without scripts, or with
 * `prefers-reduced-motion: reduce`, sees — and only once mounted, in a browser that
 * allows motion, rewinds to its first beat and plays while it is on screen.
 */

/** Whether the visitor asked for less motion; false on the server and until mounted. */
export function useReducedMotion(): Ref<boolean> {
  const reduced = ref(false)
  onMounted(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    reduced.value = query.matches
    const update = (event: MediaQueryListEvent) => (reduced.value = event.matches)
    query.addEventListener('change', update)
    onBeforeUnmount(() => query.removeEventListener('change', update))
  })
  return reduced
}

/**
 * Whether `target` is within `rootMargin` of the viewport right now (both ways,
 * unlike `useVisibleOnce`), and whether that can be known at all: `observed`
 * stays false without an element or an `IntersectionObserver`.
 */
export function useInView(target: Ref<HTMLElement | null | undefined>, rootMargin = '0px') {
  const inView = ref(false)
  const observed = ref(false)
  onMounted(() => {
    const el = target.value
    if (!el || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver(([entry]) => (inView.value = Boolean(entry?.isIntersecting)), { rootMargin })
    observer.observe(el)
    observed.value = true
    onBeforeUnmount(() => observer.disconnect())
  })
  return { inView, observed }
}

/** The band of the viewport a scene must reach to start: its middle, where the eye is once the header is read. */
const STAGE = '-30% 0px -30% 0px'

/**
 * A scene's timeline: `beats[i]` is how long beat `i` holds, in ms. Returns the
 * current beat — the last one (the final state) until the scene may play (mounted,
 * motion allowed, an `IntersectionObserver` to say where it is), then from 0. It
 * starts once the scene reaches the middle of the viewport — not as its top edge
 * peeks in, while the visitor still reads the header above — and loops from there.
 * Only leaving the screen entirely stops the clock and rewinds, so a small scroll
 * does not restart it, and it plays again from the top when it is back.
 */
export function useSceneBeats(target: Ref<HTMLElement | null | undefined>, beats: readonly number[], options: { loop?: boolean } = {}) {
  const reduced = useReducedMotion()
  const { inView: staged, observed } = useInView(target, STAGE)
  const { inView: onScreen } = useInView(target)
  const mounted = ref(false)
  const beat = ref(beats.length - 1)
  let timer: ReturnType<typeof setTimeout> | undefined
  let running = false

  // A scene that cannot tell whether it is on screen never rewinds: it would sit on its empty first beat.
  const playing = computed(() => mounted.value && observed.value && !reduced.value)

  function schedule() {
    clearTimeout(timer)
    timer = setTimeout(() => {
      const next = beat.value + 1
      if (next < beats.length) beat.value = next
      else if (options.loop !== false) beat.value = 0
      else return
      schedule()
    }, beats[beat.value])
  }

  function rewind() {
    clearTimeout(timer)
    running = false
    beat.value = 0
  }

  function start() {
    if (!staged.value || !playing.value || running) return
    running = true
    schedule()
  }

  onMounted(() => (mounted.value = true))
  onBeforeUnmount(() => clearTimeout(timer))

  // A scene already in the middle of the screen when it may first play starts at once.
  watch(playing, (canPlay) => {
    if (canPlay) {
      rewind()
      return start()
    }
    clearTimeout(timer)
    running = false
    beat.value = beats.length - 1
  })

  watch(staged, start)

  watch(onScreen, (visible) => {
    if (!visible && playing.value) rewind()
  })

  /** Whether the scene has reached beat `index` (true for every beat when it does not play). */
  const reached = (index: number) => beat.value >= index

  return { beat, reached, playing }
}
