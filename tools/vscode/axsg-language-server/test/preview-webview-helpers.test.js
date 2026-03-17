const test = require('node:test');
const assert = require('node:assert/strict');

const {
  createPreviewKeyboardInputPayloads,
  createPreviewKeyInputPayload,
  createPreviewTextInputPayload,
  getPreviewKeyboardModifiers,
  getPreviewKeyboardText,
  mapPreviewClientPointToRemotePoint,
  normalizePreviewRenderScale
} = require('../preview-webview-helpers');

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
