import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { pageTitle, verdictFavicon, verdictTitle } from '~/utils/page-title'

/**
 * Puts the pipeline-health verdict in the browser tab (#2296): `● Red — Health`
 * and a favicon with a coloured dot while it is not green, so a background
 * `/health` or Overview tab still raises its hand after the next 30 s refresh.
 *
 * Fed by the payload the page already draws — no request of its own. The head
 * entry is the page's: it overrides the app-wide title and icon set in `app.vue`
 * while mounted, and leaving the page disposes it, which restores both.
 */
export function useVerdictTitle(status: MaybeRefOrGetter<string | null | undefined>) {
  const route = useRoute()
  useHead({
    title: computed(() => verdictTitle(pageTitle(route.path, route.query) ?? '', toValue(status))),
    link: computed(() => [{ key: 'icon', rel: 'icon', ...verdictFavicon(toValue(status)) }]),
  })
}
