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

/** Whether `target` is on screen right now (both ways, unlike `useVisibleOnce`). */
export function useInView(target: Ref<HTMLElement | null | undefined>, threshold = 0.35): Ref<boolean> {
  const inView = ref(false)
  onMounted(() => {
    const el = target.value
    if (!el || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver(([entry]) => (inView.value = Boolean(entry?.isIntersecting)), { threshold })
    observer.observe(el)
    onBeforeUnmount(() => observer.disconnect())
  })
  return inView
}

/**
 * A scene's timeline: `beats[i]` is how long beat `i` holds, in ms. Returns the
 * current beat — the last one (the final state) until the scene may play, then
 * from 0 whenever it comes on screen, looping while it stays there. Leaving the
 * screen stops the clock and rewinds, so the scene starts over when it is back.
 */
export function useSceneBeats(target: Ref<HTMLElement | null | undefined>, beats: readonly number[], options: { loop?: boolean, threshold?: number } = {}) {
  const reduced = useReducedMotion()
  const inView = useInView(target, options.threshold)
  const mounted = ref(false)
  const beat = ref(beats.length - 1)
  let timer: ReturnType<typeof setTimeout> | undefined

  const playing = computed(() => mounted.value && !reduced.value)

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

  onMounted(() => (mounted.value = true))
  onBeforeUnmount(() => clearTimeout(timer))

  watch([playing, inView], ([canPlay, visible]) => {
    clearTimeout(timer)
    if (!canPlay) {
      beat.value = beats.length - 1
      return
    }
    beat.value = 0
    if (visible) schedule()
  })

  /** Whether the scene has reached beat `index` (true for every beat when it does not play). */
  const reached = (index: number) => beat.value >= index

  return { beat, reached, playing }
}
