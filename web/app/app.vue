<template>
  <UApp :tooltip="{ delayDuration: 0 }">
    <NuxtRouteAnnouncer />
    <AppHeader />

    <UMain>
      <NuxtPage />
    </UMain>

    <AppFooter />
  </UApp>
</template>
