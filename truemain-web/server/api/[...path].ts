import { defineEventHandler, getRequestURL, getRouterParam, proxyRequest } from 'h3'

export default defineEventHandler(async (event) => {
  const runtimeConfig = useRuntimeConfig(event)
  const apiBaseUrl = String(runtimeConfig.apiBaseUrl || '').replace(/\/+$/, '')
  const proxiedPath = getRouterParam(event, 'path') || ''
  const requestUrl = getRequestURL(event)
  const target = `${apiBaseUrl}/${proxiedPath}${requestUrl.search}`

  return proxyRequest(event, target)
})
