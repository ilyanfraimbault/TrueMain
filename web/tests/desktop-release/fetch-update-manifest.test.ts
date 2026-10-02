import { describe, expect, it } from 'vitest'
import { fetchUpdateManifest } from '~~/server/utils/desktop-manifest'

const manifest = { version: '0.2.1', platforms: { 'darwin-aarch64': { signature: 'sig', url: 'https://github.com/dl/truemain.app.tar.gz' } } }

// What GitHub answers for a release asset: the bytes, typed as a download rather than as JSON.
const github = (body: string, status = 200): typeof fetch =>
  async () => new Response(body, { status, headers: { 'content-type': 'application/octet-stream' } })

describe('fetchUpdateManifest', () => {
  it('parses a manifest GitHub serves as octet-stream', async () => {
    await expect(fetchUpdateManifest('https://github.com/dl/latest.json', github(JSON.stringify(manifest)))).resolves.toEqual(manifest)
  })

  it('fails on a missing asset rather than relaying its error page', async () => {
    await expect(fetchUpdateManifest('https://github.com/dl/latest.json', github('Not Found', 404))).rejects.toThrow('404')
  })
})
