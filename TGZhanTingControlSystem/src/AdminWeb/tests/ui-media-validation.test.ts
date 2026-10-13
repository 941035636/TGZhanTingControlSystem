import test from 'node:test'
import assert from 'node:assert/strict'
import { validateLedIdleMediaDimensions } from '../src/ui-media-validation.ts'

test('accepts standard 1920 by 1080 LED idle media', () => {
  assert.equal(validateLedIdleMediaDimensions(1920, 1080), null)
})

test('accepts the customer 4K 4096 by 2304 LED idle media', () => {
  assert.equal(validateLedIdleMediaDimensions(4096, 2304), null)
})

test('rejects non-16:9 LED idle media with the actual size', () => {
  assert.equal(validateLedIdleMediaDimensions(1920, 1200), '大屏待机素材必须为16:9横屏比例，当前文件为 1920×1200。')
})

test('rejects unreadable LED idle media metadata', () => {
  assert.equal(validateLedIdleMediaDimensions(0, 0), '无法读取素材分辨率，请更换文件后重试。')
})
