function normalizePreviewRenderScale(dpiOrScale, fallbackScale = 1) {
  const normalizedFallback = Number.isFinite(Number(fallbackScale)) && Number(fallbackScale) > 0
    ? Number(fallbackScale)
    : 1;
  const normalizedValue = Number(dpiOrScale);
  if (!Number.isFinite(normalizedValue) || normalizedValue <= 0) {
    return normalizedFallback;
  }

  // Avalonia frame messages report DPI, while the webview bootstrap carries scale.
  return normalizedValue > 12
    ? normalizedValue / 96
    : normalizedValue;
}

function mapPreviewClientPointToRemotePoint(offsetX, offsetY, viewportScale, previewRenderScale) {
  const normalizedViewportScale = normalizePreviewRenderScale(viewportScale, 1);
  const normalizedPreviewRenderScale = normalizePreviewRenderScale(previewRenderScale, normalizedViewportScale);
  const scaleFactor = normalizedViewportScale / normalizedPreviewRenderScale;

  return {
    x: Number(offsetX) * scaleFactor,
    y: Number(offsetY) * scaleFactor
  };
}

module.exports = {
  mapPreviewClientPointToRemotePoint,
  normalizePreviewRenderScale
};
