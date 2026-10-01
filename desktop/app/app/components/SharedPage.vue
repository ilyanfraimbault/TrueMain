<script setup lang="ts">
/**
 * The frame of a page shared with the site (#1732): the app's scroll
 * container, and a `<Suspense>` of the app's own (#1788).
 *
 * The shared pages await their first read in setup. On the site that keeps
 * the outgoing page under the header's loading bar (#1689); under the app's
 * `<NuxtPage>` the same await held the previous tab on screen for as long as
 * TrueMain took to answer through the shell, with nothing saying the click was
 * taken. Caught here, it no longer holds the route: the tab changes on click,
 * the page's own header stands over a skeleton, and the window's loading bar
 * runs until the page is in.
 */
defineProps<{
  /** The shared page's `PageHeader`, drawn while it loads. */
  eyebrow: string
  title: string
}>()

const suspended = loadSpan()
onBeforeUnmount(() => suspended.end())
</script>

<template>
  <div class="h-full overflow-y-auto">
    <Suspense
      @pending="suspended.begin()"
      @resolve="suspended.end()"
    >
      <slot />

      <template #fallback>
        <div
          aria-busy="true"
          class="mx-auto max-w-6xl space-y-6 p-4 md:p-6"
        >
          <PageHeader
            :eyebrow="eyebrow"
            :title="title"
          >
            <USkeleton class="h-8 w-full max-w-md" />
          </PageHeader>
          <div class="space-y-2">
            <USkeleton
              v-for="row in 8"
              :key="row"
              class="h-12 w-full"
            />
          </div>
        </div>
      </template>
    </Suspense>
  </div>
</template>
