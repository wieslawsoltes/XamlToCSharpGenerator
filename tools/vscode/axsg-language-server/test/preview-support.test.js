const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const os = require('node:os');
const path = require('node:path');

function createVscodeMock() {
  const didChangeTextDocumentListeners = [];
  const didSaveTextDocumentListeners = [];
  const fileWatchers = [];

  return {
    window: {
      activeTextEditor: null
    },
    workspace: {
      onDidChangeTextDocument(listener) {
        didChangeTextDocumentListeners.push(listener);
        return {
          dispose() {}
        };
      },
      onDidSaveTextDocument(listener) {
        didSaveTextDocumentListeners.push(listener);
        return {
          dispose() {}
        };
      },
      createFileSystemWatcher() {
        const changeListeners = [];
        const createListeners = [];
        const deleteListeners = [];
        const watcher = {
          onDidChange(listener) {
            changeListeners.push(listener);
            return { dispose() {} };
          },
          onDidCreate(listener) {
            createListeners.push(listener);
            return { dispose() {} };
          },
          onDidDelete(listener) {
            deleteListeners.push(listener);
            return { dispose() {} };
          },
          dispose() {}
        };

        fileWatchers.push({
          fireChange(uri) {
            for (const listener of changeListeners) {
              listener(uri);
            }
          },
          fireCreate(uri) {
            for (const listener of createListeners) {
              listener(uri);
            }
          },
          fireDelete(uri) {
            for (const listener of deleteListeners) {
              listener(uri);
            }
          }
        });

        return watcher;
      }
    },
    Uri: {
      file(value) {
        return {
          fsPath: value,
          toString() {
            return `file://${value}`;
          }
        };
      },
      parse(value) {
        return {
          fsPath: value.replace(/^file:\/\//, ''),
          toString() {
            return value;
          }
        };
      }
    },
    commands: {
      registerCommand() {
        return {
          dispose() {}
        };
      }
    },
    ProgressLocation: {
      Notification: 15
    },
    ViewColumn: {
      Beside: 2
    },
    __fireDidChangeTextDocument(event) {
      for (const listener of didChangeTextDocumentListeners) {
        listener(event);
      }
    },
    __fireDidSaveTextDocument(document) {
      for (const listener of didSaveTextDocumentListeners) {
        listener(document);
      }
    },
    __fileWatchers: fileWatchers
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

test('register clears cached project evaluation state after saving a project file', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const context = {
    subscriptions: []
  };

  controller.projectInfoCache.set('/tmp::/tmp/App.csproj::<auto>', { targetPath: '/tmp/bin/App.dll' });
  controller.projectReferenceCache.set('/tmp/Host.csproj::/tmp/App.csproj', true);

  controller.register(context);
  vscodeMock.__fireDidSaveTextDocument({
    uri: {
      fsPath: '/tmp/App.csproj',
      toString() {
        return 'file:///tmp/App.csproj';
      }
    }
  });

  assert.equal(controller.projectInfoCache.size, 0);
  assert.equal(controller.projectReferenceCache.size, 0);
});

test('register clears cached project evaluation state after watched project changes', () => {
  const vscodeMock = createVscodeMock();
  const controller = createController(vscodeMock);
  const context = {
    subscriptions: []
  };

  controller.projectInfoCache.set('/tmp::/tmp/App.csproj::<auto>', { targetPath: '/tmp/bin/App.dll' });
  controller.projectReferenceCache.set('/tmp/Host.csproj::/tmp/App.csproj', true);

  controller.register(context);
  assert.equal(vscodeMock.__fileWatchers.length, 1);

  vscodeMock.__fileWatchers[0].fireChange({
    fsPath: '/tmp/App.csproj',
    toString() {
      return 'file:///tmp/App.csproj';
    }
  });

  assert.equal(controller.projectInfoCache.size, 0);
  assert.equal(controller.projectReferenceCache.size, 0);
});

test('buildPreviewHostExitStatus prefers the captured crash summary', () => {
  const vscodeMock = createVscodeMock();
  const { buildPreviewHostExitStatus } = loadPreviewSupport(vscodeMock);

  assert.equal(
    buildPreviewHostExitStatus({
      exitCode: 134,
      error: "System.TypeLoadException: Could not load type 'Example.MissingType'."
    }),
    "Preview host crashed (134): System.TypeLoadException: Could not load type 'Example.MissingType'.");
});

test('describePreviewCompilerMode reports AXSG for source-generated preview', () => {
  const vscodeMock = createVscodeMock();
  const { describePreviewCompilerMode } = loadPreviewSupport(vscodeMock);

  assert.deepEqual(describePreviewCompilerMode('sourceGenerated'), {
    kind: 'axsg',
    label: 'AXSG',
    title: 'Using AXSG source-generated preview compiler.'
  });
});

test('describePreviewCompilerMode reports XamlX for Avalonia preview', () => {
  const vscodeMock = createVscodeMock();
  const { describePreviewCompilerMode } = loadPreviewSupport(vscodeMock);

  assert.deepEqual(describePreviewCompilerMode('avalonia'), {
    kind: 'xamlx',
    label: 'XamlX',
    title: 'Using Avalonia XamlX preview compiler.'
  });
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

test('buildStartAttempts prefers the project fallback first after a bundled-host failure in the same session', () => {
  const vscodeMock = createVscodeMock();
  const { buildStartAttempts } = loadPreviewSupport(vscodeMock);
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-support-'));

  try {
    const bundledHostPath = path.join(tempRoot, 'designer-host', 'XamlToCSharpGenerator.Previewer.DesignerHost.dll');
    const projectHostPath = path.join(tempRoot, 'project', 'Avalonia.Designer.HostApp.dll');
    const hostTargetPath = path.join(tempRoot, 'app', 'TestApp.dll');

    fs.mkdirSync(path.dirname(bundledHostPath), { recursive: true });
    fs.mkdirSync(path.dirname(projectHostPath), { recursive: true });
    fs.mkdirSync(path.dirname(hostTargetPath), { recursive: true });
    fs.writeFileSync(bundledHostPath, '');
    fs.writeFileSync(projectHostPath, '');
    fs.writeFileSync(hostTargetPath, '');

    const launchInfo = {
      previewPlan: {
        requestedMode: 'avalonia',
        preferredMode: 'avalonia'
      },
      hostProject: {
        previewerToolPath: projectHostPath,
        targetPath: hostTargetPath
      }
    };

    const defaultAttempts = buildStartAttempts(tempRoot, launchInfo, false);
    const stickyFallbackAttempts = buildStartAttempts(tempRoot, launchInfo, true);

    assert.equal(defaultAttempts[0].label, 'Avalonia XamlX');
    assert.equal(defaultAttempts[0].isBundledDesignerHost, true);
    assert.equal(defaultAttempts[1].label, 'Avalonia XamlX (project host fallback)');
    assert.equal(defaultAttempts[1].useProjectHostRuntime, true);

    assert.equal(stickyFallbackAttempts[0].label, 'Avalonia XamlX (project host fallback)');
    assert.equal(stickyFallbackAttempts[0].useProjectHostRuntime, true);
    assert.equal(stickyFallbackAttempts[1].label, 'Avalonia XamlX');
    assert.equal(stickyFallbackAttempts[1].isBundledDesignerHost, true);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});
