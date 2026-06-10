<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'

defineProps<{
  collapsed?: boolean
}>()

const colorMode = useColorMode()
const { user, clear: clearSession } = useUserSession()

const name = computed(() => user.value?.name ?? 'Operator')

async function logout() {
  // Clear the server cookie, then drop the client session so `loggedIn` flips
  // before navigation and the global middleware lets `/login` render.
  await $fetch('/api/auth/logout', { method: 'POST' })
  await clearSession()
  await navigateTo('/login')
}

const items = computed<DropdownMenuItem[][]>(() => [
  [{
    type: 'label',
    label: name.value,
    avatar: { icon: 'i-lucide-user' },
  }],
  [{
    label: 'Appearance',
    icon: 'i-lucide-sun-moon',
    children: [{
      label: 'Light',
      icon: 'i-lucide-sun',
      type: 'checkbox',
      checked: colorMode.value === 'light',
      onSelect(e: Event) {
        e.preventDefault()
        colorMode.preference = 'light'
      },
    }, {
      label: 'Dark',
      icon: 'i-lucide-moon',
      type: 'checkbox',
      checked: colorMode.value === 'dark',
      onSelect(e: Event) {
        e.preventDefault()
        colorMode.preference = 'dark'
      },
    }],
  }],
  [{
    label: 'Logout',
    icon: 'i-lucide-log-out',
    onSelect: () => logout(),
  }],
])
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{ content: collapsed ? 'w-48' : 'w-(--reka-dropdown-menu-trigger-width)' }"
  >
    <UButton
      :avatar="{ icon: 'i-lucide-user' }"
      :label="collapsed ? undefined : name"
      :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
      color="neutral"
      variant="ghost"
      block
      :square="collapsed"
      class="data-[state=open]:bg-elevated"
      :ui="{ trailingIcon: 'text-dimmed' }"
    />
  </UDropdownMenu>
</template>
