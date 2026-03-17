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

function getPreviewKeyboardModifiers(event) {
  return {
    alt: !!(event && event.altKey),
    control: !!(event && event.ctrlKey),
    shift: !!(event && event.shiftKey),
    meta: !!(event && event.metaKey)
  };
}

function getPreviewKeyboardText(event) {
  if (!event || event.isComposing || event.metaKey) {
    return '';
  }

  const altGraph = typeof event.getModifierState === 'function' && event.getModifierState('AltGraph');
  const allowAltText = altGraph ||
    !!event.allowAltText ||
    (typeof navigator !== 'undefined' &&
      /Mac|iPhone|iPad|iPod/i.test(
        String(
          navigator.userAgentData && typeof navigator.userAgentData.platform === 'string'
            ? navigator.userAgentData.platform
            : navigator.platform || navigator.userAgent || '')));
  if ((!altGraph && event.ctrlKey) || (event.altKey && !allowAltText)) {
    return '';
  }

  const key = typeof event.key === 'string' ? event.key : '';
  if (!key || key === 'Dead' || key === 'Process' || key === 'Unidentified') {
    return '';
  }

  if (key === 'Enter') {
    return '\r';
  }

  return key.length === 1
    ? key
    : '';
}

function createPreviewKeyInputPayload(event, isDown) {
  const key = event && typeof event.key === 'string' ? event.key : '';
  const code = event && typeof event.code === 'string' ? event.code : '';
  const keySymbol = isDown ? getPreviewKeyboardText(event) : '';
  if (!key && !code && !keySymbol) {
    return null;
  }

  const rawLocation = Number(event && event.location);
  return {
    eventType: 'key',
    isDown: !!isDown,
    key: key || undefined,
    code: code || undefined,
    location: Number.isFinite(rawLocation) && rawLocation >= 0
      ? rawLocation
      : 0,
    keySymbol: keySymbol || undefined,
    modifiers: getPreviewKeyboardModifiers(event)
  };
}

function createPreviewTextInputPayload(event) {
  const text = getPreviewKeyboardText(event);
  if (!text) {
    return null;
  }

  return {
    eventType: 'text',
    text,
    modifiers: getPreviewKeyboardModifiers(event)
  };
}

function createPreviewKeyboardInputPayloads(event, isDown) {
  const payloads = [];
  const keyPayload = createPreviewKeyInputPayload(event, isDown);
  if (keyPayload) {
    payloads.push(keyPayload);
  }

  if (isDown) {
    const textPayload = createPreviewTextInputPayload(event);
    if (textPayload) {
      payloads.push(textPayload);
    }
  }

  return payloads;
}

module.exports = {
  createPreviewKeyboardInputPayloads,
  createPreviewKeyInputPayload,
  createPreviewTextInputPayload,
  getPreviewKeyboardModifiers,
  getPreviewKeyboardText,
  mapPreviewClientPointToRemotePoint,
  normalizePreviewRenderScale
};
