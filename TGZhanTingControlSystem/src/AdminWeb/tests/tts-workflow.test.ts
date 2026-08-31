import test from 'node:test'
import assert from 'node:assert/strict'
import { TtsWorkflowController, type AudioPreview, type TtsWorkflowApi } from '../src/tts-workflow.ts'
import type { NarrationAudioCandidate, NarrationAudioCandidateEvaluation, TtsProductionJob, TtsProviderDescriptor, TtsSynthesisConfiguration } from '../src/api.ts'

const configuration: TtsSynthesisConfiguration = {
  providerKey: 'deterministic-test', voice: 'test-zh-CN', language: 'zh-CN', rate: 1,
  pitch: 0, volume: 1, outputMediaType: 'audio/wav', sampleRateHz: 24000, channels: 1,
}

const provider: TtsProviderDescriptor = {
  providerId: 'deterministic-test', displayName: '确定性测试 Provider', available: true,
  developmentOnly: true, unavailableReason: null,
  voices: [{ voiceId: 'test-zh-CN', displayName: '开发测试音色', language: 'zh-CN' }],
  capabilities: { maxTextLength: 5000, minRate: 0.5, maxRate: 2, minPitch: -1, maxPitch: 1, supportedMediaTypes: ['audio/wav'] },
}

const makeJob = (status: 0|1|2|3|4, candidateId: string|null = null): TtsProductionJob => ({
  jobId: 'job-1', moduleId: 'module-1', nodeId: 'node-1', requestedBy: 'admin', narrationText: '讲解词',
  narrationTextFingerprint: 'text-fingerprint', synthesisConfiguration: configuration,
  synthesisConfigurationFingerprint: 'config-fingerprint', idempotencyKey: 'key', providerId: provider.providerId,
  voice: configuration.voice, status, createdAtUtc: '2026-08-31T00:00:00Z', startedAtUtc: null,
  completedAtUtc: status >= 2 ? '2026-08-31T00:00:01Z' : null, retryCount: 0, attempts: [],
  errorCategory: status === 3 ? 0 : null, errorCode: status === 3 ? 'provider_unavailable' : null,
  errorMessage: status === 3 ? 'provider failed' : null, candidateId,
})

const candidate: NarrationAudioCandidate = {
  candidateId: 'candidate-1', jobId: 'job-1',
  asset: { id: 'asset-1', name: 'candidate.wav', kind: 3, url: '/assets/candidate.wav', sha256: 'a'.repeat(64), sizeBytes: 128, durationSeconds: 1, mediaType: 'audio/wav' },
  narrationTextFingerprint: 'text-fingerprint', synthesisConfigurationFingerprint: 'config-fingerprint',
  synthesisConfiguration: configuration, providerId: provider.providerId, voice: configuration.voice,
  createdAtUtc: '2026-08-31T00:00:01Z', validation: { valid: true, validator: 'wave', mediaType: 'audio/wav', durationSeconds: 1, validatedAtUtc: '2026-08-31T00:00:01Z' }, providerRequestId: null,
}

const evaluation: NarrationAudioCandidateEvaluation = {
  candidateId: candidate.candidateId, baseContentVersion: 8, draftRevision: 4, candidateExists: true,
  locationMatches: true, narrationTextMatches: true, synthesisConfigurationMatches: true,
  assetValid: true, adoptable: true, message: 'ok',
}

const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(completion => { resolve = completion })
  return { promise, resolve }
}

const fakeApi = (overrides: Partial<TtsWorkflowApi> = {}): TtsWorkflowApi => ({
  listProviders: async () => [provider],
  createJob: async () => ({ job: makeJob(2, candidate.candidateId), created: true }),
  getJob: async () => makeJob(2, candidate.candidateId),
  cancelJob: async () => makeJob(4),
  getCandidate: async () => candidate,
  evaluateCandidate: async () => evaluation,
  adoptCandidate: async () => ({
    draft: { baseContentVersion: 8, revision: 5, updatedAtUtc: '2026-08-31T00:00:02Z', updatedBy: 'admin', modules: [], narrationAudioStatuses: [] },
    binding: { asset: candidate.asset, narrationTextFingerprint: candidate.narrationTextFingerprint, synthesisConfigurationFingerprint: candidate.synthesisConfigurationFingerprint, synthesisConfiguration: configuration, origin: 0, boundAtUtc: '2026-08-31T00:00:02Z', fingerprintVersion: 'v1', providerRequestId: null },
  }),
  ...overrides,
})

