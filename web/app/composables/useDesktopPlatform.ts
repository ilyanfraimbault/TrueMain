import type { DesktopPlatform } from '~~/shared/types/desktop'

/** What each installer runs on, as `/download` offers them. */
export const DESKTOP_PLATFORMS: Record<DesktopPlatform, { label: string, icon: string, requirement: string }> = {
  mac: { label: 'macOS', icon: 'i-simple-icons-apple', requirement: 'macOS 13.3 or later, Apple Silicon or Intel' },
  windows: { label: 'Windows', icon: 'i-simple-icons-windows', requirement: 'Windows 10 or 11, 64-bit' },
}

/**
 * The visitor's platform, read in the browser after hydration (#1719): the
 * server cannot know it, and guessing from the user agent there would cache one
 * visitor's answer for the next. Null until then — and on a phone, or Linux —
 * so both installers are offered side by side.
 */
export function useDesktopPlatform() {
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

  return { platform, other, onPhone }
}
