<script setup lang="ts">
import type { DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

/**
 * The download call to action (#1719): the visitor's installer first and the
 * other beside it once the platform is read; both side by side before that, on
 * a phone or on Linux. Installers go through the stable per-platform links
 * (`/api/desktop/download/{platform}`), so the page never names a file.
 */
const props = defineProps<{
  release: DesktopRelease | null | undefined
  pending: boolean
  failed: boolean
  platform: DesktopPlatform | null
  other: DesktopPlatform | null
  onPhone: boolean
}>()

const available = (target: DesktopPlatform) => Boolean(props.release?.installers[target])
const downloadUrl = (target: DesktopPlatform) => `/api/desktop/download/${target}`

const releasedOn = computed(() => (props.release
  ? new Date(props.release.publishedAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
  : null))
</script>

<template>
  <div class="flex flex-col gap-3">
    <template v-if="release">
      <div class="flex flex-col gap-3 sm:flex-row">
        <template v-if="platform">
          <UButton
            :to="downloadUrl(platform)"
            external
            size="xl"
            :icon="DESKTOP_PLATFORMS[platform].icon"
            :label="`Download for ${DESKTOP_PLATFORMS[platform].label}`"
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
            :icon="DESKTOP_PLATFORMS[other].icon"
            :label="DESKTOP_PLATFORMS[other].label"
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
            :icon="DESKTOP_PLATFORMS[target].icon"
            :label="`Download for ${DESKTOP_PLATFORMS[target].label}`"
            :disabled="!available(target)"
            class="justify-center"
          />
        </template>
      </div>
      <p class="text-sm text-dimmed">
        Free · Version {{ release.version }} · {{ releasedOn }}
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
      v-else-if="failed"
      color="warning"
      variant="subtle"
      icon="i-lucide-cloud-off"
      title="The download is unavailable for a moment"
      description="The list of releases could not be read. Try again in a few minutes."
      class="max-w-md"
    />
    <template v-else-if="!pending">
      <UButton
        size="xl"
        color="neutral"
        variant="subtle"
        icon="i-lucide-download"
        label="Coming soon"
        disabled
        class="justify-center self-start"
      />
      <p class="text-sm text-dimmed">
        The first beta is on its way; it will be offered here.
      </p>
    </template>
  </div>
</template>
