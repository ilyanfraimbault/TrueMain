import type { PipelineCatalogReadModel } from '#site-shared/contract/api'

let loading: Promise<void> | null = null

/**
 * The ingestor's pipeline as the API declares it (`GET /ops/pipeline`, #2488): its steps in
 * the order a full pass runs them, with their labels and descriptions, and the lanes they
 * partition into. Read once per session — it changes only with a deploy — and shared by every
 * component that draws a step, so a step added to `PipelineCatalog` shows in place, under its
 * label, without an admin change. `null` until it has loaded, and if it could not be read: the
 * chain then draws each run under its raw name rather than guessing an order.
 */
export function usePipelineCatalog() {
  const catalog = useState<PipelineCatalogReadModel | null>('pipeline-catalog', () => null)
  if (catalog.value === null && loading === null) {
    loading = opsCall('get', '/pipeline')
      .then((answer) => {
        catalog.value = answer
      })
      .catch(() => {
        // Left null; the next component that asks tries again.
        loading = null
      })
  }
  return catalog
}
