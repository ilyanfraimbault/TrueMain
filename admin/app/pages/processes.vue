<script setup lang="ts">
// TODO(panel): Processes panel.
// Show ingestor / background job status and controls from `/api/ops/*`.
// Replace <ComingSoon> with the real content; keep the chrome below intact.
</script>

<template>
  <UDashboardPanel id="processes">
    <template #header>
      <UDashboardNavbar title="Processes" icon="i-lucide-activity">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): status / type filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Filter processes…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Processes"
        icon="i-lucide-activity"
        description="Background jobs and ingestor run status."
      />
    </template>
  </UDashboardPanel>
</template>
