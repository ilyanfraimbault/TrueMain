// @vitest-environment node
import { Buffer } from 'node:buffer'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const store = { get: vi.fn(), set: vi.fn(), sweep: vi.fn() }

vi.mock('~~/server/utils/html-cache', async (original) => ({
  ...await original<typeof import('~~/server/utils/html-cache')>(),
  getHtmlStore: () => store,
}))

const { HTML_FRESH_MS, HTML_RENDER_HEADER, HTML_RENDER_KEY, HTML_STALE_MS } = await import('~~/server/utils/html-cache')

const localFetch = vi.fn(async () => new Response('rendered'))
const setResponseHeaders = vi.fn()
const setResponseHeader = vi.fn()
const config = { htmlCacheEnabled: true }
let requestHeaders: Record<string, string> = {}
let requestHost = 'truemain.lol'
let responseHeaders: Record<string, string> = {}

vi.stubGlobal('defineEventHandler', (handler: unknown) => handler)
vi.stubGlobal('defineNitroPlugin', (plugin: unknown) => plugin)
vi.stubGlobal('useRuntimeConfig', () => config)
vi.stubGlobal('getRequestHeader', (_event: unknown, name: string) => requestHeaders[name])
vi.stubGlobal('getRequestHost', () => requestHost)
vi.stubGlobal('getResponseHeaders', () => responseHeaders)
vi.stubGlobal('setResponseHeaders', setResponseHeaders)
vi.stubGlobal('setResponseHeader', setResponseHeader)
vi.stubGlobal('useNitroApp', () => ({ localFetch }))

const middleware = (await import('~~/server/middleware/html-cache')).default as unknown as
  (event: { method: string, path: string }) => Promise<unknown>
const plugin = (await import('~~/server/plugins/html-cache')).default as unknown as
  (app: { hooks: { hook: (name: string, listener: unknown) => void } }) => void

type RenderListener = (response: { statusCode: number, body: unknown, headers: Record<string, string> }, context: { event: unknown }) => void

function renderListener(): RenderListener {
  let listener: RenderListener | undefined
  plugin({ hooks: { hook: (_name, registered) => { listener = registered as RenderListener } } })
  return listener!
}

const page = (ageMs: number) => ({
  storedAt: Date.now() - ageMs,
  headers: { 'content-type': 'text/html', 'x-robots-tag': 'index' },
  body: Buffer.from('<html>'),
})

beforeEach(() => {
  store.get.mockReset()
  store.set.mockReset()
  localFetch.mockClear()
  setResponseHeaders.mockClear()
  setResponseHeader.mockClear()
  config.htmlCacheEnabled = true
  requestHeaders = {}
  requestHost = 'truemain.lol'
  responseHeaders = {}
  vi.stubEnv('NUXT_PUBLIC_SITE_URL', 'https://truemain.lol')
})

describe('answering from the cache', () => {
  it('answers a fresh page with its stored headers, without rendering', async () => {
    store.get.mockResolvedValue(page(0))

    const body = await middleware({ method: 'GET', path: '/champions' })

    expect(body).toEqual(Buffer.from('<html>'))
    expect(setResponseHeaders).toHaveBeenCalledWith(expect.anything(), { 'content-type': 'text/html', 'x-robots-tag': 'index' })
    expect(setResponseHeader).toHaveBeenCalledWith(expect.anything(), 'x-html-cache', 'fresh')
    expect(localFetch).not.toHaveBeenCalled()
  })

  it('answers a stale page at once and renders it again behind the visitor, once', async () => {
    store.get.mockResolvedValue(page(HTML_FRESH_MS + 1))
    let finishRender: (response: Response) => void = () => {}
    localFetch.mockImplementationOnce(() => new Promise<Response>((resolve) => {
      finishRender = resolve
    }))

    expect(await middleware({ method: 'GET', path: '/champions' })).toEqual(Buffer.from('<html>'))
    expect(await middleware({ method: 'GET', path: '/champions' })).toEqual(Buffer.from('<html>'))

    expect(localFetch).toHaveBeenCalledOnce()
    expect(localFetch).toHaveBeenCalledWith('/champions', {
      headers: { [HTML_RENDER_HEADER]: HTML_RENDER_KEY, 'host': 'truemain.lol', 'x-forwarded-proto': 'https' },
    })
    finishRender(new Response('rendered'))
  })

  it('lets an expired page render on the request', async () => {
    store.get.mockResolvedValue(page(HTML_STALE_MS + 1))

    expect(await middleware({ method: 'GET', path: '/champions' })).toBeUndefined()
  })

  it.each([
    ['a page nobody stored', { method: 'GET', path: '/champions/ahri' }, undefined],
    ['a request other than a read', { method: 'POST', path: '/champions' }, page(0)],
    ['a path outside the cache', { method: 'GET', path: '/about' }, page(0)],
  ])('renders %s', async (_name, event, stored) => {
    store.get.mockResolvedValue(stored ?? null)

    expect(await middleware(event)).toBeUndefined()
  })

  it('renders when asked to by this process', async () => {
    store.get.mockResolvedValue(page(0))
    requestHeaders = { [HTML_RENDER_HEADER]: HTML_RENDER_KEY }

    expect(await middleware({ method: 'GET', path: '/champions' })).toBeUndefined()
  })

  it('ignores a wrong render key: a visitor cannot make every request a render', async () => {
    store.get.mockResolvedValue(page(0))
    requestHeaders = { [HTML_RENDER_HEADER]: 'guess' }

    expect(await middleware({ method: 'GET', path: '/champions' })).toEqual(Buffer.from('<html>'))
  })

  it('does nothing when switched off', async () => {
    config.htmlCacheEnabled = false
    store.get.mockResolvedValue(page(0))

    expect(await middleware({ method: 'GET', path: '/champions' })).toBeUndefined()
  })
})

describe('storing a render', () => {
  const event = (overrides: object = {}) => ({ method: 'GET', path: '/champions', context: {}, ...overrides })
  const response = (overrides: object = {}) => ({ statusCode: 200, body: '<html>', headers: { 'content-type': 'text/html' }, ...overrides })

  it('stores a good render with its headers', () => {
    responseHeaders = { 'x-robots-tag': 'index' }

    renderListener()(response(), { event: event() })

    expect(store.set).toHaveBeenCalledWith('/champions', expect.objectContaining({
      headers: { 'x-robots-tag': 'index', 'content-type': 'text/html' },
      body: Buffer.from('<html>'),
    }))
  })

  it('refuses a render whose fetch failed', () => {
    renderListener()(response(), { event: event({ context: { renderFailed: true } }) })

    expect(store.set).not.toHaveBeenCalled()
  })

  it('refuses an error page', () => {
    renderListener()(response({ statusCode: 404 }), { event: event() })

    expect(store.set).not.toHaveBeenCalled()
  })

  it('refuses a request forging another host', () => {
    requestHost = 'evil.example'

    renderListener()(response(), { event: event() })

    expect(store.set).not.toHaveBeenCalled()
  })

  it('refuses a page that set a cookie', () => {
    responseHeaders = { 'set-cookie': 'a=1' }

    renderListener()(response(), { event: event() })

    expect(store.set).not.toHaveBeenCalled()
  })

  it('refuses a path outside the cache', () => {
    renderListener()(response(), { event: event({ path: '/about' }) })

    expect(store.set).not.toHaveBeenCalled()
  })
})
