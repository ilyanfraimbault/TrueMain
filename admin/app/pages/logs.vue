<script setup lang="ts">
// TODO(panel): Logs panel.
// Stream / page application logs from `/api/ops/*`. Replace <ComingSoon> with
// the real log viewer; keep the chrome below intact.
</script>

<template>
  <UDashboardPanel id="logs">
    <template #header>
      <UDashboardNavbar title="Logs" icon="i-lucide-scroll-text">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): level / source filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Search logs…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Logs"
        icon="i-lucide-scroll-text"
        description="Browse and filter application logs."
      />
    </template>
  </UDashboardPanel>
</template>
