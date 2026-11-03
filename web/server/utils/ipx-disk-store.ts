import { createIpxDiskCache, type IpxDiskCache } from './ipx-disk-cache'

// Far above the catalogue: a patch's icons and banners come to some tens of MB, and the
// oldest patch goes first. The disk is shared with everything else on the host (#680).
const MAX_BYTES = 512 * 1024 * 1024
const MAX_AGE_MS = 45 * 24 * 60 * 60 * 1000

let disk: IpxDiskCache | null | undefined

/** This worker's handle on the shared image directory, or null when none is configured. */
export function getIpxDiskCache(): IpxDiskCache | null {
  if (disk === undefined) {
    const dir = useRuntimeConfig().ipxCacheDir
    disk = dir ? createIpxDiskCache({ dir, maxBytes: MAX_BYTES, maxAgeMs: MAX_AGE_MS }) : null
  }
  return disk
}
