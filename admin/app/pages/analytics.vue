<script setup lang="ts">
// Analytics panel (#728) — a window onto the self-hosted Umami instance,
// which tracks visitors/sessions on the public site and lives fully outside
// the TrueMain backend (own container + database). The iframe prefers the
// public share URL (renders without an Umami login); when only the app URL
// is configured it embeds that instead, and Umami's own login shows inside
// the frame.
//
// Session replays and heatmaps (#1013) are deliberately absent from Umami's
// share view — a share link is an unauthenticated public URL and a replay is
// a full DOM recording of a visit — so they are unreachable from the iframe.
// They get deep links into the authenticated app instead, which keeps them
// behind Umami's login.
const config = useRuntimeConfig().public

const umamiUrl = computed(() => config.umamiUrl)
const embedUrl = computed(() => config.umamiShareUrl || config.umamiUrl)

const websiteLinks = computed(() => {
  const { umamiUrl: url, umamiWebsiteId: websiteId } = config
  if (!url || !websiteId) {
    return []
  }

  const base = `${url.replace(/\/+$/, '')}/websites/${websiteId}`

  return [
    { label: 'Replays', icon: 'i-lucide-play', to: `${base}/replays` },
    { label: 'Heatmaps', icon: 'i-lucide-flame', to: `${base}/heatmaps` },
  ]
})
</script>

<template>
  <UDashboardPanel id="analytics" :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <UDashboardNavbar title="Analytics" icon="i-lucide-chart-line">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            v-for="link in websiteLinks"
            :key="link.to"
            :icon="link.icon"
            color="neutral"
            variant="ghost"
            :label="link.label"
            :to="link.to"
            target="_blank"
          />
          <UButton
            v-if="umamiUrl"
            icon="i-lucide-external-link"
            color="neutral"
            variant="ghost"
            label="Open in Umami"
            :to="umamiUrl"
            target="_blank"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <iframe
        v-if="embedUrl"
        :src="embedUrl"
        title="Umami analytics dashboard"
        class="size-full border-0"
      />
      <div v-else class="flex size-full items-center justify-center p-8">
        <UAlert
          color="neutral"
          variant="subtle"
          icon="i-lucide-chart-line"
          title="Umami is not configured"
          description="Set NUXT_PUBLIC_UMAMI_URL to the Umami instance URL (and optionally NUXT_PUBLIC_UMAMI_SHARE_URL to a website share link for a login-free embed), then restart the admin container."
          class="max-w-xl"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
