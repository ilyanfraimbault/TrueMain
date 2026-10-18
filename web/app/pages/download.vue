<script setup lang="ts">
import type { DesktopRelease } from '~~/shared/types/desktop'

// The desktop companion's landing page (#1719, #1725, #2072): one section per
// moment of a game — champion select, in game, recordings —
// each with the app's UI drawn live (`components/desktop/landing/*`) rather
// than a capture, then the download, the first launch and the questions.
useSeoMeta({
  title: 'TrueMain desktop app (beta)',
  description:
    'The TrueMain companion for League of Legends: picks from your own pool in champion select, the rune page in one click, an overlay with win probability, your pace and your next item, and your games recorded to cut your best plays. For macOS and Windows.',
})

const { data: release, status, error } = await useFetch<DesktopRelease | null>('/api/desktop/release')
const { platform, other, onPhone } = useDesktopPlatform()

// The drawn scenes' icons: the latest patch, and the item map
// only once a scene that needs it comes near — it is not above the fold.
const { data: versions } = useDDragonVersions()
const patch = computed(() => versions.value[0] ?? null)
const scenes = ref<HTMLElement | null>(null)
const nearScenes = useVisibleOnce(scenes, { rootMargin: '800px' })
const staticPatch = computed(() => (nearScenes.value ? patch.value : null))
const { data: items, execute: readItems } = useStaticItems(staticPatch, { immediate: false, unresolvedKeySegment: 'pending' })
// A key that changes does not start a fetch that was never run (`immediate: false`): start it once the patch is in.
const stopReading = watch(staticPatch, (value) => {
  if (!value) return
  readItems()
  queueMicrotask(() => stopReading())
}, { immediate: true })

const DRAFT_POINTS = [
  { icon: 'i-lucide-list-ordered', title: 'Picks from your pool', body: 'Your ten most-mastered champions on your lane, ranked as the draft fills, each with its reason: the matchup, a synergy, a safe blind.' },
  { icon: 'i-lucide-hand', title: 'Hover, lock and ban from the app', body: 'On your click only. A ban always asks first, and nothing is ever locked, banned or accepted for you.' },
  { icon: 'i-lucide-download', title: 'Runes and item set in one click', body: 'The page and the build on screen go into the client as "TrueMain: <champion>", replaced in place — never one of your pages.' },
] as const

