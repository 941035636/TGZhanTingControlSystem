import test from 'node:test'
import assert from 'node:assert/strict'
import { validateLedIdleMediaDimensions } from '../src/ui-media-validation.ts'

test('accepts exact 1920 by 1080 LED idle media', () => {
  assert.equal(validateLedIdleMediaDimensions(1920, 1080), null)
})

test('rejects other LED idle media dimensions with the actual size', () => {
  assert.equal(
    validateLedIdleMediaDimensions(3840, 2160),
    '大屏待机素材必须为 1920×1080，当前文件为 3840×2160。',
  )
})

test('rejects unreadable LED idle media metadata', () => {
  assert.equal(validateLedIdleMediaDimensions(0, 0), '无法读取素材分辨率，请更换文件后重试。')
})
