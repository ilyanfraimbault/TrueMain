<script setup lang="ts">
/**
 * Share affordance for the pages that carry a dynamic OG card (#926):
 * champion pages and truemain profiles.
 *
 * Three controls, in decreasing order of how often they are the right one:
 *   - **Copy link** — the universal one. Pasting the URL into Discord (or
 *     anywhere else that unfurls) is what produces the stats card, so this is
 *     also the "Discord button" the issue asks for; Discord has no share
 *     intent URL to link to.
 *   - **Share** — the native sheet, rendered only where `navigator.share`
 *     exists (essentially mobile). Mount-gated, since its availability is a
 *     client fact and branching on it during SSR is a hydration mismatch.
 *   - **X** — a plain intent link. Kept as an anchor rather than a scripted
 *     popup so it survives middle-click and "open in new tab".
 *
 * The URL is rebuilt from the live route rather than captured once: every
 * filter on these pages is URL-backed, so the shared link must carry whatever
 * slice the user is actually looking at — which is also the slice the OG card
 * renders, since it reads the same query params.
 */
const props = withDefaults(defineProps<{
  /** What is being shared, used as the native sheet's title and the X post text. */
  title: string
  /** Longer blurb for the native share sheet. Falls back to the title. */
  description?: string
}>(), {
  description: undefined,
})

const route = useRoute()
const requestUrl = useRequestURL()
const toast = useToast()

// `useRequestURL()` gives the request origin on the server and
// `window.location` on the client, so the shared link always points at the host
// the visitor is actually on (prod, preprod or a local dev server) instead of
// the canonical site URL baked into the site config.
const shareUrl = computed(() => new URL(route.fullPath, requestUrl.origin).toString())

const xIntentUrl = computed(() => {
  const params = new URLSearchParams({ url: shareUrl.value, text: props.title })
  return `https://x.com/intent/post?${params.toString()}`
})

const mounted = ref(false)
onMounted(() => {
  mounted.value = true
})
const canNativeShare = computed(() =>
  mounted.value && typeof navigator !== 'undefined' && typeof navigator.share === 'function',
)

// Short-lived "Copied" state on the button itself; the toast is the
// announcement, this is the in-place confirmation.
const copied = ref(false)
let copiedTimer: ReturnType<typeof setTimeout> | undefined

onBeforeUnmount(() => {
  if (copiedTimer) clearTimeout(copiedTimer)
})

/**
 * `navigator.clipboard` only exists in secure contexts. Production is HTTPS and
 * `localhost` counts as secure, but the preprod stack is reachable over plain
 * HTTP on an IP, where the modern API is simply undefined — hence the legacy
 * `execCommand` path, which is the difference between "copy works everywhere"
 * and "copy silently fails on the environment we test on".
 */
async function writeToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    }
    catch {
      // Fall through: a denied permission still has the legacy path left.
    }
  }

  try {
    const textarea = document.createElement('textarea')
    textarea.value = text
    // Keep it out of the layout and off screen readers, but still focusable —
    // `display: none` would make the selection (and therefore the copy) fail.
    textarea.setAttribute('readonly', '')
    textarea.setAttribute('aria-hidden', 'true')
    textarea.style.position = 'fixed'
    textarea.style.opacity = '0'
    document.body.appendChild(textarea)
    textarea.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(textarea)
    return ok
  }
  catch {
    return false
  }
}

async function copyLink() {
  const ok = await writeToClipboard(shareUrl.value)
  if (!ok) {
    toast.add({
      title: 'Could not copy the link',
      description: 'Your browser blocked clipboard access — copy it from the address bar instead.',
      color: 'error',
      icon: 'i-lucide-circle-alert',
    })
    return
  }

  copied.value = true
  if (copiedTimer) clearTimeout(copiedTimer)
  copiedTimer = setTimeout(() => {
    copied.value = false
  }, 2000)

  toast.add({
    title: 'Link copied',
    description: 'Paste it in Discord or X — it unfurls with a stats card.',
    color: 'success',
    icon: 'i-lucide-link',
  })
}

async function nativeShare() {
  try {
    await navigator.share({
      title: props.title,
      text: props.description ?? props.title,
      url: shareUrl.value,
    })
  }
  catch {
    // `AbortError` is the overwhelmingly common case here — the user dismissed
    // the sheet, which is not a failure worth reporting. A genuine share
    // failure is indistinguishable from it without sniffing error names, and
    // the copy button is right there as a fallback either way.
  }
}
</script>

<template>
  <div class="flex shrink-0 items-center gap-2">
    <UButton
      :icon="copied ? 'i-lucide-check' : 'i-lucide-link'"
      :label="copied ? 'Copied' : 'Copy link'"
      color="neutral"
      variant="subtle"
      size="sm"
      @click="copyLink"
    />

    <UButton
      v-if="canNativeShare"
      icon="i-lucide-share-2"
      label="Share"
      color="neutral"
      variant="subtle"
      size="sm"
      @click="nativeShare"
    />

    <!-- Icon-only: the glyph *is* the wordmark, so a "X" label beside it reads
         as a duplicate rather than a caption. -->
    <UButton
      :to="xIntentUrl"
      target="_blank"
      rel="noopener noreferrer"
      color="neutral"
      variant="subtle"
      size="sm"
      aria-label="Share on X"
      title="Share on X"
    >
      <!-- Inline mark rather than an icon-collection lookup: the app only ships
           the `lucide` set, which has no brand glyphs, and one 20-line path is
           cheaper than pulling a second collection in for a single icon. -->
      <svg
        viewBox="0 0 24 24"
        class="size-4 shrink-0"
        fill="currentColor"
        aria-hidden="true"
      >
        <path d="M18.244 2.25h3.308l-7.227 8.26 8.502 11.24H16.17l-5.214-6.817L4.99 21.75H1.68l7.73-8.835L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z" />
      </svg>
    </UButton>
  </div>
</template>
