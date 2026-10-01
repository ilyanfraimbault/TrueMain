<script setup lang="ts">
import type { DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

// The desktop companion's download page (#1719). The installers are the newest
// `desktop-v*` release's, reached through stable per-platform links
// (`/api/desktop/download/{platform}`), so the page never names a file.
//
// The platform is read in the browser, after hydration: the server cannot know
// it, and guessing from the user agent there would cache one visitor's answer
// for the next. Until then — and on a phone, or Linux — both installers are
// offered side by side.
useSeoMeta({
  title: 'TrueMain desktop app (beta)',
  description:
    'The TrueMain companion for League of Legends champion select: pick advice from your own champion pool, the enemy lanes read for you, and the build against the draft. For macOS and Windows.',
})

const { data: release, status, error } = await useFetch<DesktopRelease | null>('/api/desktop/release')

const PLATFORMS: Record<DesktopPlatform, { label: string, icon: string, requirement: string }> = {
  mac: { label: 'macOS', icon: 'i-simple-icons-apple', requirement: 'macOS 13.3 or later, Apple Silicon or Intel' },
  windows: { label: 'Windows', icon: 'i-simple-icons-windows', requirement: 'Windows 10 or 11, 64-bit' },
}

const platform = ref<DesktopPlatform | null>(null)
const onPhone = ref(false)

onMounted(() => {
  const agent = navigator.userAgent
  const reported = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform ?? navigator.platform ?? ''
  // iPadOS announces itself as a Mac; its touch points give it away.
  onPhone.value = /iPhone|iPad|Android/i.test(agent) || (/Mac/i.test(reported) && navigator.maxTouchPoints > 1)
  if (onPhone.value) return
  if (/Mac/i.test(reported) || /Mac OS X/.test(agent)) platform.value = 'mac'
  else if (/Win/i.test(reported) || /Windows/.test(agent)) platform.value = 'windows'
})

const other = computed<DesktopPlatform | null>(() => (platform.value === 'mac' ? 'windows' : platform.value === 'windows' ? 'mac' : null))
const available = (target: DesktopPlatform) => Boolean(release.value?.installers[target])
const downloadUrl = (target: DesktopPlatform) => `/api/desktop/download/${target}`

const releasedOn = computed(() => (release.value
  ? new Date(release.value.publishedAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
  : null))

const FEATURES = [
  {
    icon: 'i-lucide-list-ordered',
    title: 'Picks from your own pool',
    body: 'Your most-mastered champions on your lane, ranked as the draft fills: the matchup into your lane opponent and the synergy with the allies already locked.',
  },
  {
    icon: 'i-lucide-crosshair',
    title: 'The enemy lanes, read for you',
    body: 'Who goes where on the other side is guessed from the picks. A wrong guess is one click to correct, or a drag from one lane to another.',
  },
  {
    icon: 'i-lucide-hammer',
    title: 'The build against the draft',
    body: 'Runes, items and skill order for your pick, against the composition in front of you — or the build a true main of that champion runs.',
  },
  {
    icon: 'i-lucide-user-round',
    title: 'Your profile, from your client',
    body: 'Your rank, your form over the last games and your match history, read from the League client — whether TrueMain tracks you or not.',
  },
] as const

const FACTS = [
  { icon: 'i-lucide-user-x', label: 'No account' },
  { icon: 'i-lucide-refresh-cw', label: 'Updates itself' },
  { icon: 'i-lucide-hand', label: 'Never plays for you' },
] as const
</script>


<template>
  <div class="pb-20">
    <!-- Hero: the app's icon in its own light, what it is, and the download.
         The glow is a plain blurred disc rather than the home page's eclipse,
         which stays the home hero's alone. -->
    <section class="relative isolate overflow-hidden">
      <div
        aria-hidden="true"
        class="pointer-events-none absolute left-1/2 top-0 -z-10 h-[28rem] w-[44rem] max-w-[140%] -translate-x-1/2 -translate-y-1/3 rounded-full bg-primary/10 blur-3xl"
      />
      <div class="mx-auto flex max-w-3xl flex-col items-center px-4 pb-14 pt-8 text-center sm:pt-12 md:px-6">
        <DesktopAppIcon class="size-20 sm:size-24" />

        <div class="mt-8 flex items-center gap-2">
          <p class="eyebrow">
            Desktop app
          </p>
          <UBadge
            color="primary"
            variant="subtle"
            size="sm"
            label="Beta"
          />
        </div>
        <h1 class="mt-3 text-4xl font-semibold leading-[1.05] tracking-tighter text-balance text-highlighted sm:text-5xl">
          TrueMain, in your <span class="text-primary">champion select</span>.
        </h1>
        <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
          Picks ranked from your own pool, the enemy lanes read for you, and the build against the draft as it stands.
        </p>

        <div class="mt-9 flex w-full flex-col items-center gap-4">
          <template v-if="release">
            <div class="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
              <template v-if="platform">
                <UButton
                  :to="downloadUrl(platform)"
                  external
                  size="xl"
                  :icon="PLATFORMS[platform].icon"
                  :label="`Download for ${PLATFORMS[platform].label}`"
                  :disabled="!available(platform)"
                  class="justify-center"
                />
                <UButton
                  v-if="other && available(other)"
                  :to="downloadUrl(other)"
                  external
                  size="xl"
                  color="neutral"
                  variant="ghost"
                  :icon="PLATFORMS[other].icon"
                  :label="`Also for ${PLATFORMS[other].label}`"
                  class="justify-center"
                />
              </template>
              <template v-else>
                <UButton
                  v-for="target in (['mac', 'windows'] as const)"
                  :key="target"
                  :to="downloadUrl(target)"
                  external
                  size="xl"
                  :color="target === 'mac' ? 'primary' : 'neutral'"
                  :variant="target === 'mac' ? 'solid' : 'subtle'"
                  :icon="PLATFORMS[target].icon"
                  :label="`Download for ${PLATFORMS[target].label}`"
                  :disabled="!available(target)"
                  class="justify-center"
                />
              </template>
            </div>
            <p class="text-sm text-muted">
              Version <span class="font-medium tabular-nums text-default">{{ release.version }}</span> · released {{ releasedOn }}
              <template v-if="onPhone">
                <br>The app runs on a computer, next to the League client: open this page there.
              </template>
            </p>
          </template>

          <UAlert
            v-else-if="error"
            color="warning"
            variant="subtle"
            icon="i-lucide-cloud-off"
            title="The download is unavailable for a moment"
            description="The list of releases could not be read. Try again in a few minutes."
            class="max-w-lg text-left"
          />
          <div
            v-else-if="status !== 'pending'"
            class="inline-flex items-center gap-2.5 rounded-full bg-elevated px-4 py-2 text-sm text-muted ring-1 ring-default"
          >
            <span class="relative flex size-2">
              <span class="absolute inline-flex size-full animate-ping rounded-full bg-primary/60" />
              <span class="relative inline-flex size-2 rounded-full bg-primary" />
            </span>
            The first beta is on its way — this page offers it as soon as it is out.
          </div>

          <ul class="flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-sm text-muted">
            <li
              v-for="fact in FACTS"
              :key="fact.label"
              class="flex items-center gap-1.5"
            >
              <UIcon
                :name="fact.icon"
                class="size-4 text-primary"
              />
              {{ fact.label }}
            </li>
          </ul>
        </div>
      </div>
    </section>

    <div class="mx-auto max-w-5xl space-y-16 px-4 md:px-6">
      <section>
        <div class="mb-6">
          <p class="eyebrow">
            What it does
          </p>
          <h2 class="mt-2 text-2xl font-semibold tracking-tight text-balance text-highlighted sm:text-3xl">
            Champion select, read for you.
          </h2>
        </div>
        <ul class="grid gap-4 sm:grid-cols-2">
          <li
            v-for="feature in FEATURES"
            :key="feature.title"
            class="surface rounded-xl p-5 sm:p-6"
          >
            <span class="flex size-10 items-center justify-center rounded-lg bg-primary/10 ring-1 ring-primary/30">
              <UIcon
                :name="feature.icon"
                class="size-5 text-primary"
              />
            </span>
            <h3 class="mt-4 font-semibold text-highlighted">
              {{ feature.title }}
            </h3>
            <p class="mt-1.5 text-sm leading-relaxed text-muted">
              {{ feature.body }}
            </p>
          </li>
        </ul>
      </section>

      <section>
        <div class="mb-6">
          <p class="eyebrow">
            First launch
          </p>
          <h2 class="mt-2 text-2xl font-semibold tracking-tight text-balance text-highlighted sm:text-3xl">
            Opening it the first time.
          </h2>
          <p class="mt-3 max-w-2xl text-sm leading-relaxed text-muted sm:text-base">
            The beta is not signed by Apple or Microsoft yet, so your computer asks once whether to trust it. After that it
            opens like any other app.
          </p>
        </div>
        <DesktopFirstLaunch :platform="platform" />
      </section>

      <section class="grid gap-4 md:grid-cols-2">
        <div class="surface rounded-xl p-5 sm:p-6">
          <h2 class="font-semibold text-highlighted">
            What it needs
          </h2>
          <ul class="mt-4 space-y-2.5 text-sm text-muted">
            <li
              v-for="(entry, target) in PLATFORMS"
              :key="target"
              class="flex items-center gap-2.5"
            >
              <UIcon
                :name="entry.icon"
                class="size-4 shrink-0 text-dimmed"
              />
              {{ entry.requirement }}
            </li>
            <li class="flex items-center gap-2.5">
              <UIcon
                name="i-lucide-gamepad-2"
                class="size-4 shrink-0 text-dimmed"
              />
              The League of Legends client, on the same computer
            </li>
          </ul>
        </div>
        <div class="surface rounded-xl p-5 sm:p-6">
          <h2 class="font-semibold text-highlighted">
            What it reads
          </h2>
          <p class="mt-4 text-sm leading-relaxed text-muted">
            No account and no sign-in. The app reads your champion select, your rank and your recent games from the
            League client on your computer — your games stay on it — and asks TrueMain for the picks and builds that fit
            the draft. It never plays, picks or types anything for you.
          </p>
        </div>
      </section>

      <p class="flex items-start justify-center gap-2 text-center text-sm text-dimmed">
        <UIcon
          name="i-lucide-flask-conical"
          class="mt-0.5 size-4 shrink-0"
        />
        <span>A beta: expect rough edges and frequent changes. It updates itself, so each fix reaches you without reinstalling.</span>
      </p>
    </div>
  </div>
</template>
