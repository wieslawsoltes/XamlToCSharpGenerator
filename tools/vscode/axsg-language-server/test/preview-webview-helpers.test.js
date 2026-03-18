const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');

const {
  calculatePreviewSurfaceBounds,
  clampPreviewZoom,
  createPreviewKeyboardInputPayloads,
  createPreviewKeyInputPayload,
  createPreviewTextInputPayload,
  formatPreviewZoomLabel,
  getPreviewKeyboardModifiers,
  getPreviewKeyboardText,
  mapPreviewClientPointToDesignPoint,
  mapPreviewClientPointToRemotePoint,
  normalizePreviewRenderScale,
  projectPreviewOverlayBounds,
  stepPreviewZoom
} = require('../preview-webview-helpers');

test('calculatePreviewSurfaceBounds subtracts stage padding from stable bounds', () => {
  assert.deepEqual(
    calculatePreviewSurfaceBounds(1280, 720, 48, 48),
    {
      width: 1232,
      height: 672
    });
});

test('calculatePreviewSurfaceBounds clamps invalid inputs to a minimum renderable size', () => {
  assert.deepEqual(
    calculatePreviewSurfaceBounds(undefined, Number.NaN, 48, 48),
    {
      width: 1,
      height: 1
    });
});

test('clampPreviewZoom bounds zoom levels to the supported range', () => {
  assert.equal(clampPreviewZoom(0.1), 0.25);
  assert.equal(clampPreviewZoom(1.4), 1.4);
  assert.equal(clampPreviewZoom(8), 3);
});

test('stepPreviewZoom applies fixed zoom increments in both directions', () => {
  assert.equal(stepPreviewZoom(1, 1), 1.1);
  assert.equal(stepPreviewZoom(1, -1), 0.9);
  assert.equal(stepPreviewZoom(0.25, -1), 0.25);
});

test('formatPreviewZoomLabel renders rounded percentages', () => {
  assert.equal(formatPreviewZoomLabel(1), '100%');
  assert.equal(formatPreviewZoomLabel(1.26), '126%');
});

test('zoom helpers stay self-contained when serialized into the webview', () => {
  const context = vm.createContext({});
  vm.runInContext(`
    ${calculatePreviewSurfaceBounds.toString()}
    ${clampPreviewZoom.toString()}
    ${stepPreviewZoom.toString()}
    ${formatPreviewZoomLabel.toString()}
    ${mapPreviewClientPointToDesignPoint.toString()}
    ${projectPreviewOverlayBounds.toString()}
    globalThis.results = {
      bounds: calculatePreviewSurfaceBounds(1280, 720, 48, 48),
      clamped: clampPreviewZoom(0.1),
      stepped: stepPreviewZoom(1, 1),
      label: formatPreviewZoomLabel(1.25),
      designPoint: mapPreviewClientPointToDesignPoint(240, 120, 2),
      overlay: projectPreviewOverlayBounds({ X: 20, Y: 10, Width: 100, Height: 50 }, 400, 200, 800, 400)
    };
  `, context);

  assert.equal(context.results.bounds.width, 1232);
  assert.equal(context.results.bounds.height, 672);
  assert.equal(context.results.clamped, 0.25);
  assert.equal(context.results.stepped, 1.1);
  assert.equal(context.results.label, '125%');
  assert.equal(context.results.designPoint.x, 120);
  assert.equal(context.results.designPoint.y, 60);
  assert.equal(context.results.overlay.left, 40);
  assert.equal(context.results.overlay.height, 100);
});

test('normalizePreviewRenderScale converts Avalonia frame DPI to render scale', () => {
  assert.equal(normalizePreviewRenderScale(192, 1), 2);
  assert.equal(normalizePreviewRenderScale(144, 1), 1.5);
});

test('normalizePreviewRenderScale accepts bootstrap scale values directly', () => {
  assert.equal(normalizePreviewRenderScale(2, 1), 2);
  assert.equal(normalizePreviewRenderScale(1.25, 1), 1.25);
});

test('normalizePreviewRenderScale falls back when the provided value is invalid', () => {
  assert.equal(normalizePreviewRenderScale(0, 1.5), 1.5);
  assert.equal(normalizePreviewRenderScale(Number.NaN, 2), 2);
});

test('mapPreviewClientPointToRemotePoint keeps CSS coordinates when preview scale matches viewport scale', () => {
  assert.deepEqual(
    mapPreviewClientPointToRemotePoint(120, 80, 2, 2),
    {
      x: 120,
      y: 80
    });
});

test('mapPreviewClientPointToRemotePoint compensates for differing viewport and preview scales', () => {
  assert.deepEqual(
    mapPreviewClientPointToRemotePoint(120, 80, 2, 1),
    {
      x: 240,
      y: 160
    });
});

test('mapPreviewClientPointToRemotePoint compensates for client zoom', () => {
  assert.deepEqual(
    mapPreviewClientPointToRemotePoint(200, 120, 1, 1, 2),
    {
      x: 100,
      y: 60
  });
});

test('mapPreviewClientPointToDesignPoint removes the client zoom factor', () => {
  assert.deepEqual(
    mapPreviewClientPointToDesignPoint(240, 150, 2),
    {
      x: 120,
      y: 75
    });
});

