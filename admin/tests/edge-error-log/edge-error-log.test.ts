import { connect, type AddressInfo, type Server } from 'node:net'
import { afterEach, describe, expect, it } from 'vitest'
import { createLineServer, edgeLogPort, normalizeEdgeError, toEdgeProxyError } from '~~/server/utils/edge-error-log'

// Captured from caddy:2-alpine 2.11 with the edge's `edge_errors` logger (field filter on),
// for a dial to a closed port and a dial that timed out.
const REFUSED = '{"level":"debug","ts":1792871357.77,"logger":"http.log.error.web","msg":"dial tcp 127.0.0.1:1: connect: connection refused","request":{"proto":"HTTP/1.1","method":"GET","host":"localhost:8098","uri":"/api/truemains/Faker-KR1/matches?page=2"},"duration":0.000474958,"status":502,"err_id":"jrs77w5ch","err_trace":"reverseproxy.statusError (reverseproxy.go:1695)"}'
const TIMEOUT = '{"level":"debug","ts":1792871238.61,"logger":"http.log.error.admin","msg":"dial tcp 10.255.255.1:3000: i/o timeout","request":{"method":"GET","host":"localhost:8099","uri":"/"},"duration":2.004700543,"status":502}'

describe('toEdgeProxyError', () => {
  it('turns a dial failure into an Edge report keyed on site, status and error', () => {
    expect(toEdgeProxyError(REFUSED)).toEqual({
      level: 'Error',
      category: 'edge',
      eventType: 'EdgeProxyError',
      message: 'The edge proxy could not serve a request to web (502): dial tcp 127.0.0.1:1: connect: connection refused',
      statusCode: 502,
      durationMs: 0,
    })
    expect(toEdgeProxyError(TIMEOUT)).toMatchObject({
      message: 'The edge proxy could not serve a request to admin (502): dial tcp 10.255.255.1:3000: i/o timeout',
      durationMs: 2005,
    })
  })

  it('reports the original failure when the error route failed too', () => {
    const line = '{"level":"error","logger":"http.log.error.admin","msg":"error handling handler error","request":{"method":"GET","host":"localhost:8099","uri":"/logs"},"duration":3.0036,"error":"{id=phm7i8nkb} fileserver.(*FileServer).notFound (staticfiles.go:787): HTTP 404","first_error":{"msg":"dial tcp 10.255.255.1:3000: i/o timeout","status":502,"err_id":"ctj4e66da"}}'
    expect(toEdgeProxyError(line)).toMatchObject({
      message: 'The edge proxy could not serve a request to admin (502): dial tcp 10.255.255.1:3000: i/o timeout',
      statusCode: 502,
      durationMs: 3004,
    })
  })

  it('keeps the request out of the report', () => {
    const report = JSON.stringify(toEdgeProxyError(REFUSED))
    expect(report).not.toContain('Faker')
    expect(report).not.toContain('localhost:8098')
  })

  it('names an unnamed site logger "default"', () => {
    expect(toEdgeProxyError('{"logger":"http.log.error","msg":"boom","status":503}')?.message)
      .toBe('The edge proxy could not serve a request to default (503): boom')
  })

  it.each([
    ['not JSON', 'dial tcp: i/o timeout'],
    ['not an object', '42'],
    ['null', 'null'],
    ['an access log line', '{"logger":"http.log.access.web","status":502}'],
    ['another logger', '{"logger":"tls","status":502}'],
    ['no logger', '{"status":502}'],
    ['a client abort', '{"logger":"http.log.error.web","msg":"context canceled","status":499}'],
    ['a 404', '{"logger":"http.log.error.web","status":404}'],
    ['no status', '{"logger":"http.log.error.web"}'],
    ['a non-integer status', '{"logger":"http.log.error.web","status":502.5}'],
  ])('ignores %s', (_, line) => {
    expect(toEdgeProxyError(line)).toBeNull()
  })

  it('tolerates a missing message and duration', () => {
    expect(toEdgeProxyError('{"logger":"http.log.error.web","status":502,"duration":-1}')).toMatchObject({
      message: 'The edge proxy could not serve a request to web (502): unknown error',
      durationMs: null,
    })
  })
})

describe('normalizeEdgeError', () => {
  it('drops the local end of a connection, whose ephemeral port would split identical errors', () => {
    expect(normalizeEdgeError('read tcp 172.18.0.4:51234->172.18.0.5:3000: read: connection reset by peer'))
      .toBe('read tcp 172.18.0.5:3000: read: connection reset by peer')
  })

  it('keeps the far end of a chained address and collapses the spaces it leaves', () => {
    expect(normalizeEdgeError('a 1.1.1.1:1->2.2.2.2:2->3.3.3.3:3  b')).toBe('a 3.3.3.3:3 b')
  })

  it('stays linear on a long run of non-space characters', () => {
    const started = performance.now()
    expect(normalizeEdgeError('x'.repeat(1_000_000) + '-')).toBe('x'.repeat(300))
    expect(performance.now() - started).toBeLessThan(500)
  })

  it('caps the error text', () => {
    expect(normalizeEdgeError('x'.repeat(1_000))).toHaveLength(300)
  })
})

describe('edgeLogPort', () => {
  it.each([
    ['5140', 5140],
    [' 5140 ', 5140],
    [5140, 5140],
    ['', null],
    [undefined, null],
    ['0', null],
    ['65536', null],
    ['51a', null],
    ['-1', null],
    [{ port: 5140 }, null],
    [null, null],
  ])('reads %j as %j', (value, expected) => {
    expect(edgeLogPort(value)).toBe(expected)
  })
})

describe('createLineServer', () => {
  let server: Server | null = null

  afterEach(async () => {
    await new Promise<void>(resolve => server ? server.close(() => resolve()) : resolve())
    server = null
  })

  async function send(chunks: string[], onLine: (line: string) => void, maxLineLength?: number): Promise<void> {
    server = createLineServer(onLine, { maxLineLength })
    await new Promise<void>(resolve => server!.listen(0, '127.0.0.1', resolve))
    const { port } = server.address() as AddressInfo
    const socket = connect(port, '127.0.0.1')
    await new Promise<void>(resolve => socket.once('connect', resolve))
    for (const chunk of chunks) {
      socket.write(chunk)
      await new Promise(resolve => setTimeout(resolve, 5))
    }
    await new Promise<void>(resolve => socket.end(resolve))
    await new Promise(resolve => setTimeout(resolve, 20))
  }

  it('splits the stream into lines, across chunk boundaries', async () => {
    const lines: string[] = []
    await send(['first\nsec', 'ond\n\nthird', '\n'], line => lines.push(line))
    expect(lines).toEqual(['first', 'second', 'third'])
  })

  it('drops a line longer than the limit, whether it arrives whole or in pieces', async () => {
    const lines: string[] = []
    await send(['short\n', 'y'.repeat(20) + '\n', 'z'.repeat(12), 'z'.repeat(12), 'zz\nafter\n'], line => lines.push(line), 16)
    expect(lines).toEqual(['short', 'after'])
  })

  it('keeps going when a line handler throws', async () => {
    const lines: string[] = []
    await send(['bad\ngood\n'], (line) => {
      if (line === 'bad') throw new Error('unreadable')
      lines.push(line)
    })
    expect(lines).toEqual(['good'])
  })
})
