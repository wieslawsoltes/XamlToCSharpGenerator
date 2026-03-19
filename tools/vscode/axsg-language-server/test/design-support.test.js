const test = require('node:test');
const assert = require('node:assert/strict');
const Module = require('node:module');
const {
  DESIGN_TOOLBOX_ITEM_MIME,
  DESIGN_TOOLBOX_TEXT_MIME,
  DESIGN_TOOLBOX_TEXT_PREFIX
} = require('../design-toolbox-dnd');

function createVscodeMock() {
  class EventEmitter {
    constructor() {
      this.event = () => {};
    }

    fire() {}
  }

  return {
    EventEmitter,
    DataTransferItem: class DataTransferItem {
      constructor(value) {
        this.value = value;
      }

      async asString() {
        return typeof this.value === 'string'
          ? this.value
          : JSON.stringify(this.value);
      }
    },
    DocumentDropEdit: class DocumentDropEdit {
      constructor(insertText, title) {
        this.insertText = insertText;
        this.title = title;
      }
    },
    TreeItem: class TreeItem {},
    TreeItemCollapsibleState: {
      None: 0,
      Collapsed: 1,
      Expanded: 2
    },
    window: {
      async showInformationMessage() {},
      async showErrorMessage() {},
      async showWarningMessage() {}
    },
    workspace: {},
    Uri: {
      file: value => ({ fsPath: value })
    },
    Range: class Range {},
    Selection: class Selection {}
  };
}

function loadDesignSupport(vscodeMock) {
  const modulePath = require.resolve('../design-support');
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

function createController() {
  const vscodeMock = createVscodeMock();
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const updates = [];
  const context = {
    workspaceState: {
      get: () => null,
      update: async (key, value) => {
        updates.push({ key, value });
      }
    }
  };

  const controller = new DesignSessionController({
    context,
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });

  return { controller, updates };
}

async function waitFor(predicate) {
  for (let index = 0; index < 20; index += 1) {
    if (predicate()) {
      return;
    }

    await new Promise(resolve => setImmediate(resolve));
  }

  throw new Error('Timed out waiting for predicate.');
}

test('selectElement uses sourceElementId for live tree nodes', async () => {
  const { controller } = createController();
  const sentCommands = [];

  controller.currentSession = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/Preview.axaml',
    elements: [{ id: '0', children: [] }]
  };

  await controller.selectElement(
    {
      id: 'live:0/0/0',
      sourceElementId: '0/0/0',
      sourceBuildUri: 'avares://tests/Preview.axaml'
    },
    { revealEditor: false });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Preview.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
});

