const test = require('node:test');
const assert = require('node:assert/strict');

const {
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
