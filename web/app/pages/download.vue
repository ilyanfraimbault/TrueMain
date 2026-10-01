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

/** Which first-launch steps are unfolded: macOS until the platform is known, then the visitor's. */
const firstLaunchOpen = ref<string>('mac')
watch(platform, (detected) => {
  if (detected) firstLaunchOpen.value = detected
})
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Desktop app · Beta"
      title="TrueMain on your desktop"
      description="Champion select, read for you: picks ranked from your own pool, the enemy lanes guessed, and the build against the draft as it stands."
    />

    <UAlert
      color="primary"
      variant="subtle"
      icon="i-lucide-flask-conical"
      title="This is a beta"
      description="The app is still being built: expect rough edges and frequent changes. It updates itself, so each fix reaches you without reinstalling."
    />

    <section class="surface space-y-4 rounded-xl p-5 sm:p-6">
      <template v-if="release">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
          <template v-if="platform">
            <UButton
              :to="downloadUrl(platform)"
              external
              size="xl"
              :icon="PLATFORMS[platform].icon"
              :label="`Download for ${PLATFORMS[platform].label}`"
              :disabled="!available(platform)"
            />
            <UButton
              v-if="other && available(other)"
              :to="downloadUrl(other)"
              external
              color="neutral"
              variant="ghost"
              :icon="PLATFORMS[other].icon"
              :label="`Also for ${PLATFORMS[other].label}`"
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
            />
          </template>
        </div>
        <p class="text-sm text-muted">
          Version {{ release.version }} · released {{ releasedOn }}.
          <template v-if="onPhone">
            The app runs on a computer, next to the League client: open this page there.
          </template>
          <template v-else>
            It updates itself when a new beta is out.
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
      />
      <p v-else-if="status !== 'pending'" class="text-sm text-muted">
        The first beta is on its way. This page will offer it as soon as it is out.
      </p>
    </section>

    <section class="surface space-y-3 rounded-xl p-5 sm:p-6">
      <h2 class="text-base font-semibold text-highlighted">
        Opening it the first time
      </h2>
      <p class="text-sm text-muted">
        The beta is not signed by Apple or Microsoft yet, so your computer asks once whether to trust it. After that it opens like any other app.
      </p>
      <UAccordion
        v-model="firstLaunchOpen"
        :items="[
          { label: 'On macOS', value: 'mac', icon: PLATFORMS.mac.icon, slot: 'mac' as const },
          { label: 'On Windows', value: 'windows', icon: PLATFORMS.windows.icon, slot: 'windows' as const },
        ]"
      >
        <template #mac-body>
          <ol class="list-decimal space-y-1.5 pl-5 text-sm text-default">
            <li>Open the downloaded <code>.dmg</code> and drag TrueMain into Applications.</li>
            <li>Open TrueMain. macOS says it cannot check it for malicious software: choose <strong>Done</strong>.</li>
            <li>Open <strong>System Settings → Privacy &amp; Security</strong>, scroll to Security and choose <strong>Open Anyway</strong> next to TrueMain, then confirm.</li>
          </ol>
        </template>
        <template #windows-body>
          <ol class="list-decimal space-y-1.5 pl-5 text-sm text-default">
            <li>Run the downloaded installer.</li>
            <li>If Windows shows <strong>"Windows protected your PC"</strong>, choose <strong>More info</strong>, then <strong>Run anyway</strong>.</li>
            <li>The installer finishes on its own and TrueMain opens.</li>
          </ol>
        </template>
      </UAccordion>
    </section>

    <section class="surface space-y-3 rounded-xl p-5 sm:p-6">
      <h2 class="text-base font-semibold text-highlighted">
        What it needs
      </h2>
      <ul class="space-y-1.5 text-sm text-muted">
        <li v-for="(entry, target) in PLATFORMS" :key="target" class="flex items-center gap-2">
          <UIcon :name="entry.icon" class="size-4 shrink-0 text-dimmed" />
          {{ entry.requirement }}
        </li>
        <li class="flex items-center gap-2">
          <UIcon name="i-lucide-gamepad-2" class="size-4 shrink-0 text-dimmed" />
          The League of Legends client, on the same computer
        </li>
      </ul>
      <p class="text-sm text-muted">
        No account and no sign-in. The app reads your champion select, your rank and your recent games from the League
        client on your computer — your games stay on it — and asks TrueMain for the picks and builds that fit the draft;
        it never plays, picks or types anything for you.
      </p>
    </section>
  </div>
</template>