test('projectPreviewOverlayBounds maps logical preview bounds onto the rendered surface', () => {
  assert.deepEqual(
    projectPreviewOverlayBounds(
      {
        X: 100,
        Y: 50,
        Width: 200,
        Height: 100
      },
      800,
      400,
      1200,
      600),
    {
      left: 150,
      top: 75,
      width: 300,
      height: 150
    });
});

test('getPreviewKeyboardModifiers projects browser modifier flags', () => {
  assert.deepEqual(
    getPreviewKeyboardModifiers({
      altKey: true,
      ctrlKey: false,
      shiftKey: true,
      metaKey: false
    }),
    {
      alt: true,
      control: false,
      shift: true,
      meta: false
    });
});

test('getPreviewKeyboardText returns printable characters and enter text', () => {
  assert.equal(getPreviewKeyboardText({ key: 'a' }), 'a');
  assert.equal(getPreviewKeyboardText({ key: 'Enter' }), '\r');
  assert.equal(getPreviewKeyboardText({ key: 'ArrowLeft' }), '');
  assert.equal(getPreviewKeyboardText({ key: 'x', ctrlKey: true }), '');
});

test('getPreviewKeyboardText preserves AltGr text input', () => {
  assert.equal(
    getPreviewKeyboardText({
      key: '@',
      ctrlKey: true,
      altKey: true,
      getModifierState(modifier) {
        return modifier === 'AltGraph';
      }
    }),
    '@');
});

test('getPreviewKeyboardText preserves macOS option text input', () => {
  assert.equal(
    getPreviewKeyboardText({
      key: 'å',
      altKey: true,
      ctrlKey: false,
      metaKey: false,
      allowAltText: true
    }),
    'å');
});

test('getPreviewKeyboardText detects macOS option text input from navigator platform', () => {
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');

  try {
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {
        platform: 'MacIntel'
      }
    });

    assert.equal(
      getPreviewKeyboardText({
        key: 'å',
        altKey: true
      }),
      'å');
  } finally {
    Object.defineProperty(globalThis, 'navigator', originalNavigator);
  }
});

test('getPreviewKeyboardText suppresses non-mac alt accelerators', () => {
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');

  try {
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {
        platform: 'Linux x86_64'
      }
    });

    assert.equal(
      getPreviewKeyboardText({
        key: 'f',
        altKey: true
      }),
      '');
  } finally {
    Object.defineProperty(globalThis, 'navigator', originalNavigator);
  }
});

test('createPreviewKeyInputPayload includes key metadata for non-text keys', () => {
  assert.deepEqual(
    createPreviewKeyInputPayload(
      {
        key: 'ArrowLeft',
        code: 'ArrowLeft',
        location: 0,
        altKey: false,
        ctrlKey: false,
        shiftKey: false,
        metaKey: false
      },
      true),
    {
      eventType: 'key',
      isDown: true,
      key: 'ArrowLeft',
      code: 'ArrowLeft',
      location: 0,
      keySymbol: undefined,
      modifiers: {
        alt: false,
        control: false,
        shift: false,
        meta: false
      }
    });
});

test('createPreviewTextInputPayload ignores control chords', () => {
  assert.equal(
    createPreviewTextInputPayload({
      key: 'v',
      code: 'KeyV',
      ctrlKey: true,
      location: 0
    }),
    null);
});

test('createPreviewTextInputPayload preserves macOS option text input', () => {
  assert.deepEqual(
    createPreviewTextInputPayload({
      key: 'å',
      code: 'KeyA',
      altKey: true,
      allowAltText: true,
      location: 0
    }),
    {
      eventType: 'text',
      text: 'å',
      modifiers: {
        alt: true,
        control: false,
        shift: false,
        meta: false
      }
    });
});

test('createPreviewTextInputPayload uses committed composition text', () => {
  assert.deepEqual(
    createPreviewTextInputPayload({
      key: 'Process',
      code: 'KeyA',
      data: '漢',
      isComposing: true,
      location: 0
    }),
    {
      eventType: 'text',
      text: '漢',
      modifiers: {
        alt: false,
        control: false,
        shift: false,
        meta: false
      }
    });
});

test('createPreviewKeyboardInputPayloads emits key then text for printable input', () => {
  assert.deepEqual(
    createPreviewKeyboardInputPayloads(
      {
        key: 'a',
        code: 'KeyA',
        location: 0,
        altKey: false,
        ctrlKey: false,
        shiftKey: false,
        metaKey: false
      },
      true),
    [
      {
        eventType: 'key',
        isDown: true,
        key: 'a',
        code: 'KeyA',
        location: 0,
        keySymbol: 'a',
        modifiers: {
          alt: false,
          control: false,
          shift: false,
          meta: false
        }
      },
      {
        eventType: 'text',
        text: 'a',
        modifiers: {
          alt: false,
          control: false,
          shift: false,
          meta: false
        }
      }
    ]);
});

test('createPreviewKeyboardInputPayloads emits only key input for keyup', () => {
  assert.deepEqual(
    createPreviewKeyboardInputPayloads(
      {
        key: 'a',
        code: 'KeyA',
        location: 0
      },
      false),
    [
      {
        eventType: 'key',
        isDown: false,
        key: 'a',
        code: 'KeyA',
        location: 0,
        keySymbol: undefined,
        modifiers: {
          alt: false,
          control: false,
          shift: false,
          meta: false
        }
      }
    ]);
});