test('selectElement reveals the editor even when the target is already selected', async () => {
  const { controller, updates } = createController();
  const sentCommands = [];
  let revealedElement = null;

  controller.currentSession = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };
  controller.workspace = {
    activeBuildUri: 'avares://tests/Preview.axaml',
    selectedElementId: '0/0/0',
    elements: [
      {
        id: '0/0/0',
        sourceBuildUri: 'avares://tests/Preview.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };
  controller.revealElementInEditor = async element => {
    revealedElement = element;
  };

  await controller.selectElement(
    {
      id: 'live:0/0/0',
      sourceElementId: '0/0/0',
      sourceBuildUri: 'avares://tests/Preview.axaml'
    },
    { revealEditor: true });

  assert.deepEqual(sentCommands, []);
  assert.equal(revealedElement?.id, '0/0/0');
  assert.deepEqual(updates, [
    {
      key: 'axsg.design.selectedDocument',
      value: {
        __default__: 'avares://tests/Preview.axaml'
      }
    }
  ]);
});

test('selectElement does not skip selection when the element id matches but the source document changed', async () => {
  const { controller, updates } = createController();
  const sentCommands = [];

  controller.currentSession = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/First.axaml',
    selectedElementId: '0/0/0',
    elements: []
  };

  await controller.selectElement(
    {
      id: '0/0/0',
      sourceBuildUri: 'avares://tests/Second.axaml',
      sourceRange: {
        startOffset: 0,
        endOffset: 24
      },
      children: []
    },
    { revealEditor: false });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Second.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
  assert.deepEqual(updates, [
    {
      key: 'axsg.design.selectedDocument',
      value: {
        __default__: 'avares://tests/Second.axaml'
      }
    }
  ]);
});

test('syncEditorSelectionAsync uses sourceElementId when the workspace element exposes one', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const session = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => session;
  controller.currentSession = session;
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/Preview.axaml',
    selectedElementId: null,
    elements: [
      {
        id: 'live:0/0/0',
        sourceElementId: '0/0/0',
        sourceBuildUri: 'avares://tests/Preview.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };

  await controller.syncEditorSelectionAsync({
    document: {
      uri: {
        toString: () => 'file:///tmp/Preview.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Preview.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
});

test('resolveSessionForEditor falls back to the current preview session for workspace documents without direct session mapping', () => {
  const { controller } = createController();
  const currentSession = {
    documentUri: 'file:///tmp/Launch.axaml',
    setDesignState() {}
  };
  const editor = {
    document: {
      fileName: '/tmp/Secondary.axaml',
      uri: {
        fsPath: '/tmp/Secondary.axaml',
        toString: () => 'file:///tmp/Secondary.axaml'
      }
    }
  };

  controller.currentSession = currentSession;
  controller.workspace = {
    documents: [
      {
        buildUri: 'avares://tests/Launch.axaml',
        sourcePath: '/tmp/Launch.axaml'
      },
      {
        buildUri: 'avares://tests/Secondary.axaml',
        sourcePath: '/tmp/Secondary.axaml'
      }
    ]
  };
  controller.previewController.getSession = () => null;
  controller.previewController.getActiveSession = () => null;

  assert.equal(controller.resolveSessionForEditor(editor), currentSession);
});

test('syncEditorSelectionAsync remembers the selected document buildUri before syncing runtime selection', async () => {
  const { controller, updates } = createController();
  const sentCommands = [];
  const session = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => session;
  controller.currentSession = session;
  controller.selectedDocumentBuildUri = 'avares://tests/First.axaml';
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/Second.axaml',
    selectedElementId: null,
    elements: [
      {
        id: '0/0/0',
        sourceBuildUri: 'avares://tests/Second.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };

  await controller.syncEditorSelectionAsync({
    document: {
      uri: {
        toString: () => 'file:///tmp/Preview.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.equal(controller.selectedDocumentBuildUri, 'avares://tests/Second.axaml');
  assert.deepEqual(updates, [
    {
      key: 'axsg.design.selectedDocument',
      value: {
        __default__: 'avares://tests/Second.axaml'
      }
    }
  ]);
  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Second.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
});

test('syncEditorSelectionAsync uses the current preview session for secondary workspace documents', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const session = {
    documentUri: 'file:///tmp/Launch.axaml',
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => null;
  controller.currentSession = session;
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/Secondary.axaml',
    selectedElementId: null,
    documents: [
      {
        buildUri: 'avares://tests/Launch.axaml',
        sourcePath: '/tmp/Launch.axaml'
      },
      {
        buildUri: 'avares://tests/Secondary.axaml',
        sourcePath: '/tmp/Secondary.axaml'
      }
    ],
    elements: [
      {
        id: '0/0/0',
        sourceBuildUri: 'avares://tests/Secondary.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };

  await controller.syncEditorSelectionAsync({
    document: {
      fileName: '/tmp/Secondary.axaml',
      uri: {
        fsPath: '/tmp/Secondary.axaml',
        toString: () => 'file:///tmp/Secondary.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Secondary.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
});

test('syncEditorSelectionAsync does not skip selection when another workspace document has the same element id', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const session = {
    documentUri: 'file:///tmp/Launch.axaml',
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => null;
  controller.currentSession = session;
  controller.refreshFromSession = async () => {};
  controller.workspace = {
    activeBuildUri: 'avares://tests/First.axaml',
    selectedElementId: '0/0/0',
    documents: [
      {
        buildUri: 'avares://tests/First.axaml',
        sourcePath: '/tmp/First.axaml'
      },
      {
        buildUri: 'avares://tests/Second.axaml',
        sourcePath: '/tmp/Second.axaml'
      }
    ],
    elements: [
      {
        id: '0/0/0',
        sourceBuildUri: 'avares://tests/Second.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };

  await controller.syncEditorSelectionAsync({
    document: {
      fileName: '/tmp/Second.axaml',
      uri: {
        fsPath: '/tmp/Second.axaml',
        toString: () => 'file:///tmp/Second.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Second.axaml',
        elementId: '0/0/0'
      }
    }
  ]);
});

test('revealSelectedElementInTreeAsync matches workspace selection by sourceElementId', async () => {
  const { controller } = createController();
  let revealedElement = null;

  controller.workspace = {
    selectedElementId: '0/0/0'
  };

  await controller.revealSelectedElementInTreeAsync(
    {
      async reveal(element) {
        revealedElement = element;
      }
    },
    {
      elements: [
        {
          id: 'live:0',
          sourceElementId: '0',
          children: [
            {
              id: 'live:0/0',
              sourceElementId: '0/0',
              children: [
                {
                  id: 'live:0/0/0',
                  sourceElementId: '0/0/0',
                  children: []
                }
              ]
            }
          ]
        }
      ]
    });

  assert.equal(revealedElement?.id, 'live:0/0/0');
});

test('disconnected transport detection includes disposed and peer reset messages', () => {
  const { controller } = createController();

  assert.equal(controller.isDisconnectedTransportMessage('Preview host was disposed.'), true);
  assert.equal(controller.isDisconnectedTransportMessage('Connection reset by peer'), true);
  assert.equal(controller.isDisconnectedTransportMessage('Some unrelated error'), false);
});

test('handleSessionEvent refreshes previewStarted session when no active session is resolved yet', async () => {
  const { controller } = createController();
  const previewSession = { setDesignState() {} };
  const refreshes = [];

  controller.currentSession = null;
  controller.refreshFromSession = async (session, reason) => {
    refreshes.push({ session, reason });
  };

  await controller.handleSessionEvent({
    session: previewSession,
    event: 'previewStarted',
    payload: {}
  });

  assert.deepEqual(refreshes, [
    {
      session: previewSession,
      reason: 'previewStarted'
    }
  ]);
});

test('handleSessionEvent refreshes panelActivated session when it is active', async () => {
  const { controller } = createController();
  const previewSession = { setDesignState() {} };
  const refreshes = [];

  controller.previewController.getActiveSession = () => previewSession;
  controller.refreshFromSession = async (session, reason) => {
    refreshes.push({ session, reason });
  };

  await controller.handleSessionEvent({
    session: previewSession,
    event: 'panelActivated',
    payload: {}
  });

  assert.deepEqual(refreshes, [
    {
      session: previewSession,
      reason: 'panelActivated'
    }
  ]);
});

test('handleSessionEvent defers updateResult refresh instead of refreshing immediately', async () => {
  const { controller } = createController();
  const previewSession = { setDesignState() {} };
  const scheduled = [];
  const refreshes = [];

  controller.previewController.getActiveSession = () => previewSession;
  controller.scheduleUpdateResultRefresh = session => {
    scheduled.push(session);
  };
  controller.refreshFromSession = async (session, reason) => {
    refreshes.push({ session, reason });
  };

  await controller.handleSessionEvent({
    session: previewSession,
    event: 'updateResult',
    payload: {
      succeeded: true
    }
  });

  assert.deepEqual(scheduled, [previewSession]);
  assert.deepEqual(refreshes, []);
});

test('handleSessionEvent clears stale inspector state when the preview host exits', async () => {
  const { controller } = createController();
  const designStates = [];
  const previewSession = {
    setDesignState(state) {
      designStates.push(state);
    }
  };

  controller.previewController.getActiveSession = () => previewSession;
  controller.currentSession = previewSession;
  controller.workspace = {
    mode: 'Design',
    hitTestMode: 'Visual',
    activeBuildUri: 'avares://tests/MainView.axaml',
    documents: [{ buildUri: 'avares://tests/MainView.axaml' }]
  };
  controller.logicalTree = { elements: [{ id: 'live:0', children: [] }] };
  controller.visualTree = { elements: [{ id: 'live:0', children: [] }] };
  controller.overlay = { highlightedElementId: 'live:0' };

  await controller.handleSessionEvent({
    session: previewSession,
    event: 'hostExited',
    payload: {
      exitCode: 5
    }
  });

  assert.equal(controller.workspace, null);
  assert.equal(controller.logicalTree, null);
  assert.equal(controller.visualTree, null);
  assert.equal(controller.overlay, null);
  assert.deepEqual(designStates, [
    {
      available: false,
      reason: 'hostExited',
      message: 'Preview host exited (5). Restart the preview to repopulate the AXSG Inspector.'
    }
  ]);
});

test('handleSessionWebviewMessage adopts the active preview session before processing design input', async () => {
  const { controller } = createController();
  const previousDesignStates = [];
  let selectRequest = null;
  const previousSession = {
    setDesignState(state) {
      previousDesignStates.push(state);
    }
  };
  const activeSession = {
    panel: {
      active: true
    },
    setDesignState() {}
  };

  controller.currentSession = previousSession;
  controller.workspace = {
    activeBuildUri: 'avares://tests/Other.axaml'
  };
  controller.refreshFromSession = async () => {};
  controller.selectAtPoint = async (session, x, y, updateSelection) => {
    selectRequest = {
      session,
      x,
      y,
      updateSelection,
      currentSession: controller.currentSession,
      workspace: controller.workspace
    };
  };

  const handled = controller.handleSessionWebviewMessage(activeSession, {
    type: 'designSelectAtPoint',
    x: 10,
    y: 20
  });
  await Promise.resolve();

  assert.equal(handled, true);
  assert.equal(controller.currentSession, activeSession);
  assert.equal(controller.workspace, null);
  assert.deepEqual(previousDesignStates, [null]);
  assert.deepEqual(selectRequest, {
    session: activeSession,
    x: 10,
    y: 20,
    updateSelection: true,
    currentSession: activeSession,
    workspace: null
  });
});

test('ensureSessionPreferencesAsync retries saved document selection when documents appear later', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const session = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.currentSession = session;
  controller.selectedDocumentBuildUri = 'avares://tests/Second.axaml';
  controller.loadSessionStateAsync = async () => {};
  controller.workspace = {
    mode: 'Interactive',
    hitTestMode: 'Logical',
    activeBuildUri: 'avares://tests/First.axaml',
    documents: []
  };

  await controller.ensureSessionPreferencesAsync(session);
  assert.deepEqual(sentCommands, []);

  controller.workspace = {
    mode: 'Interactive',
    hitTestMode: 'Logical',
    activeBuildUri: 'avares://tests/First.axaml',
    documents: [
      { buildUri: 'avares://tests/First.axaml' },
      { buildUri: 'avares://tests/Second.axaml' }
    ]
  };

  await controller.ensureSessionPreferencesAsync(session);

  assert.deepEqual(sentCommands, [
    {
      command: 'selectDocument',
      payload: {
        buildUri: 'avares://tests/Second.axaml'
      }
    }
  ]);
});

test('performRefreshFromSession uses selected document preferences scoped to the preview session', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const sentCommands = [];
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: key => {
          if (key === 'axsg.design.selectedDocument') {
            return {
              'file:///first.axaml': 'avares://tests/First.axaml',
              'file:///second.axaml': 'avares://tests/Second.axaml'
            };
          }

          return null;
        },
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });
  const session = {
    documentUri: 'file:///second.axaml',
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.loadSessionStateAsync = async currentSession => {
    controller.currentSession = currentSession;
    controller.workspace = {
      mode: 'Design',
      hitTestMode: 'Logical',
      activeBuildUri: 'avares://tests/First.axaml',
      documents: [
        { buildUri: 'avares://tests/First.axaml' },
        { buildUri: 'avares://tests/Second.axaml' }
      ]
    };
  };
  controller.refreshProviders = () => {};
  controller.publishPreviewDesignState = () => {};
  controller.revealCurrentSelectionAsync = async () => {};

  await controller.performRefreshFromSessionAsync(session, 'previewStarted');

  assert.equal(controller.selectedDocumentBuildUri, 'avares://tests/Second.axaml');
  assert.deepEqual(sentCommands, [
    {
      command: 'selectDocument',
      payload: {
        buildUri: 'avares://tests/Second.axaml'
      }
    }
  ]);
});

test('refreshFromActiveSession keeps the current preview session for active secondary workspace editors', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: () => null,
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });
  const session = {
    documentUri: 'file:///tmp/Launch.axaml',
    setDesignState() {}
  };
  const editor = {
    document: {
      fileName: '/tmp/Secondary.axaml',
      uri: {
        fsPath: '/tmp/Secondary.axaml',
        toString: () => 'file:///tmp/Secondary.axaml'
      }
    }
  };
  const refreshes = [];

  controller.currentSession = session;
  controller.workspace = {
    documents: [
      {
        buildUri: 'avares://tests/Launch.axaml',
        sourcePath: '/tmp/Launch.axaml'
      },
      {
        buildUri: 'avares://tests/Secondary.axaml',
        sourcePath: '/tmp/Secondary.axaml'
      }
    ]
  };
  controller.refreshFromSession = async (resolvedSession, reason) => {
    refreshes.push({ session: resolvedSession, reason });
  };

  await controller.refreshFromActiveSession('activeEditor', editor);

  assert.deepEqual(refreshes, [
    {
      session,
      reason: 'activeEditor'
    }
  ]);
});

test('performRefreshFromSession does not reuse another preview session selected document preference', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const sentCommands = [];
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: key => {
          if (key === 'axsg.design.selectedDocument') {
            return {
              'file:///first.axaml': 'avares://tests/First.axaml'
            };
          }

          return null;
        },
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });
  const session = {
    documentUri: 'file:///second.axaml',
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.loadSessionStateAsync = async currentSession => {
    controller.currentSession = currentSession;
    controller.workspace = {
      mode: 'Design',
      hitTestMode: 'Logical',
      activeBuildUri: 'avares://tests/Second.axaml',
      documents: [
        { buildUri: 'avares://tests/First.axaml' },
        { buildUri: 'avares://tests/Second.axaml' }
      ]
    };
  };
  controller.refreshProviders = () => {};
  controller.publishPreviewDesignState = () => {};
  controller.revealCurrentSelectionAsync = async () => {};

  await controller.performRefreshFromSessionAsync(session, 'previewStarted');

  assert.equal(controller.selectedDocumentBuildUri, null);
  assert.deepEqual(sentCommands, []);
});

test('refreshFromSession does not remap workspace documents onto the preview session', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  let syncCalls = 0;
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: () => null,
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      },
      syncSessionDocuments() {
        syncCalls++;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });
  const session = { setDesignState() {} };

  controller.loadSessionStateAsync = async () => {
    controller.workspace = {
      mode: 'Design',
      hitTestMode: 'Logical',
      activeBuildUri: 'avares://tests/MainView.axaml',
      documents: [
        {
          buildUri: 'avares://tests/MainView.axaml',
          sourcePath: '/tmp/MainView.axaml'
        },
        {
          buildUri: 'avares://tests/Secondary.axaml',
          sourcePath: '/tmp/Secondary.axaml'
        }
      ]
    };
  };
  controller.ensureSessionPreferencesAsync = async () => {};
  controller.refreshProviders = () => {};
  controller.publishPreviewDesignState = () => {};
  controller.revealCurrentSelectionAsync = async () => {};

  await controller.refreshFromSession(session, 'test');

  assert.equal(syncCalls, 0);
});

test('flushScheduledUpdateResultRefreshAsync reschedules while preview updates are still pending', async () => {
  const { controller } = createController();
  const session = {
    hasPendingPreviewUpdate() {
      return true;
    },
    setDesignState() {}
  };
  const scheduled = [];
  const refreshes = [];

  controller.pendingUpdateResultRefreshSession = session;
  controller.scheduleUpdateResultRefresh = (scheduledSession, delayMs) => {
    scheduled.push({ session: scheduledSession, delayMs });
  };
  controller.refreshFromSession = async (refreshSession, reason) => {
    refreshes.push({ session: refreshSession, reason });
  };

  await controller.flushScheduledUpdateResultRefreshAsync(session);

  assert.deepEqual(scheduled, [
    {
      session,
      delayMs: 120
    }
  ]);
  assert.deepEqual(refreshes, []);
});

test('flushScheduledUpdateResultRefreshAsync refreshes once preview updates are idle', async () => {
  const { controller } = createController();
  const session = {
    hasPendingPreviewUpdate() {
      return false;
    },
    setDesignState() {}
  };
  const refreshes = [];

  controller.pendingUpdateResultRefreshSession = session;
  controller.refreshFromSession = async (refreshSession, reason) => {
    refreshes.push({ session: refreshSession, reason });
  };

  await controller.flushScheduledUpdateResultRefreshAsync(session);

  assert.deepEqual(refreshes, [
    {
      session,
      reason: 'updateResult'
    }
  ]);
  assert.equal(controller.pendingUpdateResultRefreshSession, null);
});

test('syncEditorSelectionAsync refreshes and retries when a stale element id no longer exists', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const refreshReasons = [];
  const session = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      if (sentCommands.length === 1) {
        throw new Error("No element with id '0/0/1/2/0' exists in buildUri 'avares://tests/Preview.axaml'.");
      }

      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => session;
  controller.currentSession = session;
  controller.workspace = {
    activeBuildUri: 'avares://tests/Preview.axaml',
    selectedElementId: null,
    elements: [
      {
        id: '0/0/1/2/0',
        sourceBuildUri: 'avares://tests/Preview.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };
  controller.refreshFromSession = async (_session, reason) => {
    refreshReasons.push(reason);
    if (reason === 'editorSelectionRetry') {
      controller.workspace = {
        activeBuildUri: 'avares://tests/Preview.axaml',
        selectedElementId: null,
        elements: [
          {
            id: '0/0/1/3/0',
            sourceBuildUri: 'avares://tests/Preview.axaml',
            sourceRange: {
              startOffset: 0,
              endOffset: 24
            },
            children: []
          }
        ]
      };
    }
  };

  await controller.syncEditorSelectionAsync({
    document: {
      uri: {
        toString: () => 'file:///tmp/Preview.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.deepEqual(sentCommands, [
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Preview.axaml',
        elementId: '0/0/1/2/0'
      }
    },
    {
      command: 'selectElement',
      payload: {
        buildUri: 'avares://tests/Preview.axaml',
        elementId: '0/0/1/3/0'
      }
    }
  ]);
  assert.deepEqual(refreshReasons, ['editorSelectionRetry', 'editorSelectionSync']);
});

test('performSelectAtPointAsync remembers the selected document buildUri from hit test results', async () => {
  const { controller, updates } = createController();
  let revealedElement = null;
  const session = {
    async sendDesignCommand(command) {
      assert.equal(command, 'selectAtPoint');
      return {
        succeeded: true,
        activeBuildUri: 'avares://tests/Second.axaml',
        elementId: '0/0/0',
        element: {
          id: '0/0/0',
          sourceBuildUri: 'avares://tests/Second.axaml',
          sourceRange: {
            startOffset: 0,
            endOffset: 24
          },
          children: []
        },
        overlay: {
          selectedElementId: '0/0/0'
        }
      };
    },
    setDesignState() {}
  };

  controller.currentSession = session;
  controller.selectedDocumentBuildUri = 'avares://tests/First.axaml';
  controller.workspace = {
    activeBuildUri: 'avares://tests/First.axaml',
    mode: 'Design',
    elements: []
  };
  controller.refreshFromSession = async () => {};
  controller.revealElementInEditor = async element => {
    revealedElement = element;
  };

  await controller.performSelectAtPointAsync(session, 10, 20, true);

  assert.equal(controller.selectedDocumentBuildUri, 'avares://tests/Second.axaml');
  assert.deepEqual(updates, [
    {
      key: 'axsg.design.selectedDocument',
      value: {
        __default__: 'avares://tests/Second.axaml'
      }
    }
  ]);
  assert.equal(revealedElement?.sourceBuildUri, 'avares://tests/Second.axaml');
});

test('syncEditorSelectionAsync skips runtime selection sync while interactive mode is active', async () => {
  const { controller } = createController();
  const sentCommands = [];
  const session = {
    async sendDesignCommand(command, payload) {
      sentCommands.push({ command, payload });
      return {};
    },
    setDesignState() {}
  };

  controller.previewController.getSession = () => session;
  controller.currentSession = session;
  controller.workspace = {
    activeBuildUri: 'avares://tests/Preview.axaml',
    mode: 'Interactive',
    selectedElementId: null,
    elements: [
      {
        id: '0/0/0',
        sourceBuildUri: 'avares://tests/Preview.axaml',
        sourceRange: {
          startOffset: 0,
          endOffset: 24
        },
        children: []
      }
    ]
  };

  await controller.syncEditorSelectionAsync({
    document: {
      uri: {
        toString: () => 'file:///tmp/Preview.axaml'
      },
      offsetAt() {
        return 4;
      }
    },
    selection: {
      active: {}
    }
  });

  assert.deepEqual(sentCommands, []);
});

test('refreshFromSession coalesces overlapping refresh requests onto the latest pending state', async () => {
  const { controller } = createController();
  const session = { setDesignState() {} };
  const reasons = [];
  let inFlight = 0;
  let maxInFlight = 0;
  let releaseFirstRefresh = null;

  controller.performRefreshFromSessionAsync = async (_session, reason) => {
    reasons.push(reason);
    inFlight += 1;
    maxInFlight = Math.max(maxInFlight, inFlight);
    try {
      if (reason === 'first') {
        await new Promise(resolve => {
          releaseFirstRefresh = resolve;
        });
      }
    } finally {
      inFlight -= 1;
    }
  };

  const firstRefresh = controller.refreshFromSession(session, 'first');
  const secondRefresh = controller.refreshFromSession(session, 'second');
  const thirdRefresh = controller.refreshFromSession(session, 'third');
  await Promise.resolve();
  releaseFirstRefresh();
  await Promise.all([firstRefresh, secondRefresh, thirdRefresh]);

  assert.deepEqual(reasons, ['first', 'third']);
  assert.equal(maxInFlight, 1);
});

test('loadSessionStateAsync loads design snapshots sequentially', async () => {
  const { controller } = createController();
  const commands = [];
  let inFlight = 0;
  let maxInFlight = 0;
  const session = {
    async sendDesignCommand(command) {
      commands.push(command);
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await Promise.resolve();
      inFlight -= 1;

      switch (command) {
        case 'workspace.current':
          return { activeBuildUri: 'avares://tests/MainView.axaml', mode: 'Design' };
        case 'tree.logical':
          return { elements: [{ id: 'logical:0', children: [] }] };
        case 'tree.visual':
          return { elements: [{ id: 'visual:0', children: [] }] };
        case 'overlay.current':
          return { selectedElementId: '0/0/0' };
        default:
          return null;
      }
    }
  };

  controller.currentSession = session;
  await controller.loadSessionStateAsync(session);

  assert.deepEqual(commands, [
    'workspace.current',
    'tree.logical',
    'tree.visual',
    'overlay.current'
  ]);
  assert.equal(maxInFlight, 1);
  assert.deepEqual(controller.workspace, { activeBuildUri: 'avares://tests/MainView.axaml', mode: 'Design' });
  assert.deepEqual(controller.logicalTree, { elements: [{ id: 'logical:0', children: [] }] });
  assert.deepEqual(controller.visualTree, { elements: [{ id: 'visual:0', children: [] }] });
  assert.deepEqual(controller.overlay, { selectedElementId: '0/0/0' });
});

test('loadSessionStateAsync skips tree and overlay queries while interactive mode is active', async () => {
  const { controller } = createController();
  const commands = [];
  const session = {
    async sendDesignCommand(command) {
      commands.push(command);
      if (command === 'workspace.current') {
        return {
          activeBuildUri: 'avares://tests/MainView.axaml',
          mode: 'Interactive'
        };
      }

      throw new Error(`Unexpected command: ${command}`);
    }
  };

  controller.currentSession = session;
  controller.logicalTree = { elements: [{ id: 'stale-logical', children: [] }] };
  controller.visualTree = { elements: [{ id: 'stale-visual', children: [] }] };
  controller.overlay = { selectedElementId: 'stale' };

  await controller.loadSessionStateAsync(session);

  assert.deepEqual(commands, ['workspace.current']);
  assert.equal(controller.logicalTree, null);
  assert.equal(controller.visualTree, null);
  assert.equal(controller.overlay, null);
});

test('insertToolboxItemAtPoint aborts insertion when hit testing misses a parent', async () => {
  const { controller } = createController();
  let insertCalls = 0;

  controller.selectAtPoint = async () => ({
    succeeded: false,
    elementId: null
  });
  controller.insertToolboxItem = async () => {
    insertCalls++;
  };

  await controller.insertToolboxItemAtPoint(
    { setDesignState() {} },
    { name: 'Button' },
    10,
    20);

  assert.equal(insertCalls, 0);
});

test('insertToolboxItemAtPoint inserts after a successful hit test', async () => {
  const { controller } = createController();
  let insertCalls = 0;

  controller.selectAtPoint = async () => ({
    succeeded: true,
    elementId: '0/0/1'
  });
  controller.insertToolboxItem = async () => {
    insertCalls++;
  };

  await controller.insertToolboxItemAtPoint(
    { setDesignState() {} },
    { name: 'Button' },
    10,
    20);

  assert.equal(insertCalls, 1);
});

test('selectAtPoint collapses overlapping hover hit tests to the latest pending point', async () => {
  const { controller } = createController();
  const payloads = [];
  let releaseFirstHover = null;

  controller.currentSession = {
    setDesignState() {}
  };
  controller.publishPreviewDesignState = () => {};

  const session = {
    async sendDesignCommand(command, payload) {
      assert.equal(command, 'selectAtPoint');
      payloads.push(payload);
      if (payloads.length === 1) {
        await new Promise(resolve => {
          releaseFirstHover = resolve;
        });
      }

      return { overlay: { highlightedElementId: payload.x + ':' + payload.y } };
    }
  };

  controller.hitTestMode = 'Logical';
  const firstHover = controller.selectAtPoint(session, 10, 20, false);
  const secondHover = controller.selectAtPoint(session, 30, 40, false);
  const thirdHover = controller.selectAtPoint(session, 50, 60, false);
  await waitFor(() => typeof releaseFirstHover === 'function');
  releaseFirstHover();
  await Promise.all([firstHover, secondHover, thirdHover]);

  assert.deepEqual(payloads, [
    {
      buildUri: undefined,
      x: 10,
      y: 20,
      updateSelection: false,
      hitTestMode: 'Logical'
    },
    {
      buildUri: undefined,
      x: 50,
      y: 60,
      updateSelection: false,
      hitTestMode: 'Logical'
    }
  ]);
  assert.deepEqual(controller.overlay, { highlightedElementId: '50:60' });
});

test('selectAtPoint clears pending hover work when a click selection arrives', async () => {
  const { controller } = createController();
  const payloads = [];
  let releaseFirstHover = null;

  controller.currentSession = {
    setDesignState() {}
  };
  controller.publishPreviewDesignState = () => {};
  controller.refreshFromSession = async () => {};

  const session = {
    async sendDesignCommand(command, payload) {
      assert.equal(command, 'selectAtPoint');
      payloads.push(payload);
      if (payloads.length === 1) {
        await new Promise(resolve => {
          releaseFirstHover = resolve;
        });
      }

      return { succeeded: true, overlay: { highlightedElementId: payload.x + ':' + payload.y } };
    }
  };

  controller.hitTestMode = 'Logical';
  const firstHover = controller.selectAtPoint(session, 10, 20, false);
  const secondHover = controller.selectAtPoint(session, 30, 40, false);
  const selection = controller.selectAtPoint(session, 50, 60, true);
  await waitFor(() => typeof releaseFirstHover === 'function');
  releaseFirstHover();
  await Promise.all([firstHover, secondHover, selection]);

  assert.deepEqual(payloads, [
    {
      buildUri: undefined,
      x: 10,
      y: 20,
      updateSelection: false,
      hitTestMode: 'Logical'
    },
    {
      buildUri: undefined,
      x: 50,
      y: 60,
      updateSelection: true,
      hitTestMode: 'Logical'
    }
  ]);
});

test('applyMutation flushes pending preview edits before sending the design command', async () => {
  const { controller } = createController();
  const steps = [];

  controller.currentSession = {
    async flushPendingPreviewUpdateAsync() {
      steps.push('flush');
    },
    async sendDesignCommand() {
      steps.push('command');
      return {
        applyResult: {
          succeeded: true,
          buildUri: 'avares://tests/MainView.axaml',
          minimalDiffStart: 0,
          minimalDiffRemovedLength: 0,
          minimalDiffInsertedLength: 0
        },
        workspace: {
          currentXamlText: '',
          documents: []
        }
      };
    },
    setDesignState() {}
  };
  controller.applyMinimalDiffToEditor = async () => {
    steps.push('diff');
  };
  controller.refreshFromSession = async () => {
    steps.push('refresh');
  };

  await controller.applyMutation('applyPropertyUpdate', { propertyName: 'Width' });

  assert.deepEqual(steps, ['flush', 'command', 'diff', 'refresh']);
});

test('applyMutation aborts when flushing pending preview edits fails', async () => {
  const vscodeMock = createVscodeMock();
  const errors = [];
  vscodeMock.window.showErrorMessage = async message => {
    errors.push(message);
  };
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: () => null,
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });
  let commandCalls = 0;
  controller.currentSession = {
    async flushPendingPreviewUpdateAsync() {
      throw new Error('Preview update failed; retry the inspector action after the preview is in sync.');
    },
    async sendDesignCommand() {
      commandCalls++;
      return {};
    },
    setDesignState() {}
  };

  await controller.applyMutation('insertElement', { elementName: 'Button' });

  assert.equal(commandCalls, 0);
  assert.deepEqual(errors, [
    'AXSG Design: Preview update failed; retry the inspector action after the preview is in sync.'
  ]);
});

test('applyMutation skips minimal diff when the source document changes while the mutation is in flight', async () => {
  const vscodeMock = createVscodeMock();
  const warnings = [];
  let applyEditCalls = 0;
  let refreshCalls = 0;
  let documentVersion = 1;
  let documentText = '<TextBox Width="100" />';
  const document = {
    get version() {
      return documentVersion;
    },
    getText() {
      return documentText;
    },
    positionAt(offset) {
      return { offset };
    }
  };

  vscodeMock.window.showWarningMessage = async message => {
    warnings.push(message);
  };
  vscodeMock.workspace.openTextDocument = async () => document;
  vscodeMock.workspace.applyEdit = async () => {
    applyEditCalls++;
    return true;
  };
  const { DesignSessionController } = loadDesignSupport(vscodeMock);
  const controller = new DesignSessionController({
    context: {
      workspaceState: {
        get: () => null,
        update: async () => {}
      }
    },
    previewController: {
      setDesignController() {},
      getActiveSession() {
        return null;
      },
      getSession() {
        return null;
      }
    },
    getOutputChannel: () => ({
      appendLine() {}
    }),
    isXamlDocument: () => true
  });

  controller.workspace = {
    activeBuildUri: 'avares://tests/MainView.axaml',
    documents: [
      {
        buildUri: 'avares://tests/MainView.axaml',
        sourcePath: '/tmp/MainView.axaml'
      }
    ]
  };
  controller.currentSession = {
    async flushPendingPreviewUpdateAsync() {},
    async sendDesignCommand() {
      documentVersion = 2;
      documentText = '<TextBox Width="100" /><Button />';
      return {
        applyResult: {
          succeeded: true,
          buildUri: 'avares://tests/MainView.axaml',
          minimalDiffStart: 22,
          minimalDiffRemovedLength: 0,
          minimalDiffInsertedLength: 10
        },
        workspace: {
          activeBuildUri: 'avares://tests/MainView.axaml',
          currentXamlText: '<TextBox Width="100" /><Button />',
          documents: [
            {
              buildUri: 'avares://tests/MainView.axaml',
              sourcePath: '/tmp/MainView.axaml'
            }
          ]
        }
      };
    },
    setDesignState() {}
  };
  controller.refreshFromSession = async () => {
    refreshCalls++;
  };

  await controller.applyMutation('insertElement', {
    buildUri: 'avares://tests/MainView.axaml',
    elementName: 'Button'
  });

  assert.equal(applyEditCalls, 0);
  assert.equal(refreshCalls, 1);
  assert.deepEqual(warnings, [
    'AXSG Design: The XAML document changed while the design edit was in flight, so the source update was skipped. Retry the action after the editor is idle.'
  ]);
});

test('toolbox drag controller publishes custom and text payloads', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignToolboxDragAndDropController } = loadDesignSupport(vscodeMock);
  const controller = new DesignToolboxDragAndDropController();
  const values = new Map();

  await controller.handleDrag([
    {
      name: 'Button',
      displayName: 'Button',
      xamlSnippet: '<Button Content="Button" />'
    }
  ], {
    set(key, value) {
      values.set(key, value);
    }
  });

  assert.equal(await values.get(DESIGN_TOOLBOX_ITEM_MIME).asString(), JSON.stringify({
    name: 'Button',
    displayName: 'Button',
    category: '',
    xamlSnippet: '<Button Content="Button" />',
    isProjectControl: false,
    tags: []
  }));
  assert.equal(
    await values.get(DESIGN_TOOLBOX_TEXT_MIME).asString(),
    `${DESIGN_TOOLBOX_TEXT_PREFIX}{"name":"Button","displayName":"Button","category":"","xamlSnippet":"<Button Content=\\"Button\\" />","isProjectControl":false,"tags":[]}`);
});

test('document drop provider creates edit for toolbox text payload', async () => {
  const vscodeMock = createVscodeMock();
  const { DesignToolboxDocumentDropProvider } = loadDesignSupport(vscodeMock);
  const provider = new DesignToolboxDocumentDropProvider();
  const edit = await provider.provideDocumentDropEdits(null, null, {
    get(mimeType) {
      if (mimeType !== DESIGN_TOOLBOX_TEXT_MIME) {
        return undefined;
      }

      return new vscodeMock.DataTransferItem(
        `${DESIGN_TOOLBOX_TEXT_PREFIX}{"name":"TextBlock","displayName":"TextBlock","category":"Text","xamlSnippet":"<TextBlock Text=\\"Text\\" />","isProjectControl":false,"tags":[]}`);
    }
  });

  assert.equal(edit.insertText, '<TextBlock Text="Text" />');
  assert.equal(edit.title, 'Insert TextBlock');
});

test('renderPropertiesViewHtml renders a property-grid layout with name filtering support', () => {
  const vscodeMock = createVscodeMock();
  const { renderPropertiesViewHtml } = loadDesignSupport(vscodeMock);
  const html = renderPropertiesViewHtml(
    { cspSource: 'vscode-test' },
    {
      displayName: 'TextBox',
      typeName: 'TextBox',
      xamlName: 'Editor'
    },
    [
      {
        name: 'Text',
        value: 'Hello',
        category: 'Common',
        typeName: 'String',
        source: 'Local',
        ownerTypeName: 'TextBox',
        editorKind: 'Text'
      },
      {
        name: 'LongText',
        value: 'x'.repeat(120),
        category: 'Common',
        typeName: 'String',
        source: 'Local',
        ownerTypeName: 'TextBox',
        editorKind: 'Text'
      }
    ],
    'Smart');

  assert.match(html, /id="property-name-filter"/);
  assert.match(html, /data-property-row/);
  assert.match(html, /data-property-search="text"/);
  assert.match(html, /id="property-summary">2 properties</);
  assert.match(html, /class="group-table"/);
  assert.match(html, /\[hidden\]\s*\{\s*display:\s*none\s*!important;/);
  assert.match(html, /<textarea class="editor-textarea" data-property-input/);
});

test('updateViewMessages uses interactive guidance for empty live trees', () => {
  const { controller } = createController();
  controller.currentSession = { setDesignState() {} };
  controller.workspace = {
    mode: 'Interactive',
    hitTestMode: 'Logical',
    documents: [],
    toolbox: []
  };
  controller.logicalTreeView = {};
  controller.visualTreeView = {};

  controller.updateViewMessages();

  assert.match(controller.logicalTreeView.message, /Interactive mode is active/);
  assert.match(controller.visualTreeView.message, /Interactive mode is active/);
});

test('renderPropertiesViewHtml shows inspector availability guidance when no selection exists', () => {
  const vscodeMock = createVscodeMock();
  const { renderPropertiesViewHtml } = loadDesignSupport(vscodeMock);
  const html = renderPropertiesViewHtml(
    { cspSource: 'vscode-test' },
    null,
    [],
    'Smart',
    {
      available: true,
      kind: 'interactiveReady',
      message: 'Interactive mode is active. Switch Mode to Design or Agent to inspect the logical tree and select elements from the preview surface.'
    });

  assert.match(html, /No selection/);
  assert.match(html, /Interactive mode is active\./);
});
