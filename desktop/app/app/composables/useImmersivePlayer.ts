/** How long the mouse rests before the full-screen controls fade out. */
const IDLE_MS = 2_500

/**
 * The recap player's full screen: the shell's window goes full screen (an
 * element's own full screen only fills the webview, not the screen) and the
 * controls float over the video, shown while the mouse moves or the video is
 * paused. Outside the shell (`npm run dev`), the element's own full screen
 * stands in.
 */
export function useImmersivePlayer(root: Ref<HTMLElement | null>, playing: Ref<boolean>) {
  const immersive = ref(false)
  const idle = ref(false)
  const hoveringControls = ref(false)
  const controlsHidden = computed(() => immersive.value && idle.value && playing.value && !hoveringControls.value)
  let idleTimer: ReturnType<typeof setTimeout> | undefined
  let stopWatchingWindow: (() => void) | undefined

  function wake() {
    idle.value = false
    clearTimeout(idleTimer)
    idleTimer = setTimeout(() => { idle.value = true }, IDLE_MS)
  }

  async function setWindowFullscreen(on: boolean) {
    if (insideTauri()) {
      const { getCurrentWindow } = await import('@tauri-apps/api/window')
      await getCurrentWindow().setFullscreen(on)
    }
    else if (on) await root.value?.requestFullscreen().catch(() => {})
    else if (document.fullscreenElement) await document.exitFullscreen()
  }

  async function enterFullscreen() {
    immersive.value = true
    wake()
    await setWindowFullscreen(true)
    if (insideTauri() && !stopWatchingWindow) {
      // Leaving through the OS (the green button, a Space swipe) drops the immersive view too.
      const { getCurrentWindow } = await import('@tauri-apps/api/window')
      const appWindow = getCurrentWindow()
      stopWatchingWindow = await appWindow.onResized(async () => {
        if (immersive.value && !(await appWindow.isFullscreen())) immersive.value = false
      })
    }
  }

  async function exitFullscreen() {
    immersive.value = false
    clearTimeout(idleTimer)
    await setWindowFullscreen(false)
  }

  const fullscreen = () => (immersive.value ? exitFullscreen() : enterFullscreen())

  function onDocumentFullscreen() {
    if (!insideTauri() && !document.fullscreenElement) immersive.value = false
  }
  onMounted(() => document.addEventListener('fullscreenchange', onDocumentFullscreen))
  onBeforeUnmount(() => {
    document.removeEventListener('fullscreenchange', onDocumentFullscreen)
    clearTimeout(idleTimer)
    stopWatchingWindow?.()
    if (immersive.value) void setWindowFullscreen(false)
  })

  return { immersive, hoveringControls, controlsHidden, wake, exitFullscreen, fullscreen }
}
