import { createError, defineEventHandler, proxyRequest } from 'h3'

export default defineEventHandler((event) => {
  const { apiBaseUrl } = useRuntimeConfig(event)

  // Validate the configured base URL up front so a misconfigured env var
  // surfaces as a clear 500 instead of letting `proxyRequest` fail downstream
  // on a string like `undefined/champions`.
  let base: URL
  try {
    base = new URL(apiBaseUrl)
  }
  catch {
    throw createError({ statusCode: 500, statusMessage: 'apiBaseUrl misconfigured' })
  }
  if (base.protocol !== 'http:' && base.protocol !== 'https:') {
    throw createError({ statusCode: 500, statusMessage: 'apiBaseUrl must be http(s)' })
  }

  // Reject paths that could escape the configured backend:
  //   `..` segments  → could walk above `base.pathname`
  //   `//host/…`     → protocol-relative, points at a different host
  //   `scheme://…`   → absolute URL, same problem
  const path = event.path.replace(/^\/api/, '')
  const isUnsafe
    = /(^|\/)\.\.(\/|$)/.test(path)
      || /^\/\//.test(path)
      || /^\/?[a-z][a-z0-9+.-]*:\/\//i.test(path)
  if (isUnsafe) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }

  return proxyRequest(event, `${apiBaseUrl}${path}`)
})
