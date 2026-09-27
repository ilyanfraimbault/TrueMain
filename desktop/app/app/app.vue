<script setup lang="ts">
const { screen, ready } = useLcuState()
const router = useRouter()

useHead({ title: 'TrueMain' })

// The scenario picker exists only for `npm run dev` in a browser: inside Tauri
// the state comes from the client, and in a production build `import.meta.dev`
// is false so the component and its fixtures are dropped from the bundle.
const showScenarioPicker = computed(() => import.meta.dev && !insideTauri())

/**
 * The gameflow phase still drives the screen (`AppState::screen()` in Rust is
 * the rule): entering champion select opens the draft on its own, and leaving
 * it takes the player back home — but only from the draft, so a page they
 * navigated to by hand is never pulled away from under them.
 */
watch(screen, (next, previous) => {
  const onDraft = router.currentRoute.value.path === '/draft'
  if (next === 'draft' && !onDraft) void router.push('/draft')
  else if (previous === 'draft' && next !== 'draft' && onDraft) void router.push('/')
})
</script>

<template>
  <UApp>
    <div class="flex h-screen overflow-hidden bg-default text-default">
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
