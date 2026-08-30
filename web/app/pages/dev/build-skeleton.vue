<script setup lang="ts">
definePageMeta({ layout: 'default' })

useSeoMeta({
  title: 'Build skeleton playground',
  description: 'Isolated visual review of the champion build section loading states.',
})

// The two build skeletons, side by side with nothing to fetch. They are the
// real components in `pending` mode (see `utils/build-placeholder`), so what
// this page shows is exactly the layout the loaded card occupies — which is the
// property worth being able to eyeball without waiting for a slow network on
// the champion page.
</script>

<template>
  <main class="mx-auto w-full max-w-[96rem] space-y-6 p-4 md:p-6">
    <h1 class="text-lg font-semibold text-default">
      Build skeleton playground
    </h1>

    <section class="space-y-3">
      <h2 class="text-sm font-medium text-muted">
        ChampionBuildTabsSkeleton — champion page
      </h2>
      <ChampionBuildTabsSkeleton />
    </section>

    <section class="space-y-3">
      <h2 class="text-sm font-medium text-muted">
        ChampionBuildTabsSkeleton — player-scoped page (no power spikes)
      </h2>
      <ChampionBuildTabsSkeleton :powerspikes="false" />
    </section>

    <section class="space-y-3">
      <h2 class="text-sm font-medium text-muted">
        ChampionBuildCoreSkeleton — composition builder
      </h2>
      <SectionCard title="Standard build">
        <ChampionBuildCoreSkeleton />
      </SectionCard>
    </section>
  </main>
</template>
