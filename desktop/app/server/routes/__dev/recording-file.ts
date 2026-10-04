import { spawn } from 'node:child_process'
import { createReadStream, statSync } from 'node:fs'

/**
 * The dev fixtures' video and thumbnails, in `npm run dev` only (#1755). The
 * packaged app loads a recording's files through its `recording` scheme
 * (`convertFileSrc(path, 'recording')`); a browser has no such thing, so
 * `useRecordings().fileSrc` points here instead with the fixture's path.
 *
 * Every video path answers with the one local file named by
 * `TRUEMAIN_DEV_RECORDING` (any MP4 — never commit one), honouring HTTP Range
 * so the player can seek. A `.jpg` path answers with a frame of that file
 * grabbed by `ffmpeg` when it is installed, at a point picked from the path so
 * the cards differ. Without the variable, or without `ffmpeg`, a 404: the
 * pages then show their placeholders.
 */
const frames = new Map<string, Buffer>()

function frameOf(file: string, path: string): Promise<Buffer | null> {
  const cached = frames.get(path)
  if (cached) return Promise.resolve(cached)
  let hash = 0
  for (const char of path) hash = (hash * 31 + char.charCodeAt(0)) | 0
  const at = Math.abs(hash) % 100
  return new Promise((resolve) => {
    const chunks: Buffer[] = []
    const ffmpeg = spawn('ffmpeg', ['-v', 'error', '-ss', String(at), '-i', file, '-frames:v', '1', '-vf', 'scale=480:-2', '-f', 'image2pipe', '-vcodec', 'mjpeg', '-'])
    ffmpeg.stdout.on('data', (chunk: Buffer) => chunks.push(chunk))
    ffmpeg.on('error', () => resolve(null))
    ffmpeg.on('close', (code) => {
      const frame = code === 0 && chunks.length ? Buffer.concat(chunks) : null
      if (frame) frames.set(path, frame)
      resolve(frame)
    })
  })
}

export default defineEventHandler(async (event) => {
  // A static build has no server; this is here only so it can never answer outside `npm run dev`.
  if (!import.meta.dev) throw createError({ statusCode: 404 })

  const file = process.env.TRUEMAIN_DEV_RECORDING
  const path = String(getQuery(event).path ?? '')
  if (!file) throw createError({ statusCode: 404, statusMessage: 'Set TRUEMAIN_DEV_RECORDING to a local video' })

  let size: number
  try {
    size = statSync(file).size
  }
  catch {
    throw createError({ statusCode: 404, statusMessage: 'TRUEMAIN_DEV_RECORDING is not a readable file' })
  }

  if (path.endsWith('.jpg')) {
    const frame = await frameOf(file, path)
    if (!frame) throw createError({ statusCode: 404 })
    setResponseHeader(event, 'Content-Type', 'image/jpeg')
    return frame
  }

  setResponseHeaders(event, { 'Content-Type': 'video/mp4', 'Accept-Ranges': 'bytes' })
  const range = /^bytes=(\d*)-(\d*)$/.exec(getRequestHeader(event, 'range') ?? '')
  if (!range) {
    setResponseHeader(event, 'Content-Length', size)
    return sendStream(event, createReadStream(file))
  }

  const start = range[1] ? Number(range[1]) : Math.max(0, size - Number(range[2]))
  const end = range[1] && range[2] ? Math.min(Number(range[2]), size - 1) : size - 1
  if (start >= size || start > end) {
    setResponseHeader(event, 'Content-Range', `bytes */${size}`)
    throw createError({ statusCode: 416 })
  }
  setResponseStatus(event, 206)
  setResponseHeaders(event, { 'Content-Range': `bytes ${start}-${end}/${size}`, 'Content-Length': end - start + 1 })
  return sendStream(event, createReadStream(file, { start, end }))
})
