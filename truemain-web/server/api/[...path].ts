import { createError, defineEventHandler, getRequestURL, proxyRequest } from 'h3'

const absoluteUrlPattern = /^[A-Za-z][A-Za-z\d+.-]*:/

export default defineEventHandler(async (event) => {
  const runtimeConfig = useRuntimeConfig(event)
  const apiBaseUrl = String(runtimeConfig.apiBaseUrl || '').replace(/\/+$/, '')
  if (!apiBaseUrl) {
    throw createError({
      statusCode: 500,
      statusMessage: 'NUXT_API_BASE_URL is not configured.'
    })
  }

  const requestUrl = getRequestURL(event)
  const proxiedPath = requestUrl.pathname.replace(/^\/api\/?/, '')
  const normalizedProxiedPath = proxiedPath.replace(/^\/+/, '')

  if (isUnsafeProxyPath(proxiedPath) || isUnsafeProxyPath(normalizedProxiedPath)) {
    throw createError({
      statusCode: 400,
      statusMessage: 'Proxy path must stay relative to the configured API base URL.'
    })
  }

  let target: URL

  try {
    target = new URL(apiBaseUrl)
  }
  catch {
    throw createError({
      statusCode: 500,
      statusMessage: `NUXT_API_BASE_URL is invalid: ${apiBaseUrl}`
    })
  }

  if (!['http:', 'https:'].includes(target.protocol)) {
    throw createError({
      statusCode: 500,
      statusMessage: `NUXT_API_BASE_URL must use http or https: ${apiBaseUrl}`
    })
  }

  target.pathname = appendRelativePath(target.pathname, normalizedProxiedPath)
  target.search = requestUrl.search

  return proxyRequest(event, target.toString())
})

function isUnsafeProxyPath(value: string): boolean {
  if (!value) {
    return false
  }

  if (value.startsWith('//') || absoluteUrlPattern.test(value)) {
    return true
  }

  return value
    .split('/')
    .some(segment => segment === '.' || segment === '..')
}

function appendRelativePath(basePath: string, relativePath: string): string {
  const normalizedBasePath = basePath.replace(/\/+$/, '')
  if (!relativePath) {
    return normalizedBasePath || '/'
  }

  return `${normalizedBasePath}/${relativePath}`.replace(/\/{2,}/g, '/')
}
