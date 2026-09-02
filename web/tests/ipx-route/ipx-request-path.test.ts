// @vitest-environment node
// The default happy-dom environment swaps in its own `Response`, which cannot read
// the body of the native one IPX returns. Nothing here touches the DOM.
import { Buffer } from 'node:buffer'
import { createIPX, createIPXFetchHandler, ipxFSStorage } from 'ipx'
import { describe, expect, it } from 'vitest'
import { ipxRequestPath } from '~~/shared/utils/ipx'

// The @nuxt/image 2.1.0 bump brought ipx 4, whose h3-native handler is gone; the
// replacement is a fetch handler, and the h3 `useBase` wrapper that was supposed to
// strip `/_ipx` for it does not reach it — `toWebRequest` returns Nitro's original
// request, prefix and all. IPX then read `_ipx` as the modifiers segment, was left
// with an id that no longer looks like a URL, routed it to the filesystem storage
// and answered 403 for every image on the site. These run the real IPX handler, so
// the assertion is about what IPX does with the path, not about string slicing.

describe('ipxRequestPath', () => {
  it('removes the route prefix', () => {
    expect(ipxRequestPath('/_ipx/f_webp,s_64x64/https://cdn.example/a.png'))
      .toBe('/f_webp,s_64x64/https://cdn.example/a.png')
  })

  it('keeps the embedded source URL intact', () => {
    // The `//` of the source must survive: it is what tells IPX to use the HTTP
    // storage rather than the filesystem one.
    expect(ipxRequestPath('/_ipx/s_64/https://cdn.example/a.png')).toContain('https://cdn.example/a.png')
  })

  it('leaves an already-stripped path alone', () => {
    expect(ipxRequestPath('/f_webp/a.png')).toBe('/f_webp/a.png')
  })
})

describe('the real IPX handler, given a prefixed route path', () => {
  const ipx = createIPX({ storage: ipxFSStorage({ dir: 'public' }) })
  const handler = createIPXFetchHandler(ipx)

  // A bundled asset, so this stays offline and deterministic.
  const routePath = '/_ipx/s_16x16/positions/icon-position-jungle.png'

  it('serves the image once the prefix is removed', async () => {
    const response = await handler(new Request(`http://ipx.invalid${ipxRequestPath(routePath)}`))

    expect(response.status).toBe(200)
    expect(response.headers.get('content-type')).toContain('image/')
    expect(Buffer.from(await response.arrayBuffer()).byteLength).toBeGreaterThan(0)
  })

  it('resolves the wrong asset when the prefix is left in — the production failure', async () => {
    const response = await handler(new Request(`http://ipx.invalid${routePath}`))

    // IPX consumed `_ipx` as the modifiers segment, so the id it looked up was
    // `/s_16x16/positions/icon-position-jungle.png` — a path that does not exist.
    // Production saw the same mechanism against a remote source: the leftover id no
    // longer looked like a URL, so it went to the filesystem storage too, and came
    // back 403 IPX_FORBIDDEN_PATH rather than 404. Either way the asset is never
    // served, which is the property worth pinning.
    expect(response.status).not.toBe(200)
    await expect(response.text()).resolves.toContain('s_16x16/positions/icon-position-jungle.png')
  })
})
