<script setup lang="ts">
useSeoMeta({
  title: 'TrueMain',
  description: 'Bootstrap runtime boundary for the TrueMain web application.'
})
</script>

<template>
  <main class="page">
    <section class="hero">
      <p class="eyebrow">TrueMain</p>
      <h1>Web runtime boundary is online.</h1>
      <p class="copy">
        This Nuxt application is the public web entrypoint for TrueMain. It is wired into the
        monorepo runtime alongside the API, ingestor, and PostgreSQL services.
      </p>
    </section>
  </main>
</template>

<style scoped>
.page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 2rem;
  background:
    radial-gradient(circle at top, rgba(80, 92, 255, 0.18), transparent 35%),
    #0b1020;
  color: #f4f7fb;
}

.hero {
  width: min(42rem, 100%);
  padding: 2rem;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 1.5rem;
  background: rgba(11, 16, 32, 0.82);
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.35);
}

.eyebrow {
  margin: 0 0 0.75rem;
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: #8fb3ff;
}

h1 {
  margin: 0;
  font-size: clamp(2rem, 5vw, 3.5rem);
  line-height: 1.05;
}

.copy {
  margin: 1rem 0 0;
  max-width: 38rem;
  font-size: 1rem;
  line-height: 1.65;
  color: rgba(244, 247, 251, 0.82);
}
</style>