test('loads dynamic provider and development voice metadata', async () => {
  const controller = new TtsWorkflowController(fakeApi(), 'module-1', 'node-1', () => {})
  await controller.loadProviders()
  assert.equal(controller.current.providers[0]?.developmentOnly, true)
  assert.equal(controller.current.providers[0]?.voices[0]?.displayName, '开发测试音色')
  controller.dispose()
})

test('represents no configured provider without manufacturing a voice', async () => {
  const controller = new TtsWorkflowController(fakeApi({ listProviders: async () => [] }), 'module-1', 'node-1', () => {})
  await controller.loadProviders()
  assert.deepEqual(controller.current.providers, [])
  assert.equal(controller.current.providersLoaded, true)
  controller.dispose()
})

test('generate success loads candidate and Server evaluation', async () => {
  const controller = new TtsWorkflowController(fakeApi(), 'module-1', 'node-1', () => {})
  await controller.generate('讲解词', configuration)
  assert.equal(controller.current.job?.status, 2)
  assert.equal(controller.current.candidate?.candidateId, candidate.candidateId)
  assert.equal(controller.current.evaluation?.adoptable, true)
  controller.dispose()
})

test('generate failure is shown as a bounded friendly state', async () => {
  const controller = new TtsWorkflowController(fakeApi({ createJob: async () => ({ job: makeJob(3), created: true }) }), 'module-1', 'node-1', () => {})
  await controller.generate('讲解词', configuration)
  assert.equal(controller.current.generating, false)
  assert.match(controller.current.error, /不可用/)
  controller.dispose()
})

test('rapid duplicate Generate is suppressed while request is active', async () => {
  const pending = deferred<{job:TtsProductionJob;created:boolean}>()
  let calls = 0
  const controller = new TtsWorkflowController(fakeApi({ createJob: async () => { calls++; return pending.promise } }), 'module-1', 'node-1', () => {})
  const first = controller.generate('讲解词', configuration)
  const second = controller.generate('讲解词', configuration)
  assert.equal(calls, 1)
  pending.resolve({ job: makeJob(3), created: true })
  await Promise.all([first, second])
  controller.dispose()
})

test('polling stops after page disposal', async () => {
  const wait = deferred<void>()
  let getCalls = 0
  const controller = new TtsWorkflowController(fakeApi({
    createJob: async () => ({ job: makeJob(0), created: true }),
    getJob: async () => { getCalls++; return makeJob(1) },
  }), 'module-1', 'node-1', () => {}, { delay: async () => wait.promise, maxPolls: 2 })
  await controller.generate('讲解词', configuration)
  controller.dispose()
  wait.resolve()
  await new Promise(resolve => setTimeout(resolve, 0))
  assert.equal(getCalls, 0)
})

test('candidate preview reuses its asset URL and stops on disposal', async () => {
  class FakeAudio implements AudioPreview {
    currentTime = 0
    duration = 1
    onended: HTMLAudioElement['onended'] = null
    playCalls = 0
    pauseCalls = 0
    async play() { this.playCalls++ }
    pause() { this.pauseCalls++ }
  }
  const audio = new FakeAudio()
  let requestedUrl = ''
  const controller = new TtsWorkflowController(fakeApi(), 'module-1', 'node-1', () => {}, { audioFactory: url => { requestedUrl = url; return audio } })
  await controller.generate('讲解词', configuration)
  await controller.togglePreview('http://localhost/assets/candidate.wav')
  assert.equal(requestedUrl, 'http://localhost/assets/candidate.wav')
  assert.equal(controller.current.previewing, true)
  controller.dispose()
  assert.equal(audio.pauseCalls, 1)
})

test('explicit Adopt delegates revision check to Server and does not publish', async () => {
  let adoptArguments: [string, number, number] | null = null
  let publishCalls = 0
  const api = fakeApi({
    adoptCandidate: async (id, base, revision) => {
      adoptArguments = [id, base, revision]
      return fakeApi().adoptCandidate(id, base, revision)
    },
  }) as TtsWorkflowApi & { publish?: () => void }
  api.publish = () => { publishCalls++ }
  const controller = new TtsWorkflowController(api, 'module-1', 'node-1', () => {})
  await controller.generate('讲解词', configuration)
  const result = await controller.adopt(8, 4)
  assert.deepEqual(adoptArguments, [candidate.candidateId, 8, 4])
  assert.equal(result?.draft.revision, 5)
  assert.equal(publishCalls, 0)
  controller.dispose()
})
