import type {
  AdoptNarrationAudioCandidateResponse,
  CreateTtsProductionJobResponse,
  NarrationAudioCandidate,
  NarrationAudioCandidateEvaluation,
  TtsProductionJob,
  TtsProviderDescriptor,
  TtsSynthesisConfiguration,
} from './api'

export interface TtsWorkflowApi {
  listProviders(): Promise<TtsProviderDescriptor[]>
  createJob(moduleId: string, nodeId: string, narrationText: string,
    configuration: TtsSynthesisConfiguration, retryFailed: boolean): Promise<CreateTtsProductionJobResponse>
  getJob(jobId: string): Promise<TtsProductionJob>
  cancelJob(jobId: string): Promise<TtsProductionJob>
  getCandidate(candidateId: string): Promise<NarrationAudioCandidate>
  evaluateCandidate(candidateId: string): Promise<NarrationAudioCandidateEvaluation>
  adoptCandidate(candidateId: string, baseContentVersion: number,
    expectedDraftRevision: number): Promise<AdoptNarrationAudioCandidateResponse>
}

export interface AudioPreview {
  currentTime: number
  readonly duration: number
  onended: HTMLAudioElement['onended']
  play(): Promise<void>
  pause(): void
}

export interface TtsWorkflowState {
  providers: TtsProviderDescriptor[]
  providersLoaded: boolean
  job: TtsProductionJob | null
  candidate: NarrationAudioCandidate | null
  evaluation: NarrationAudioCandidateEvaluation | null
  generating: boolean
  adopting: boolean
  previewing: boolean
  adopted: boolean
  error: string
}

export interface TtsWorkflowOptions {
  pollIntervalMilliseconds?: number
  maxPolls?: number
  delay?: (milliseconds: number) => Promise<void>
  audioFactory?: (url: string) => AudioPreview
}

const defaultDelay = (milliseconds: number): Promise<void> =>
  new Promise(resolve => window.setTimeout(resolve, milliseconds))

export class TtsWorkflowController {
  private readonly api: TtsWorkflowApi
  private readonly moduleId: string
  private readonly nodeId: string
  private readonly onStateChanged: (state: Readonly<TtsWorkflowState>) => void
  private state: TtsWorkflowState = {
    providers: [], providersLoaded: false, job: null, candidate: null, evaluation: null,
    generating: false, adopting: false, previewing: false, adopted: false, error: '',
  }
  private readonly pollIntervalMilliseconds: number
  private readonly maxPolls: number
  private readonly delay: (milliseconds: number) => Promise<void>
  private readonly audioFactory: (url: string) => AudioPreview
  private pollGeneration = 0
  private disposed = false
  private audio: AudioPreview | null = null
  private audioUrl = ''

  constructor(
    api: TtsWorkflowApi,
    moduleId: string,
    nodeId: string,
    onStateChanged: (state: Readonly<TtsWorkflowState>) => void,
    options: TtsWorkflowOptions = {},
  ) {
    this.api = api
    this.moduleId = moduleId
    this.nodeId = nodeId
    this.onStateChanged = onStateChanged
    this.pollIntervalMilliseconds = Math.max(250, options.pollIntervalMilliseconds ?? 1000)
    this.maxPolls = Math.max(1, options.maxPolls ?? 600)
    this.delay = options.delay ?? defaultDelay
    this.audioFactory = options.audioFactory ?? (url => new Audio(url))
  }

  get current(): Readonly<TtsWorkflowState> { return this.state }

  async loadProviders(): Promise<void> {
    try {
      const providers = await this.api.listProviders()
      if (this.disposed) return
      this.patch({ providers, providersLoaded: true, error: '' })
    } catch (error) {
      if (!this.disposed) this.patch({ providers: [], providersLoaded: true, error: errorMessage(error) })
    }
  }

  async generate(narrationText: string, configuration: TtsSynthesisConfiguration,
    retryFailed = false): Promise<void> {
    if (this.disposed || this.state.generating || this.state.job?.status === 0 || this.state.job?.status === 1) return
    this.stopPreview()
    this.stopPolling()
    this.patch({ generating: true, adopted: false, candidate: null, evaluation: null, error: '' })
    try {
      let result = await this.api.createJob(this.moduleId, this.nodeId, narrationText, configuration, retryFailed)
      // After a page reload the controller no longer knows about an idempotent failed/cancelled Job.
      // Re-enter the existing bounded Server retry path once so one user action still means "generate/retry".
      if (!retryFailed && (result.job.status === 3 || result.job.status === 4))
        result = await this.api.createJob(this.moduleId, this.nodeId, narrationText, configuration, true)
      if (this.disposed) return
      this.patch({ job: result.job, generating: result.job.status === 0 || result.job.status === 1 })
      if (result.job.status === 2) await this.loadCandidate(result.job)
      else if (result.job.status === 3 || result.job.status === 4)
        this.patch({ generating: false, error: jobFailureMessage(result.job) })
      else this.startPolling(result.job.jobId)
    } catch (error) {
      if (!this.disposed) this.patch({ generating: false, error: errorMessage(error) })
    }
  }

  async cancel(): Promise<void> {
    const job = this.state.job
    if (!job || (job.status !== 0 && job.status !== 1)) return
    try {
      const cancelled = await this.api.cancelJob(job.jobId)
      if (!this.disposed) this.patch({ job: cancelled, generating: false, error: '语音生成已取消。' })
    } catch (error) {
      if (!this.disposed) this.patch({ error: errorMessage(error) })
    }
    this.stopPolling()
  }

  async refreshEvaluation(): Promise<void> {
    const candidate = this.state.candidate
    if (!candidate || this.disposed) return
    try {
      const evaluation = await this.api.evaluateCandidate(candidate.candidateId)
      if (!this.disposed) this.patch({ evaluation })
    } catch (error) {
      if (!this.disposed) this.patch({ error: errorMessage(error) })
    }
  }

