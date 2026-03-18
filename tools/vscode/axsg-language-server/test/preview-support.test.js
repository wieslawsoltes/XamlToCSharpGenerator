const test = require('node:test');
const assert = require('node:assert/strict');
const Module = require('node:module');

function createVscodeMock() {
  return {
    window: {
      activeTextEditor: null
    },
    workspace: {},
    commands: {},
    ProgressLocation: {
      Notification: 15
    },
    ViewColumn: {
      Beside: 2
    }
  };
}

function loadPreviewSupport(vscodeMock) {
  const modulePath = require.resolve('../preview-support');
  delete require.cache[modulePath];

  const originalLoad = Module._load;
  Module._load = function patchedLoad(request, parent, isMain) {
    if (request === 'vscode') {
      return vscodeMock;
    }

    return originalLoad.call(this, request, parent, isMain);
  };

  try {
    return require(modulePath);
  } finally {
    Module._load = originalLoad;
  }
}

function createController(vscodeMock) {
  const { AvaloniaPreviewController } = loadPreviewSupport(vscodeMock);
  return new AvaloniaPreviewController({
    context: {
      extensionPath: '/tmp'
    },
    ensureClientStarted: async () => null,
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true,
    workspaceRoot: '/tmp'
  });
}

test('getActiveSession returns the editor session when available', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const editorSession = { panel: { active: false } };

  vscodeMock.window.activeTextEditor = {
    document: {
      uri: {
        toString: () => 'file:///editor.axaml'
      }
    }
  };

  controller.sessions.set('file:///editor.axaml', editorSession);

  assert.equal(controller.getActiveSession(), editorSession);
});

test('getActiveSession falls back to the active preview panel session', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const inactiveSession = { panel: { active: false } };
  const activeSession = { panel: { active: true } };

  controller.sessions.set('file:///inactive.axaml', inactiveSession);
  controller.sessions.set('file:///active.axaml', activeSession);

  assert.equal(controller.getActiveSession(), activeSession);
});

test('getActiveSession prefers the active preview panel over a stale text editor session', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const editorSession = { panel: { active: false } };
  const activePreviewSession = { panel: { active: true } };

  vscodeMock.window.activeTextEditor = {
    document: {
      uri: {
        toString: () => 'file:///editor.axaml'
      }
    }
  };

  controller.sessions.set('file:///editor.axaml', editorSession);
  controller.sessions.set('file:///preview.axaml', activePreviewSession);

  assert.equal(controller.getActiveSession(), activePreviewSession);
});
