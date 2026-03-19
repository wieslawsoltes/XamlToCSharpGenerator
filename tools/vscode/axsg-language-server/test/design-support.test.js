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