const MORE = [
  { icon: 'i-lucide-user-round', title: 'Your profile', body: 'Rank, form and LP curve, for any account.' },
  { icon: 'i-lucide-scan-eye', title: 'The lobby, scouted', body: 'Each player\'s rank, role and form on their pick.' },
  { icon: 'i-lucide-badge-check', title: 'True mains, marked', body: 'Who mains their champion, and their build.' },
  { icon: 'i-lucide-chart-pie', title: 'The damage mix', body: 'Each team\'s physical and magic split.' },
  { icon: 'i-lucide-layout-list', title: 'The site, inside', body: 'Tier list, champions and matchups, no browser.' },
  { icon: 'i-lucide-command', title: 'One search field', body: '⌘K or Ctrl K, even with League closed.' },
] as const
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 pb-24 md:px-6">
    <section class="pt-8 sm:pt-14">
      <div class="flex items-center gap-3">
        <DesktopAppIcon class="size-9" />
        <p class="text-sm font-medium text-highlighted">
          TrueMain for desktop
        </p>
        <UBadge color="primary" variant="subtle" size="sm" label="Beta" />
      </div>

      <div class="mt-8 grid gap-8 lg:grid-cols-[1fr_auto] lg:items-end">
        <div>
          <h1 class="text-4xl font-semibold leading-[1.05] tracking-tighter text-balance text-highlighted sm:text-6xl">
            Every game,<br>
            <span class="text-primary">read for you</span>.
          </h1>
          <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
            The TrueMain app sits next to League: it ranks your picks in champion select, puts what matters over the
            game, and records it so the plays worth keeping are a cut away.
          </p>
        </div>
        <DesktopLandingDownloadButtons
          :release="release"
          :pending="status === 'pending'"
          :failed="Boolean(error)"
          :platform="platform"
          :other="other"
          :on-phone="onPhone"
          class="lg:items-end lg:text-right"
        />
      </div>
    </section>

    <!-- The app itself before the drawn scenes: a capture of the real UI over live API answers (#1725). -->
    <figure class="landing-hero-shot relative mt-12 sm:mt-16">
      <div aria-hidden="true" class="pointer-events-none absolute inset-x-[10%] -top-8 -z-10 h-2/3 rounded-full bg-primary/10 blur-3xl" />
      <div class="overflow-hidden rounded-xl ring-1 ring-white/15 shadow-[0_30px_80px_-20px_rgb(0_0_0/0.8)] [mask-image:linear-gradient(to_bottom,black_70%,transparent)]">
        <SkeletonPicture
          src="/desktop/champion-select.webp"
          :width="1180"
          :height="760"
          alt="The TrueMain app during champion select: both teams with their tiers, the lane duel, and the build for Ahri against the draft."
        />
      </div>
    </figure>

    <div ref="scenes">
      <DesktopLandingSection
        id="champion-select"
        moment="Champion select"
        title="The pick, the page, then lock in."
        lead="Picks ranked from your own pool, the enemy lanes worked out, the build against the draft — and the rune page in the client before the clock runs out."
      >
        <div class="grid gap-10 lg:grid-cols-[minmax(0,1fr)_minmax(0,20rem)] lg:gap-14">
          <figure>
            <DesktopLandingDraftDemo :patch="patch" aria-hidden="true" />
          </figure>
          <ul class="flex flex-col gap-6">
            <li v-for="point in DRAFT_POINTS" :key="point.title" class="border-t border-default pt-4">
              <h3 class="flex items-center gap-2 font-semibold text-highlighted">
                <UIcon :name="point.icon" class="size-4 shrink-0 text-primary" />
                {{ point.title }}
              </h3>
              <p class="mt-1.5 text-sm leading-relaxed text-muted text-pretty">
                {{ point.body }}
              </p>
            </li>
          </ul>
        </div>
      </DesktopLandingSection>

      <DesktopLandingSection
        id="in-game"
        moment="In game"
        title="Four small panels, right on the game."
        lead="Drawn from the game's own live data in windows of their own that never take your keyboard. Nothing is injected and nothing reads the game's memory. Move them, resize them, or hide them all with one shortcut."
      >
        <DesktopLandingInGame :patch="patch" :items="items ?? null" />
      </DesktopLandingSection>

      <DesktopLandingSection
        id="recordings"
        moment="Recordings"
        title="Your games on tape, the plays a cut away."
        lead="Turn recording on and every game is captured with its kills, deaths, assists and objectives on the timeline. Drag across a moment to cut a clip; it is named from what it holds and kept as its own file, ready to publish on YouTube."
      >
        <figure>
          <DesktopLandingRecapDemo aria-hidden="true" />
        </figure>
      </DesktopLandingSection>
    </div>

    <section class="pt-24 sm:pt-32">
      <h2 class="text-2xl font-semibold tracking-tight text-highlighted">
        And between games
      </h2>
      <ul class="mt-8 grid gap-x-10 gap-y-8 sm:grid-cols-2 lg:grid-cols-3">
        <li v-for="entry in MORE" :key="entry.title" class="border-t border-default pt-4">
          <h3 class="flex items-center gap-2 font-semibold text-highlighted">
            <UIcon :name="entry.icon" class="size-4 shrink-0 text-primary" />
            {{ entry.title }}
          </h3>
          <p class="mt-1.5 text-sm leading-relaxed text-muted text-pretty">
            {{ entry.body }}
          </p>
        </li>
      </ul>
    </section>

    <section id="download" class="scroll-mt-24 pt-24 sm:pt-32">
      <div class="grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
        <div>
          <h2 class="text-3xl font-semibold tracking-tight text-highlighted">
            Get the beta.
          </h2>
          <p class="mt-3 text-sm leading-relaxed text-muted">
            Free, for macOS and Windows. It updates itself when a new beta is out.
          </p>
        </div>
        <div class="space-y-6">
          <DesktopLandingPlatformRows :release="release" :platform="platform" />
          <p v-if="release" class="text-sm text-dimmed">
            Version {{ release.version }}<template v-if="release.channel === 'beta'">
              · test build reading this preprod's data
            </template>
          </p>
        </div>
      </div>

      <div class="mt-16 grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
        <div>
          <h3 class="text-xl font-semibold tracking-tight text-highlighted">
            Opening it the first time
          </h3>
          <p class="mt-3 text-sm leading-relaxed text-muted">
            The beta is not signed by Apple or Microsoft yet, so your computer asks once whether to trust it. After that
            it opens like any other app.
          </p>
        </div>
        <DesktopFirstLaunch :platform="platform" />
      </div>

      <div class="mt-16 grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
        <h3 class="text-xl font-semibold tracking-tight text-highlighted">
          What it needs
        </h3>
        <p class="max-w-2xl text-sm leading-relaxed text-muted">
          The League of Legends client on the same computer — no account and no sign-in. The app reads your champion
          select, your rank and your recent games from the client — your games stay on your computer — and asks TrueMain
          for the picks and builds that fit the draft. It is a beta: expect rough edges while it grows.
        </p>
      </div>
    </section>

    <section class="pt-24 sm:pt-32">
      <div class="grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
        <h2 class="text-3xl font-semibold tracking-tight text-highlighted">
          Questions
        </h2>
        <DesktopLandingFaq />
      </div>
    </section>
  </div>
</template>

<style scoped>
.landing-hero-shot {
  animation: landing-hero-rise 900ms cubic-bezier(0.2, 0, 0, 1) both;
}

@keyframes landing-hero-rise {
  from { opacity: 0.4; translate: 0 24px; }
}

@media (prefers-reduced-motion: reduce) {
  .landing-hero-shot { animation: none; }
}
</style>
