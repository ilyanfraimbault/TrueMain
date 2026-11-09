import { defineEventHandler } from 'h3'

export default defineEventHandler((event) => {
  const { apiBaseUrl } = useRuntimeConfig(event)
  return proxyToBackend(event, { mount: '/api', baseUrl: apiBaseUrl, logCategory: 'api-proxy' })
})
