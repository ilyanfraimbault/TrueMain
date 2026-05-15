<script setup lang="ts">
useSeoMeta({
  title: 'TrueMain',
  description: 'Bootstrap runtime boundary for the TrueMain web application.',
})
</script>

<template>
  <main class="mx-auto grid min-h-screen max-w-2xl place-items-center p-8">
    <section class="space-y-3">
      <p class="text-xs font-bold uppercase tracking-widest text-primary">
        TrueMain
      </p>
      <h1 class="text-balance text-4xl font-semibold sm:text-5xl">
        Web runtime boundary is online.
      </h1>
      <p class="text-base text-muted">
        This Nuxt application is the public web entrypoint for TrueMain. It is wired into the
        monorepo runtime alongside the API, ingestor, and PostgreSQL services.
      </p>
    </section>
  </main>
</template>
