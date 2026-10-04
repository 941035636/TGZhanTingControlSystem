export const LED_IDLE_MEDIA_WIDTH = 1920
export const LED_IDLE_MEDIA_HEIGHT = 1080

export const validateLedIdleMediaDimensions = (width: number, height: number): string | null => {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0)
    return '无法读取素材分辨率，请更换文件后重试。'
  if (width !== LED_IDLE_MEDIA_WIDTH || height !== LED_IDLE_MEDIA_HEIGHT)
    return `大屏待机素材必须为 ${LED_IDLE_MEDIA_WIDTH}×${LED_IDLE_MEDIA_HEIGHT}，当前文件为 ${width}×${height}。`
  return null
}
