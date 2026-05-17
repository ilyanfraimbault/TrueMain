<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'

useSeoMeta({
  title: 'TrueMain — Champion builds from real mains',
  description: 'League of Legends champion builds, runes and skill orders aggregated from true main players — patch by patch.',
})

const heroLinks: ButtonProps[] = [
  { label: 'Explore champions', to: '/champions', color: 'primary', size: 'lg', icon: 'i-lucide-swords' },
  { label: 'See the meta', to: '/meta', color: 'neutral', variant: 'subtle', size: 'lg', icon: 'i-lucide-trending-up' },
]

const steps = [
  {
    step: '01',
    icon: 'i-lucide-radar',
    title: 'Identify true mains',
    description: 'We continuously scan ranked games to flag players who consistently play the same champion across patches — no smurfs, no one-off picks.',
  },
  {
    step: '02',
    icon: 'i-lucide-database',
    title: 'Aggregate every decision',
    description: 'For each main game, we capture the full item path, rune page, skill order, summoners and final result — then group them into build trees.',
  },
  {
    step: '03',
    icon: 'i-lucide-sparkles',
    title: 'Surface what wins',
    description: 'Filter by patch, position or region and instantly see the build paths, rune pages and skill orders with the highest pickRate and winRate.',
  },
]

const features = [
  {
    icon: 'i-lucide-target',
    title: 'Main-only sample',
    description: 'Stats come exclusively from players who main the champion. No casual one-trick noise, no off-role outliers diluting the signal.',
  },
  {
    icon: 'i-lucide-route',
    title: 'Full build paths',
    description: 'Browse the most-played item sequences as a tree, with pickRate and winRate at every node — not just the final 6-item core.',
  },
  {
    icon: 'i-lucide-history',
    title: 'Patch-aware filtering',
    description: 'Compare current and past patches to spot meta shifts the moment they happen. Every page is filterable by patch and position.',
  },
  {
    icon: 'i-lucide-shield-check',
    title: 'Honest sample sizes',
    description: 'Every recommendation surfaces the number of games behind it. No confident-sounding builds backed by three games.',
  },
  {
    icon: 'i-lucide-zap',
    title: 'Fast, no-fluff UI',
    description: 'Built on Nuxt with server-side caching. Open a champion page, scan the build, jump to the next one.',
  },
  {
    icon: 'i-lucide-globe',
    title: 'Region breakdowns',
    description: 'Filter by platform to see how EUW, KR or NA mains diverge — the meta is rarely the same across regions.',
  },
]

const faqs = [
  {
    label: 'What counts as a "true main"?',
    content: 'A player who plays the same champion across multiple ranked games within the recent patch window. The exact threshold adapts to the champion\'s popularity to keep the sample meaningful.',
  },
  {
    label: 'How often is data refreshed?',
    content: 'Our ingestor pulls fresh ranked games continuously. Build aggregations re-run on every new patch cycle.',
  },
  {
    label: 'Why does the build path matter more than core items?',
    content: 'Core items tell you "what to buy" but not "in what order, given the matchup". The build tree shows the actual decision tree mains follow, branch by branch.',
  },
  {
    label: 'Do I need an account?',
    content: 'No. TrueMain is read-only and free to browse. Just open the champion page you care about.',
  },
]
</script>

<template>
  <div>
    <!-- Hero -->
    <UPageHero
      headline="Champion intelligence"
      title="Real builds from real mains."
      description="TrueMain aggregates ranked games from players who genuinely main each champion, then surfaces the builds, runes and skill orders that actually win — patch by patch."
      :links="heroLinks"
      :ui="{
        container: 'relative py-24 sm:py-32',
        title: 'sm:text-6xl lg:text-7xl tracking-tighter leading-[1.05]',
        description: 'mt-5 max-w-2xl mx-auto text-base sm:text-lg leading-relaxed text-default',
        links: 'gap-3',
      }"
    >
      <template #top>
        <div
          aria-hidden="true"
          class="pointer-events-none absolute inset-x-0 top-0 -z-10 h-[80%]"
          style="background: radial-gradient(ellipse at top, color-mix(in oklch, var(--ui-color-primary-500) 18%, transparent), transparent 60%);"
        />
      </template>
    </UPageHero>

    <!-- How it works -->
    <UPageSection
      id="how-it-works"
      headline="How it works"
      title="Three steps from raw games to a build you can trust."
      description="No black-box ranking. Every number you see is grounded in a sample of games we can point to."
      :ui="{
        root: 'py-24 sm:py-32',
        container: 'max-w-5xl',
        headline: 'font-mono font-medium text-xs text-primary uppercase tracking-[0.12em] text-center',
        title: 'max-w-2xl mx-auto',
        description: 'max-w-xl mx-auto text-dimmed',
      }"
    >
      <div class="mt-12 grid gap-6 sm:grid-cols-3">
        <div
          v-for="step in steps"
          :key="step.step"
          class="space-y-3"
        >
          <div class="flex items-center gap-3">
            <span class="font-mono text-xs tabular-nums text-primary">{{ step.step }}</span>
            <UIcon
              :name="step.icon"
              class="size-5 text-primary"
            />
          </div>
          <h3 class="text-lg font-semibold text-highlighted">
            {{ step.title }}
          </h3>
          <p class="text-sm leading-relaxed text-muted">
            {{ step.description }}
          </p>
        </div>
      </div>
    </UPageSection>

    <!-- Features grid -->
    <UPageSection
      id="features"
      headline="Why TrueMain"
      title="Built to cut through the noise."
      description="Generalist sites blend every game on the planet. TrueMain narrows to dedicated players, so the signal isn't drowned in casual or off-role data."
      :ui="{
        root: 'py-24 sm:py-32 bg-elevated/30',
        container: 'max-w-5xl',
        headline: 'font-mono font-medium text-xs text-primary uppercase tracking-[0.12em] text-center',
        title: 'max-w-lg mx-auto',
        description: 'max-w-md mx-auto text-dimmed',
      }"
    >
      <UPageGrid class="mt-12 sm:grid-cols-2 lg:grid-cols-3">
        <UPageCard
          v-for="feature in features"
          :key="feature.title"
          :icon="feature.icon"
          :title="feature.title"
          :description="feature.description"
          variant="subtle"
        />
      </UPageGrid>
    </UPageSection>

    <!-- FAQ -->
    <UPageSection
      id="faq"
      headline="FAQ"
      title="Questions, answered."
      :ui="{
        root: 'py-24 sm:py-32',
        container: 'max-w-3xl',
        headline: 'font-mono font-medium text-xs text-primary uppercase tracking-[0.12em] text-center',
        title: 'mx-auto',
      }"
    >
      <UAccordion
        :items="faqs"
        class="mt-12"
      />
    </UPageSection>

    <!-- CTA -->
    <section class="border-t border-default">
      <div class="mx-auto max-w-3xl px-6 py-24 text-center sm:py-32">
        <h2 class="text-3xl font-semibold tracking-tight sm:text-4xl">
          Find <span class="text-primary">your</span> real build.
        </h2>
        <p class="mx-auto mt-4 max-w-xl text-base text-muted">
          Open the champion you actually play and see what their mains are buying this patch.
        </p>
        <div class="mt-8 flex flex-wrap justify-center gap-3">
          <UButton
            to="/champions"
            color="primary"
            size="lg"
            icon="i-lucide-swords"
            label="Explore champions"
          />
          <UButton
            to="/meta"
            color="neutral"
            variant="subtle"
            size="lg"
            icon="i-lucide-trending-up"
            label="See the meta"
          />
        </div>
      </div>
    </section>
  </div>
</template>
