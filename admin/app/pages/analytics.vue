<script setup lang="ts">
// Analytics panel (#728) — a window onto the self-hosted Umami instance,
// which tracks visitors/sessions on the public site and lives fully outside
// the TrueMain backend (own container + database). The iframe prefers the
// public share URL (renders without an Umami login); when only the app URL
// is configured it embeds that instead, and Umami's own login shows inside
// the frame.
const config = useRuntimeConfig().public

const umamiUrl = computed(() => config.umamiUrl)
const embedUrl = computed(() => config.umamiShareUrl || config.umamiUrl)
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
