export type AssetKind = 0 | 1 | 2 | 3

export interface ContentAsset { id: string; name: string; kind: AssetKind; url: string; sha256: string; sizeBytes: number; durationSeconds: number; mediaType?: string | null }
export type AudioMixPolicy = 0 | 1 | 2
export interface TtsSynthesisConfiguration { providerKey: string; voice: string; language: string; rate: number; pitch: number; volume: number; outputMediaType: string; sampleRateHz: number; channels: number }
export interface NarrationAudioBinding { asset: ContentAsset; narrationTextFingerprint: string; synthesisConfigurationFingerprint: string; synthesisConfiguration: TtsSynthesisConfiguration; origin: 0 | 1 | 2; boundAtUtc: string; fingerprintVersion: string; providerRequestId: string | null }
export interface NarrationNode { id: string; name: string; order: number; narrationText: string; ttsAudioUrl: string | null; assets: ContentAsset[]; failurePolicy: 0 | 1; audioMixPolicy: AudioMixPolicy; videoVolume: number; narrationVolume: number; ttsConfiguration?: TtsSynthesisConfiguration | null; narrationAudio?: NarrationAudioBinding | null }
export interface ExhibitionModule { id: string; name: string; order: number; description: string; coverUrl: string | null; enabled: boolean; nodes: NarrationNode[] }
export interface PublishedContent { version: number; publishedAtUtc: string; publishedBy: string; modules: ExhibitionModule[] }
export interface LoginResult { token: string; username: string; expiresAtUtc: string }
export interface TtsStatus { provider: string; voice: string; configured: boolean }
export interface TtsResult { audioUrl: string; durationSeconds: number; providerRequestId: string }
export type NarrationAudioBindingStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6
export interface NarrationAudioDraftStatus { moduleId: string; nodeId: string; status: NarrationAudioBindingStatus; message: string }
export interface ContentPublishIssue { moduleId: string; nodeId: string; moduleName: string; nodeName: string; code: string; severity: 0 | 1; message: string; narrationAudioStatus: NarrationAudioBindingStatus | null }
export interface NarrationAudioPublishSummary { fresh: number; missing: number; staleText: number; staleSynthesisConfiguration: number; legacyUnverified: number; invalidAsset: number; invalidBinding: number; blockingIssues: number; warnings: number }
export interface ContentPublishReadiness { canPublish: boolean; narrationAudio: NarrationAudioPublishSummary; issues: ContentPublishIssue[] }
export interface ContentDraftSnapshot { baseContentVersion: number; revision: number; updatedAtUtc: string; updatedBy: string; modules: ExhibitionModule[]; narrationAudioStatuses: NarrationAudioDraftStatus[]; publishReadiness: ContentPublishReadiness | null }
export interface TtsVoiceDescriptor { voiceId: string; displayName: string; language: string }
export interface TtsProviderCapabilities { maxTextLength: number; minRate: number; maxRate: number; minPitch: number; maxPitch: number; supportedMediaTypes: string[] }
export interface TtsProviderDescriptor { providerId: string; displayName: string; available: boolean; developmentOnly: boolean; unavailableReason: string | null; voices: TtsVoiceDescriptor[]; capabilities: TtsProviderCapabilities }
export type TtsProductionJobStatus = 0 | 1 | 2 | 3 | 4
export interface TtsProductionJobAttempt { attemptNumber: number; startedAtUtc: string; completedAtUtc: string; succeeded: boolean; errorCategory: number | null; errorCode: string | null; errorMessage: string | null }
export interface TtsProductionJob { jobId: string; moduleId: string; nodeId: string; requestedBy: string; narrationText: string; narrationTextFingerprint: string; synthesisConfiguration: TtsSynthesisConfiguration; synthesisConfigurationFingerprint: string; idempotencyKey: string; providerId: string; voice: string; status: TtsProductionJobStatus; createdAtUtc: string; startedAtUtc: string | null; completedAtUtc: string | null; retryCount: number; attempts: TtsProductionJobAttempt[]; errorCategory: number | null; errorCode: string | null; errorMessage: string | null; candidateId: string | null }
export interface CreateTtsProductionJobResponse { job: TtsProductionJob; created: boolean }
export interface NarrationAudioCandidateValidation { valid: boolean; validator: string; mediaType: string; durationSeconds: number; validatedAtUtc: string }
export interface NarrationAudioCandidate { candidateId: string; jobId: string; asset: ContentAsset; narrationTextFingerprint: string; synthesisConfigurationFingerprint: string; synthesisConfiguration: TtsSynthesisConfiguration; providerId: string; voice: string; createdAtUtc: string; validation: NarrationAudioCandidateValidation; providerRequestId: string | null }
export interface NarrationAudioCandidateEvaluation { candidateId: string; baseContentVersion: number; draftRevision: number; candidateExists: boolean; locationMatches: boolean; narrationTextMatches: boolean; synthesisConfigurationMatches: boolean; assetValid: boolean; adoptable: boolean; message: string }
export interface AdoptNarrationAudioCandidateResponse { draft: ContentDraftSnapshot; binding: NarrationAudioBinding }
export interface ClientRuntimeStatus { clientId: string; kind: number; appVersion: string; registeredAtUtc: string; lastSeenUtc: string; online: boolean; contentVersion: number; ready: boolean; status: string | null }
export interface PlaybackSessionStatus { sessionId: string; contentVersion: number; moduleName: string; nodeName: string; currentNodeNumber: number; totalNodes: number; paused: boolean; playPublished: boolean; preparationProgress: number }
export interface NarrationRoute { id: string; name: string; moduleIds: string[]; updatedAtUtc: string }
export interface NarrationRouteCollection { routes: NarrationRoute[] }
export interface SystemReadiness { canStart: boolean; contentVersion: number; ledOnline: boolean; ledReady: boolean; ledContentVersion: number; message: string; checkedAtUtc: string }
export interface ContentVersionSummary { version: number; publishedAtUtc: string; publishedBy: string; moduleCount: number; nodeCount: number; current: boolean }
export interface OperationalEvent { id: string; occurredAtUtc: string; level: string; category: string; action: string; message: string; sessionId: string | null; detail: string | null }
export interface UiExperienceConfig { version: number; touchTitle: string; touchSubtitle: string; touchBackgroundUrl: string | null; touchBackgroundColor: string; touchAccentColor: string; ledTitle: string; ledSubtitle: string; ledIdleMediaUrl: string | null; ledIdleMediaKind: 'none'|'image'|'video'; ledBackgroundColor: string; ledShowBranding: boolean; ledShowStatus: boolean; updatedAtUtc: string; updatedBy: string }

const configuredBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '')
const apiBase = configuredBase ?? window.location.origin
const tokenKey = 'tg-admin-token'

export class ApiError extends Error {
  constructor(message: string, public readonly status: number, public readonly code: string | null = null) { super(message) }
}

const friendlyApiError = (code: string | null, fallback: string): string => {
  const messages: Record<string, string> = {
    content_version_conflict: '当前正式内容版本已发生变化，请刷新后重新确认。',
    draft_revision_conflict: '当前内容已发生变化，请刷新后重新确认。',
    draft_content_conflict: '待发布内容与服务器草稿不一致，请刷新后重新确认。',
    candidate_not_found: '候选语音不存在或已失效，请重新生成。',
    candidate_job_not_found: '候选语音的生成记录不存在，请重新生成。',
    candidate_location_changed: '候选语音对应的模块或节点已变化，请重新生成。',
    candidate_text_stale: '讲解词已修改，旧候选语音不能采用，请重新生成。',
    candidate_configuration_stale: '音色或合成参数已变化，旧候选语音不能采用，请重新生成。',
    candidate_asset_invalid: '候选音频未通过完整性验证，请重新生成。',
    provider_not_found: '所选语音合成服务不存在或未配置。',
    provider_unavailable: '语音合成服务当前不可用。',
    voice_not_found: '所选音色当前不可用，请重新选择。',
    invalid_input: '讲解词或语音参数不受当前服务支持。',
    publish_revision_required: '发布状态已过期，请刷新内容后重新确认。',
    asset_is_referenced: '该素材仍被草稿、正式版本、历史版本或候选语音引用，不能删除。',
  }
  return code ? messages[code] ?? fallback : fallback
}

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
    let code: string | null = null
    try {
      const problem = JSON.parse(text)
      const validation = problem.errors ? Object.values(problem.errors).flat().join('；') : ''
      message = problem.message ?? (validation || problem.detail || text)
      code = problem.code ?? null
    } catch { /* keep the original response text */ }
    message = friendlyApiError(code, message || `请求失败：${response.status}`)
    throw new ApiError(message, response.status, code)
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
  getDraft: () => request<ContentDraftSnapshot>('/api/content/draft', undefined, true),
  saveDraft: (baseContentVersion: number, expectedRevision: number, modules: ExhibitionModule[]) => request<ContentDraftSnapshot>('/api/content/draft', { method: 'PUT', body: JSON.stringify({ baseContentVersion, expectedRevision, modules }) }, true),
  getUi: () => request<UiExperienceConfig>('/api/ui/current'),
  publishUi: (config: UiExperienceConfig) => request<UiExperienceConfig>('/api/ui/publish', { method: 'POST', body: JSON.stringify(config) }, true),
  publish: (modules: ExhibitionModule[], baseContentVersion?: number, expectedDraftRevision?: number) => request<PublishedContent>('/api/content/publish', { method: 'POST', body: JSON.stringify({ modules, baseContentVersion, expectedDraftRevision }) }, true),
  ttsStatus: () => request<TtsStatus>('/api/tts/status', undefined, true),
  clientStatuses: () => request<ClientRuntimeStatus[]>('/api/clients/status', undefined, true),
  playbackSessions: () => request<PlaybackSessionStatus[]>('/api/playback/sessions', undefined, true),
  readiness: () => request<SystemReadiness>('/api/readiness', undefined, true),
  routes: () => request<NarrationRouteCollection>('/api/routes'),
  saveRoute: (route: { id: string | null; name: string; moduleIds: string[] }) => request<NarrationRoute>('/api/routes', { method: 'POST', body: JSON.stringify(route) }, true),
  deleteRoute: (id: string) => request<void>(`/api/routes/${encodeURIComponent(id)}`, { method: 'DELETE' }, true),
  contentVersions: () => request<ContentVersionSummary[]>('/api/content/versions', undefined, true),
  rollbackContent: (version: number, expectedContentVersion: number, expectedDraftRevision: number) => request<PublishedContent>(`/api/content/rollback/${version}`, { method: 'POST', body: JSON.stringify({ expectedContentVersion, expectedDraftRevision }) }, true),
  operations: (count = 200) => request<OperationalEvent[]>(`/api/operations?count=${count}`, undefined, true),
  synthesize: (text: string, voice: string) => request<TtsResult>('/api/tts/synthesize', { method: 'POST', body: JSON.stringify({ text, voice, rate: 1, volume: 1, pitch: 0 }) }, true),
  ttsProviders: () => request<TtsProviderDescriptor[]>('/api/tts/providers', undefined, true),
  createTtsJob: (moduleId: string, nodeId: string, narrationText: string, synthesisConfiguration: TtsSynthesisConfiguration, retryFailed = false) => request<CreateTtsProductionJobResponse>('/api/tts/jobs', { method: 'POST', body: JSON.stringify({ moduleId, nodeId, narrationText, synthesisConfiguration, retryFailed }) }, true),
  getTtsJob: (jobId: string) => request<TtsProductionJob>(`/api/tts/jobs/${encodeURIComponent(jobId)}`, undefined, true),
  cancelTtsJob: (jobId: string) => request<TtsProductionJob>(`/api/tts/jobs/${encodeURIComponent(jobId)}/cancel`, { method: 'POST' }, true),
  getTtsCandidate: (candidateId: string) => request<NarrationAudioCandidate>(`/api/tts/candidates/${encodeURIComponent(candidateId)}`, undefined, true),
  evaluateTtsCandidate: (candidateId: string) => request<NarrationAudioCandidateEvaluation>(`/api/tts/candidates/${encodeURIComponent(candidateId)}/evaluation`, undefined, true),
  adoptTtsCandidate: (candidateId: string, baseContentVersion: number, expectedDraftRevision: number) => request<AdoptNarrationAudioCandidateResponse>(`/api/tts/candidates/${encodeURIComponent(candidateId)}/adopt`, { method: 'POST', body: JSON.stringify({ baseContentVersion, expectedDraftRevision }) }, true),
  bindManualNarrationAudio: (asset: ContentAsset, narrationText: string, language = 'zh-CN') => request<NarrationAudioBinding>('/api/narration-audio/bind-upload', { method: 'POST', body: JSON.stringify({ asset, narrationText, language }) }, true),
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

export const resolveAssetUrl = (url: string): string => new URL(url, apiBase).toString()
