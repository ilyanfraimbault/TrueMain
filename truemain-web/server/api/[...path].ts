import { createError, defineEventHandler, getRequestURL, getRouterParam, proxyRequest } from 'h3'

export default defineEventHandler(async (event) => {
  const runtimeConfig = useRuntimeConfig(event)
  const apiBaseUrl = String(runtimeConfig.apiBaseUrl || '').replace(/\/+$/, '')
  if (!apiBaseUrl) {
    throw createError({
      statusCode: 500,
      statusMessage: 'NUXT_API_BASE_URL is not configured.'
    })
  }

  const proxiedPath = (getRouterParam(event, 'path') || '').replace(/^\/+/, '')
  const requestUrl = getRequestURL(event)
  let target: URL

  try {
    target = new URL(`${proxiedPath}${requestUrl.search}`, `${apiBaseUrl}/`)
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

  return proxyRequest(event, target.toString())
})
