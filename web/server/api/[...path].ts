import { defineEventHandler, proxyRequest } from 'h3'

export default defineEventHandler((event) => {
  const { apiBaseUrl } = useRuntimeConfig(event)
  const path = event.path.replace(/^\/api/, '')
  return proxyRequest(event, `${apiBaseUrl}${path}`)
})
