// Design exploration (prototype): one `nuxt dev` on HUB_PORT serves every
// variant; each variant gets its own port here, which proxies to the hub and
// tags the request with `x-tm-design`, read by `useDesignVariant()` during SSR.
// HTTP and WebSocket upgrades (Vite HMR) are both forwarded.
import http from 'node:http'
import net from 'node:net'

const HUB_PORT = Number(process.env.HUB_PORT ?? 3100)
const ROUTES = { 3101: 'a', 3102: 'b', 3104: 'd', 3105: 'e', 3106: 'f', 3107: 'g', 3108: 'h' }

for (const [port, design] of Object.entries(ROUTES)) {
  const server = http.createServer((req, res) => {
    const upstream = http.request({
      host: 'localhost',
      port: HUB_PORT,
      method: req.method,
      path: req.url,
      headers: { ...req.headers, 'x-tm-design': design },
    }, (up) => {
      res.writeHead(up.statusCode ?? 502, up.headers)
      up.pipe(res)
    })
    upstream.on('error', () => {
      if (!res.headersSent) res.writeHead(502, { 'content-type': 'text/plain' })
      res.end('design hub not ready')
    })
    req.pipe(upstream)
  })

  server.on('upgrade', (req, socket, head) => {
    const upstream = net.connect(HUB_PORT, 'localhost', () => {
      const lines = [`${req.method} ${req.url} HTTP/${req.httpVersion}`]
      for (let i = 0; i < req.rawHeaders.length; i += 2) lines.push(`${req.rawHeaders[i]}: ${req.rawHeaders[i + 1]}`)
      lines.push(`x-tm-design: ${design}`)
      upstream.write(`${lines.join('\r\n')}\r\n\r\n`)
      if (head?.length) upstream.write(head)
      socket.pipe(upstream).pipe(socket)
    })
    upstream.on('error', () => socket.destroy())
    socket.on('error', () => upstream.destroy())
  })

  server.listen(Number(port), () => console.log(`[design-proxy] :${port} → :${HUB_PORT} (variant ${design})`))
}