  async adopt(baseContentVersion: number, expectedDraftRevision: number): Promise<AdoptNarrationAudioCandidateResponse | null> {
    const candidate = this.state.candidate
    if (!candidate || this.state.adopting || this.disposed) return null
    this.patch({ adopting: true, error: '' })
    try {
      const result = await this.api.adoptCandidate(candidate.candidateId, baseContentVersion, expectedDraftRevision)
      if (!this.disposed) this.patch({ adopting: false, adopted: true, evaluation: null })
      return result
    } catch (error) {
      if (!this.disposed) this.patch({ adopting: false, error: errorMessage(error) })
      return null
    }
  }

  async togglePreview(url: string): Promise<void> {
    if (this.disposed) return
    if (this.audio && this.audioUrl === url && this.state.previewing) {
      this.audio.pause()
      this.patch({ previewing: false })
      return
    }
    if (!this.audio || this.audioUrl !== url) {
      this.stopPreview()
      this.audio = this.audioFactory(url)
      this.audioUrl = url
      this.audio.onended = () => { if (!this.disposed) this.patch({ previewing: false }) }
    }
    if (Number.isFinite(this.audio.duration) && this.audio.duration > 0 && this.audio.currentTime >= this.audio.duration)
      this.audio.currentTime = 0
    try {
      await this.audio.play()
      if (!this.disposed) this.patch({ previewing: true, error: '' })
    } catch (error) {
      if (!this.disposed) this.patch({ previewing: false, error: `无法试听：${errorMessage(error)}` })
    }
  }

  async replay(url: string): Promise<void> {
    if (!this.audio || this.audioUrl !== url) {
      await this.togglePreview(url)
      return
    }
    this.audio.pause()
    this.audio.currentTime = 0
    try {
      await this.audio.play()
      if (!this.disposed) this.patch({ previewing: true, error: '' })
    } catch (error) {
      if (!this.disposed) this.patch({ previewing: false, error: `无法试听：${errorMessage(error)}` })
    }
  }

  abandon(): void {
    this.stopPolling()
    this.stopPreview()
    this.patch({ job: null, candidate: null, evaluation: null, generating: false, adopted: false, error: '' })
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    this.stopPolling()
    this.stopPreview(false)
  }

  private startPolling(jobId: string): void {
    const generation = ++this.pollGeneration
    void this.poll(jobId, generation)
  }

  private async poll(jobId: string, generation: number): Promise<void> {
    for (let count = 0; count < this.maxPolls; count++) {
      await this.delay(this.pollIntervalMilliseconds)
      if (this.disposed || generation !== this.pollGeneration) return
      try {
        const job = await this.api.getJob(jobId)
        if (this.disposed || generation !== this.pollGeneration) return
        this.patch({ job, generating: job.status === 0 || job.status === 1 })
        if (job.status === 2) { await this.loadCandidate(job); return }
        if (job.status === 3 || job.status === 4) {
          this.patch({ generating: false, error: jobFailureMessage(job) })
          return
        }
      } catch (error) {
        if (!this.disposed && generation === this.pollGeneration)
          this.patch({ generating: false, error: errorMessage(error) })
        return
      }
    }
    if (!this.disposed && generation === this.pollGeneration)
      this.patch({ generating: false, error: '语音生成状态查询超时，请稍后重新打开节点查看。' })
  }

  private async loadCandidate(job: TtsProductionJob): Promise<void> {
    if (!job.candidateId) {
      this.patch({ generating: false, error: '生成任务已完成，但没有返回候选语音。' })
      return
    }
    try {
      const [candidate, evaluation] = await Promise.all([
        this.api.getCandidate(job.candidateId), this.api.evaluateCandidate(job.candidateId),
      ])
      if (!this.disposed) this.patch({ job, candidate, evaluation, generating: false, error: '' })
    } catch (error) {
      if (!this.disposed) this.patch({ generating: false, error: errorMessage(error) })
    }
  }

  private stopPolling(): void { this.pollGeneration++ }

  private stopPreview(notify = true): void {
    if (this.audio) {
      this.audio.pause()
      this.audio.currentTime = 0
      this.audio.onended = null
    }
    this.audio = null
    this.audioUrl = ''
    if (notify && this.state.previewing && !this.disposed) this.patch({ previewing: false })
    else this.state = { ...this.state, previewing: false }
  }

  private patch(value: Partial<TtsWorkflowState>): void {
    this.state = { ...this.state, ...value }
    this.onStateChanged(this.state)
  }
}

export const jobStatusLabel = (status: number): string =>
  ['等待生成', '正在生成', '生成成功', '生成失败', '已取消'][status] ?? '状态未知'

export const bindingStatusLabel = (status: number): string => [
  '尚未生成', '语音有效', '讲解词已修改，请重新生成', '音色配置已变化，请重新生成',
  '音频资产异常', '语音绑定异常', '旧版语音待升级',
][status] ?? '语音状态未知'

const jobFailureMessage = (job: TtsProductionJob): string => {
  const messages: Record<string, string> = {
    provider_timeout: '语音服务响应超时，请稍后重试。',
    provider_unavailable: '语音合成服务当前不可用。',
    invalid_input: '讲解词或语音参数不受当前服务支持。',
    invalid_media: '生成的音频未通过完整性验证。',
    cancelled: '语音生成已取消。',
    server_interrupted: '生成过程中服务重启，请重新生成。',
  }
  return (job.errorCode && messages[job.errorCode]) || (job.status === 4 ? '语音生成已取消。' : '语音生成失败，请重试。')
}

const errorMessage = (error: unknown): string => error instanceof Error ? error.message : String(error)
