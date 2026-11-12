<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'

// Design exploration (prototype): one variant per port (scripts/design-proxy.mjs).
// a–c swap the header and footer; d–h own the whole frame (`Shell`), so they
// can move the navigation anywhere — a sidebar, a rail, a dock.
const design = useDesignVariant()
useHead({ htmlAttrs: { 'data-design': design ?? undefined } })
</script>

<template>
  <UApp :tooltip="{ delayDuration: 0 }">
    <NuxtRouteAnnouncer />
    <AppSkipLink />
    <AppLoadingBar />

    <DesignDShell v-if="design === 'd'">
      <NuxtPage />
    </DesignDShell>
    <DesignEShell v-else-if="design === 'e'">
      <NuxtPage />
    </DesignEShell>
    <DesignFShell v-else-if="design === 'f'">
      <NuxtPage />
    </DesignFShell>
    <DesignGShell v-else-if="design === 'g'">
      <NuxtPage />
    </DesignGShell>
    <DesignHShell v-else-if="design === 'h'">
      <NuxtPage />
    </DesignHShell>

    <template v-else>
      <DesignAHeader v-if="design === 'a'" />
      <DesignBHeader v-else-if="design === 'b'" />
      <DesignCHeader v-else-if="design === 'c'" />
      <AppHeader v-else />

      <!-- `tabindex="-1"`: focusable by the skip link and after a navigation
           (#1616), never a tab stop. The focus ring is dropped because it would
           outline the whole page; the focus lands on the next Tab's target. -->
      <UMain
        :id="MAIN_CONTENT_ID"
        tabindex="-1"
        class="outline-none"
      >
        <NuxtPage />
      </UMain>

      <DesignAFooter v-if="design === 'a'" />
      <DesignBFooter v-else-if="design === 'b'" />
      <DesignCFooter v-else-if="design === 'c'" />
      <AppFooter v-else />
    </template>

    <DesignBadge v-if="design" />
  </UApp>
</template>
