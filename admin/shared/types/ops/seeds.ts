// Account seed requests — `/api/ops/accounts/seed`.

import type { SeedRequestStatus } from '#site-shared/contract/api'

/** A status that will not change on its own — polling can stop. */
export const TERMINAL_SEED_STATUSES: readonly SeedRequestStatus[] = ['Ingested', 'Failed']
