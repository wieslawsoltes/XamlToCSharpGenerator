const test = require('node:test');
const assert = require('node:assert/strict');
const Module = require('node:module');

function createVscodeMock() {
  return {
    window: {
      activeTextEditor: null
    },
    workspace: {},
    Uri: {
      file(value) {
        return {
          toString() {
            return `file://${value}`;
          }
        };
      }
    },
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

test('preview sessions stay scoped to their launch document', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const session = {
    documentUri: 'file:///root.axaml',
    panel: { active: false }
  };

  controller.registerSessionDocumentUris(session, [session.documentUri]);

  assert.equal(controller.getSession('file:///root.axaml'), session);
  assert.equal(controller.getSession('file:///tmp/Secondary.axaml'), null);
  assert.equal(controller.getSession('file:///tmp/Tertiary.axaml'), null);

  controller.removeSession('file:///root.axaml');

  assert.equal(controller.getSession('file:///root.axaml'), null);
  assert.equal(controller.getSession('file:///tmp/Secondary.axaml'), null);
  assert.equal(controller.getSession('file:///tmp/Tertiary.axaml'), null);
});

test('describePreviewDesignState reports unavailable inspector state', () => {
  const vscodeMock = createVscodeMock();
  const { describePreviewDesignState } = loadPreviewSupport(vscodeMock);

  assert.deepEqual(describePreviewDesignState(null), {
    kind: 'unavailable',
    available: false,
    badgeText: 'Inspector unavailable',
    message: 'AXSG Inspector is waiting for preview design data.'
  });
});

test('describePreviewDesignState reports interactive preview guidance', () => {
  const vscodeMock = createVscodeMock();
  const { describePreviewDesignState } = loadPreviewSupport(vscodeMock);
  const description = describePreviewDesignState({
    available: true,
    workspaceMode: 'Interactive',
    hitTestMode: 'Visual'
  });

  assert.equal(description.kind, 'interactive');
  assert.equal(description.available, true);
  assert.equal(description.badgeText, 'Interactive mode');
  assert.match(description.message, /Switch Mode to Design or Agent/);
  assert.match(description.message, /visual tree/);
});

test('describePreviewDesignState reports ready inspector state', () => {
  const vscodeMock = createVscodeMock();
  const { describePreviewDesignState } = loadPreviewSupport(vscodeMock);

  assert.deepEqual(describePreviewDesignState({
    available: true,
    workspaceMode: 'Design',
    hitTestMode: 'Logical'
  }), {
    kind: 'ready',
    available: true,
    badgeText: 'Design / Logical',
    message: 'Inspector ready in Design mode using the logical tree.'
  });
});
