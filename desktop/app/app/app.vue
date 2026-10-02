<script setup lang="ts">
const { screen, ready } = useLcuState()
const router = useRouter()
const route = useRoute()

/**
 * The overlay's panel loads this same bundle on `/overlay` in its own webview
 * (`src-tauri/src/overlay`): the page alone, with none of the window's work —
 * no recordings, no update offer, and above all no phase navigation, which
 * would carry the overlay off its page.
 */
const overlay = route.path === '/overlay'

// Followed for as long as the window lives, so a game's updates are never
// missed while another page is open.
useLiveGame()
if (!overlay) {
  // Recordings are read once and followed for the window's life too: the
  // sidebar and the dashboard's "Watch" read the library, and the end of a
  // recorded game opens its recap (`recording://recap`, from home or the game
  // page only).
  useRecordings()
}

/** The `/dev/*` tools (the draft simulator) stand alone, outside the app's shell and its phase navigation. */
const devTool = computed(() => route.path.startsWith('/dev/'))
const standalone = computed(() => overlay || devTool.value)

useHead({ title: 'TrueMain' })

// A newer beta, offered once per launch (`useAppUpdate`).
const { check: checkForUpdate } = useAppUpdate()
onMounted(() => {
  if (!overlay) void checkForUpdate()
})

// The scenario picker exists only for `npm run dev` in a browser: inside Tauri
// the state comes from the client, and in a production build `import.meta.dev`
// is false so the component and its fixtures are dropped from the bundle.
const showScenarioPicker = computed(() => import.meta.dev && !insideTauri() && !standalone.value)

/**
 * The gameflow phase still drives the screen (`AppState::screen()` in Rust is
 * the rule): entering champion select opens the draft on its own, and leaving
 * it takes the player back home — but only from the draft, so a page they
 * navigated to by hand is never pulled away from under them.
 *
 * The game opens its page the same way (#1748), from home or from the draft
 * the game started out of — never from a page the player opened by hand —
 * and its end takes the player home only from the game page.
 */
const PHASE_PAGES = { 'draft': '/draft', 'in-game': '/game' } as const

watch(screen, (next, previous) => {
  if (standalone.value) return
  const path = router.currentRoute.value.path
  if (next === 'draft') {
    if (path !== PHASE_PAGES.draft) void router.push(PHASE_PAGES.draft)
  }
  else if (next === 'in-game') {
    if (path === '/' || path === PHASE_PAGES.draft) void router.push(PHASE_PAGES['in-game'])
  }
  else if ((previous === 'draft' || previous === 'in-game') && path === PHASE_PAGES[previous]) {
    void router.push('/')
  }
})
</script>

<template>
  <UApp>
    <NuxtPage v-if="standalone" />
    <div v-else class="flex h-screen overflow-hidden bg-default text-default">
      <AppSidebar class="w-[200px] shrink-0" />

      <div class="flex min-w-0 flex-1 flex-col">
        <AppTopbar class="h-12 shrink-0" />
        <main class="relative min-h-0 flex-1 overflow-hidden rounded-tl-2xl border-l border-t border-default bg-muted">
          <!--
            `ready` covers the gap between mount and the first read of the Rust
            state. Rendering a page during it would tell the player their client
            is closed when it is not.
          -->
          <div v-if="!ready" class="flex h-full items-center justify-center">
            <UIcon name="i-lucide-loader-circle" class="size-6 text-dimmed" />
          </div>
          <NuxtPage v-else />
        </main>
      </div>

      <DevScenarioPicker v-if="showScenarioPicker" />
    </div>
  </UApp>
</template>
