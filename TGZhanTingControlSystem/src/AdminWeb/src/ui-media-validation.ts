export const LED_IDLE_MEDIA_ASPECT = 16 / 9
export const LED_IDLE_MEDIA_ASPECT_TOLERANCE = 0.01

export const validateLedIdleMediaDimensions = (width: number, height: number): string | null => {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0)
    return '无法读取素材分辨率，请更换文件后重试。'
  const aspect = width / height
  if (Math.abs(aspect - LED_IDLE_MEDIA_ASPECT) > LED_IDLE_MEDIA_ASPECT_TOLERANCE)
    return `大屏待机素材必须为16:9横屏比例，当前文件为 ${width}×${height}。`
  return null
}
