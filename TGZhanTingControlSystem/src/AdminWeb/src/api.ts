export type AssetKind = 0 | 1 | 2 | 3

export interface ContentAsset { id: string; name: string; kind: AssetKind; url: string; sha256: string; sizeBytes: number; durationSeconds: number }
export interface NarrationNode { id: string; name: string; order: number; narrationText: string; ttsAudioUrl: string | null; assets: ContentAsset[]; failurePolicy: 0 | 1 }
export interface ExhibitionModule { id: string; name: string; order: number; description: string; coverUrl: string | null; enabled: boolean; nodes: NarrationNode[] }
export interface PublishedContent { version: number; publishedAtUtc: string; publishedBy: string; modules: ExhibitionModule[] }
export interface LoginResult { token: string; username: string; expiresAtUtc: string }
export interface TtsStatus { provider: string; voice: string; configured: boolean }
export interface TtsResult { audioUrl: string; durationSeconds: number; providerRequestId: string }
export interface ClientRuntimeStatus { clientId: string; kind: number; appVersion: string; registeredAtUtc: string; lastSeenUtc: string; online: boolean }
export interface PlaybackSessionStatus { sessionId: string; moduleName: string; nodeName: string; currentNodeNumber: number; totalNodes: number; paused: boolean; playPublished: boolean }

const configuredBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '')
const apiBase = configuredBase ?? window.location.origin
const tokenKey = 'tg-admin-token'

async function request<T>(path: string, init?: RequestInit, authenticated = false): Promise<T> {
  const token = sessionStorage.getItem(tokenKey)
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(authenticated && token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  })
  if (!response.ok) {
    if (response.status === 401 && authenticated) sessionStorage.removeItem(tokenKey)
    const text = await response.text()
    let message = text
    try {
      const problem = JSON.parse(text)
      const validation = problem.errors ? Object.values(problem.errors).flat().join('；') : ''
      message = problem.message ?? problem.detail ?? validation ?? text
    } catch { /* keep the original response text */ }
    throw new Error(message || `请求失败：${response.status}`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const api = {
  hasSession: () => Boolean(sessionStorage.getItem(tokenKey)),
  login: async (username: string, password: string) => {
    const result = await request<LoginResult>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
    sessionStorage.setItem(tokenKey, result.token)
    return result
  },
  me: () => request<{ username: string }>('/api/auth/me', undefined, true),
  logout: async () => { try { await request<void>('/api/auth/logout', { method: 'POST' }, true) } finally { sessionStorage.removeItem(tokenKey) } },
  getContent: () => request<PublishedContent>('/api/content/current'),
  publish: (modules: ExhibitionModule[]) => request<PublishedContent>('/api/content/publish', { method: 'POST', body: JSON.stringify({ modules }) }, true),
  ttsStatus: () => request<TtsStatus>('/api/tts/status', undefined, true),
  clientStatuses: () => request<ClientRuntimeStatus[]>('/api/clients/status', undefined, true),
  playbackSessions: () => request<PlaybackSessionStatus[]>('/api/playback/sessions', undefined, true),
  synthesize: (text: string, voice: string) => request<TtsResult>('/api/tts/synthesize', { method: 'POST', body: JSON.stringify({ text, voice, rate: 1, volume: 1, pitch: 0 }) }, true),
  uploadAsset: (file: File, kind: AssetKind, durationSeconds: number, onProgress: (percent: number) => void) => new Promise<ContentAsset>((resolve, reject) => {
    const token = sessionStorage.getItem(tokenKey)
    if (!token) return reject(new Error('登录已过期，请重新登录。'))
    const request = new XMLHttpRequest()
    request.open('POST', `${apiBase}/api/assets/upload`)
    request.setRequestHeader('Authorization', `Bearer ${token}`)
    request.setRequestHeader('Content-Type', file.type || 'application/octet-stream')
    request.setRequestHeader('X-File-Name', encodeURIComponent(file.name))
    request.setRequestHeader('X-Asset-Kind', String(kind))
    request.setRequestHeader('X-Duration-Seconds', String(Math.max(0, durationSeconds)))
    request.upload.onprogress = event => { if (event.lengthComputable) onProgress(Math.round(event.loaded / event.total * 100)) }
    request.onerror = () => reject(new Error('素材上传网络错误。'))
    request.onload = () => {
      if (request.status === 401) sessionStorage.removeItem(tokenKey)
      if (request.status >= 200 && request.status < 300) resolve(JSON.parse(request.responseText) as ContentAsset)
      else {
        let message = request.responseText || `上传失败：${request.status}`
        try { message = JSON.parse(request.responseText).message ?? message } catch { /* keep response */ }
        reject(new Error(message))
      }
    }
    request.send(file)
  }),
}
