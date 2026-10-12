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
    title: 'Picks from your own pool',
    body: 'Your most-mastered champions on your lane, ranked as the draft fills: the matchup into your lane opponent and the synergy with the allies already locked.',
  },
  {
    title: 'The enemy lanes, read for you',
    body: 'Who goes where on the other side is guessed from the picks. A wrong guess is one click to correct, or a drag from one lane to another.',
  },
  {
    title: 'The build against the draft',
    body: 'Runes, items and skill order for your pick against the composition in front of you — or the build a true main of that champion runs.',
  },
] as const
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 pb-24 md:px-6">
    <!-- Hero: what the app is and the download, left-aligned over the app
         itself — the page shows the product rather than describing it. -->
    <section class="pt-8 sm:pt-14">
      <div class="flex items-center gap-3">
        <DesktopAppIcon class="size-9" />
        <p class="text-sm font-medium text-highlighted">
          TrueMain for desktop
        </p>
        <UBadge
          color="primary"
          variant="subtle"
          size="sm"
          label="Beta"
        />
      </div>

      <div class="mt-8 grid gap-8 lg:grid-cols-[1fr_auto] lg:items-end">
        <div>
          <h1 class="text-4xl font-semibold leading-[1.05] tracking-tighter text-balance text-highlighted sm:text-6xl">
            Champion select,<br>
            <span class="text-primary">read for you</span>.
          </h1>
          <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
            The TrueMain app sits next to the League client: it ranks the picks from your own pool, works out the enemy
            lanes, and hands you the build against the draft as it stands.
          </p>
        </div>

        <div class="flex flex-col gap-3 lg:items-end">
          <template v-if="release">
            <div class="flex flex-col gap-3 sm:flex-row">
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
                  :label="PLATFORMS[other].label"
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
            <p class="text-sm text-dimmed lg:text-right">
              Version {{ release.version }} · {{ releasedOn }}
              <!-- Preprod serves the build made against preprod (#1779): a tester must know which data it reads. -->
              <template v-if="release.channel === 'beta'">
                <br>Test build: it reads this preprod's data, not truemain.lol's.
              </template>
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
            class="max-w-md"
          />
          <template v-else-if="status !== 'pending'">
            <UButton
              size="xl"
              color="neutral"
              variant="subtle"
              icon="i-lucide-download"
              label="Coming soon"
              disabled
              class="justify-center"
            />
            <p class="text-sm text-dimmed lg:text-right">
              The first beta is on its way; it will be offered here.
            </p>
          </template>
        </div>
      </div>
    </section>

    <!-- The app, as it draws a champion select: a capture of the real UI over
         live API answers (the build, its win rates and games, the tiers), at
         the app's own 1180×760 window. Its foot fades into the page rather than
         ending on a row cut in half. -->
    <figure class="relative mt-12 sm:mt-16">
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-x-[10%] -top-8 -z-10 h-2/3 rounded-full bg-primary/10 blur-3xl"
      />
      <div class="overflow-hidden rounded-xl ring-1 ring-white/15 shadow-[0_30px_80px_-20px_rgb(0_0_0/0.8)] [mask-image:linear-gradient(to_bottom,black_70%,transparent)]">
        <SkeletonPicture
          src="/desktop/champion-select.webp"
          :width="1180"
          :height="760"
          alt="The TrueMain app during champion select: both teams with their tiers, the lane duel, and the build for Ahri against the draft."
        />
      </div>
    </figure>

    <section class="mt-4 grid gap-x-10 gap-y-8 md:grid-cols-3">
      <div
        v-for="feature in FEATURES"
        :key="feature.title"
        class="border-t border-default pt-5"
      >
        <h2 class="font-semibold text-highlighted">
          {{ feature.title }}
        </h2>
        <p class="mt-2 text-sm leading-relaxed text-muted">
          {{ feature.body }}
        </p>
      </div>
    </section>

    <section class="mt-24 grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
      <div>
        <h2 class="text-2xl font-semibold tracking-tight text-highlighted">
          Opening it the first time
        </h2>
        <p class="mt-3 text-sm leading-relaxed text-muted">
          The beta is not signed by Apple or Microsoft yet, so your computer asks once whether to trust it. After that
          it opens like any other app, and updates itself when a new beta is out.
        </p>
      </div>
      <DesktopFirstLaunch :platform="platform" />
    </section>

    <section class="mt-24 grid gap-10 lg:grid-cols-[18rem_1fr] lg:gap-16">
      <h2 class="text-2xl font-semibold tracking-tight text-highlighted">
        Requirements
      </h2>
      <div class="space-y-6">
        <dl class="divide-y divide-default border-y border-default text-sm">
          <div
            v-for="(entry, target) in PLATFORMS"
            :key="target"
            class="flex items-center gap-3 py-3"
          >
            <dt class="flex w-28 shrink-0 items-center gap-2 text-highlighted">
              <UIcon
                :name="entry.icon"
                class="size-4 shrink-0"
              />
              {{ entry.label }}
            </dt>
            <dd class="text-muted">
              {{ entry.requirement }}
            </dd>
          </div>
          <div class="flex items-center gap-3 py-3">
            <dt class="flex w-28 shrink-0 items-center gap-2 text-highlighted">
              <UIcon
                name="i-lucide-gamepad-2"
                class="size-4 shrink-0"
              />
              League
            </dt>
            <dd class="text-muted">
              The League of Legends client, on the same computer
            </dd>
          </div>
        </dl>
        <p class="max-w-2xl text-sm leading-relaxed text-muted">
          No account and no sign-in. The app reads your champion select, your rank and your recent games from the
          League client on your computer — your games stay on it — and asks TrueMain for the picks and builds that fit
          the draft. It never plays, picks or types anything for you. It is a beta: expect rough edges while it grows.
        </p>
        <p class="max-w-2xl text-sm leading-relaxed text-muted">
          To show us what gets used, the app sends anonymous usage counts — how often it is opened, which pages and
          features — under a random id that is not tied to you or your League account. Uncheck "Share Anonymous Usage
          Data" in the app menu on macOS, or the tray icon's menu on Windows, to turn it off.
        </p>
      </div>
    </section>
  </div>
</template>
