const path = require('path');
const vscode = require('vscode');
const {
  DESIGN_TOOLBOX_ITEM_MIME,
  DESIGN_TOOLBOX_TEXT_MIME,
  readToolboxItemFromDataTransfer,
  serializeToolboxItem,
  serializeToolboxItemAsText
} = require('./design-toolbox-dnd');

const WORKSPACE_MODE_STATE_KEY = 'axsg.design.workspaceMode';
const HIT_TEST_MODE_STATE_KEY = 'axsg.design.hitTestMode';
const SELECTED_DOCUMENT_STATE_KEY = 'axsg.design.selectedDocument';
const LOGICAL_EXPANDED_STATE_KEY = 'axsg.design.logicalExpanded';
const VISUAL_EXPANDED_STATE_KEY = 'axsg.design.visualExpanded';
const PANEL_VISIBILITY_STATE_KEY = 'axsg.design.panelVisibility';
const DEFAULT_WORKSPACE_MODE = 'Interactive';
const DEFAULT_HIT_TEST_MODE = 'Logical';
const EDITOR_SELECTION_DEBOUNCE_MS = 120;

class DesignSessionController {
  constructor(options) {
    this.context = options.context;
    this.previewController = options.previewController;
    this.getOutputChannel = options.getOutputChannel;
    this.isXamlDocument = options.isXamlDocument;
    this.currentSession = null;
    this.workspace = null;
    this.logicalTree = null;
    this.visualTree = null;
    this.overlay = null;
    this.suppressEditorSync = false;
    this.editorSelectionTimer = null;
    this.preferencesAppliedSessions = new WeakSet();
    this.logicalExpandedIds = new Set(this.context.workspaceState.get(LOGICAL_EXPANDED_STATE_KEY, []));
    this.visualExpandedIds = new Set(this.context.workspaceState.get(VISUAL_EXPANDED_STATE_KEY, []));
    this.workspaceMode = this.context.workspaceState.get(WORKSPACE_MODE_STATE_KEY, DEFAULT_WORKSPACE_MODE);
    this.hitTestMode = this.context.workspaceState.get(HIT_TEST_MODE_STATE_KEY, DEFAULT_HIT_TEST_MODE);
    this.selectedDocumentBuildUri = this.context.workspaceState.get(SELECTED_DOCUMENT_STATE_KEY, null);
    this.panelVisibility = Object.assign(
      {
        documents: true,
        toolbox: true,
        logical: true,
        visual: true,
        properties: true
      },
      this.context.workspaceState.get(PANEL_VISIBILITY_STATE_KEY, null) || {});
    this.documentsProvider = new DesignDocumentsProvider(this);
    this.toolboxProvider = new DesignToolboxProvider(this);
    this.logicalTreeProvider = new DesignElementTreeProvider(this, 'logical');
    this.visualTreeProvider = new DesignElementTreeProvider(this, 'visual');
    this.propertiesProvider = new DesignPropertiesViewProvider(this);
    this.toolboxDragAndDropController = new DesignToolboxDragAndDropController();
    this.documentsTreeView = null;
    this.toolboxTreeView = null;
    this.logicalTreeView = null;
    this.visualTreeView = null;
  }

  register(context) {
    this.previewController.setDesignController(this);

    this.documentsTreeView = vscode.window.createTreeView('axsgDesignDocumentsView', {
      treeDataProvider: this.documentsProvider,
      canSelectMany: false,
      showCollapseAll: false
    });
    this.toolboxTreeView = vscode.window.createTreeView('axsgDesignToolboxView', {
      treeDataProvider: this.toolboxProvider,
      dragAndDropController: this.toolboxDragAndDropController,
      canSelectMany: false,
      showCollapseAll: false
    });
    this.logicalTreeView = vscode.window.createTreeView('axsgDesignLogicalTreeView', {
      treeDataProvider: this.logicalTreeProvider,
      canSelectMany: false,
      showCollapseAll: false
    });
    this.visualTreeView = vscode.window.createTreeView('axsgDesignVisualTreeView', {
      treeDataProvider: this.visualTreeProvider,
      canSelectMany: false,
      showCollapseAll: false
    });

    context.subscriptions.push(this.documentsTreeView);
    context.subscriptions.push(this.toolboxTreeView);
    context.subscriptions.push(this.logicalTreeView);
    context.subscriptions.push(this.visualTreeView);
    context.subscriptions.push(vscode.window.registerWebviewViewProvider('axsgDesignPropertiesView', this.propertiesProvider));
    this.registerDocumentDropProvider(context);

    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.refresh', async () => {
      await this.refreshFromActiveSession('command');
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.undo', async () => {
      await this.applyMutation('undo', {});
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.redo', async () => {
      await this.applyMutation('redo', {});
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.selectDocument', async document => {
      if (document && document.buildUri) {
        await this.selectDocument(document.buildUri, { revealEditor: true });
      }
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.selectElement', async element => {
      if (element && element.id) {
        await this.selectElement(element, { revealEditor: true });
      }
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.insertToolboxItem', async toolboxItem => {
      if (toolboxItem) {
        await this.insertToolboxItem(toolboxItem);
      }
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.setWorkspaceMode', async mode => {
      if (mode) {
        await this.setWorkspaceMode(this.currentSession, mode);
      }
    }));
    context.subscriptions.push(vscode.commands.registerCommand('axsg.design.setHitTestMode', async mode => {
      if (mode) {
        await this.setHitTestMode(this.currentSession, mode);
      }
    }));

    this.registerTreeViewStateHandlers(context, this.documentsTreeView, 'documents');
    this.registerTreeViewStateHandlers(context, this.toolboxTreeView, 'toolbox');
    this.registerTreeExpansionHandlers(context, this.logicalTreeView, this.logicalExpandedIds, LOGICAL_EXPANDED_STATE_KEY);
    this.registerTreeExpansionHandlers(context, this.visualTreeView, this.visualExpandedIds, VISUAL_EXPANDED_STATE_KEY);
    this.registerTreeViewStateHandlers(context, this.logicalTreeView, 'logical');
    this.registerTreeViewStateHandlers(context, this.visualTreeView, 'visual');

    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(async editor => {
      if (!editor || !this.isXamlDocument(editor.document)) {
        return;
      }

      await this.refreshFromActiveSession('activeEditor');
    }));
    context.subscriptions.push(vscode.window.onDidChangeTextEditorSelection(event => {
      this.handleEditorSelectionChanged(event);
    }));
    void this.refreshFromActiveSession('activate');
  }

  dispose() {
    if (this.editorSelectionTimer) {
      clearTimeout(this.editorSelectionTimer);
      this.editorSelectionTimer = null;
    }

    if (this.currentSession) {
      this.currentSession.setDesignState(null);
    }

    if (this.previewController && typeof this.previewController.setDesignController === 'function') {
      this.previewController.setDesignController(null);
    }

    this.currentSession = null;
    this.workspace = null;
    this.logicalTree = null;
    this.visualTree = null;
    this.overlay = null;
  }

  registerTreeViewStateHandlers(context, treeView, panelKey) {
    if (!treeView) {
      return;
    }

    context.subscriptions.push(treeView.onDidChangeVisibility(event => {
      this.panelVisibility[panelKey] = !!event.visible;
      void this.context.workspaceState.update(PANEL_VISIBILITY_STATE_KEY, this.panelVisibility);
    }));
  }

  registerTreeExpansionHandlers(context, treeView, expandedIds, storageKey) {
    if (!treeView) {
      return;
    }

    context.subscriptions.push(treeView.onDidExpandElement(event => {
      if (event && event.element && event.element.id) {
        expandedIds.add(event.element.id);
        void this.persistExpandedState(storageKey, expandedIds);
      }
    }));
    context.subscriptions.push(treeView.onDidCollapseElement(event => {
      if (event && event.element && event.element.id) {
        expandedIds.delete(event.element.id);
        void this.persistExpandedState(storageKey, expandedIds);
      }
    }));
  }

  async persistExpandedState(storageKey, expandedIds) {
    await this.context.workspaceState.update(storageKey, Array.from(expandedIds).sort());
  }

  async handleSessionEvent(event) {
    if (!event || !event.session) {
      return;
    }

    if (event.event === 'disposed') {
      if (this.currentSession && this.currentSession === event.session) {
        this.clearState();
      }
      return;
    }

    const activeSession = this.previewController.getActiveSession();
    if (activeSession) {
      if (event.session !== activeSession) {
        return;
      }
    } else if (event.event !== 'previewStarted' && (!this.currentSession || event.session !== this.currentSession)) {
      return;
    }

    if (event.event === 'previewStarted' || event.event === 'updateResult') {
      await this.refreshFromSession(event.session, event.event);
    }
  }

  handleSessionWebviewMessage(session, message) {
    if (!message || !message.type || !this.currentSession || session !== this.currentSession) {
      return false;
    }

    if (!this.workspace && message.type.startsWith('design')) {
      return true;
    }

    switch (message.type) {
      case 'designSelectAtPoint':
        if (typeof message.x === 'number' && typeof message.y === 'number') {
          void this.selectAtPoint(session, message.x, message.y, true);
        }
        return true;
      case 'designHoverAtPoint':
        if (typeof message.x === 'number' && typeof message.y === 'number') {
          void this.selectAtPoint(session, message.x, message.y, false);
        }
        return true;
      case 'designSetWorkspaceMode':
        if (message.mode) {
          void this.setWorkspaceMode(session, message.mode);
        }
        return true;
      case 'designSetHitTestMode':
        if (message.mode) {
          void this.setHitTestMode(session, message.mode);
        }
        return true;
      case 'designDropToolboxItem':
        if (message.toolboxItem && typeof message.x === 'number' && typeof message.y === 'number') {
          void this.insertToolboxItemAtPoint(session, message.toolboxItem, message.x, message.y);
        }
        return true;
      default:
        return false;
    }
  }

  registerDocumentDropProvider(context) {
    if (!vscode.languages || typeof vscode.languages.registerDocumentDropEditProvider !== 'function') {
      return;
    }

    context.subscriptions.push(vscode.languages.registerDocumentDropEditProvider(
      [
        { scheme: 'file', language: 'xaml' },
        { scheme: 'file', language: 'axaml' }
      ],
      new DesignToolboxDocumentDropProvider(),
      {
        dropMimeTypes: [
          DESIGN_TOOLBOX_ITEM_MIME,
          DESIGN_TOOLBOX_TEXT_MIME
        ]
      }));
  }

  async refreshFromActiveSession(reason) {
    const session = this.previewController.getActiveSession();
    if (!session) {
      this.clearState();
      return;
    }

    await this.refreshFromSession(session, reason);
  }

  async refreshFromSession(session, reason) {
    const previousSession = this.currentSession;
    this.currentSession = session;
    if (previousSession && previousSession !== session) {
      previousSession.setDesignState(null);
    }

    try {
      await this.loadSessionStateAsync(session);
      await this.ensureSessionPreferencesAsync(session);
      this.refreshProviders();
      this.publishPreviewDesignState();
      await this.revealCurrentSelectionAsync();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.handleDesignTransportFailure(session, message);
      this.getOutputChannel().appendLine(`[design] refresh failed (${reason}): ${message}`);
    }
  }

  async loadSessionStateAsync(session) {
    const workspace = await session.sendDesignCommand('workspace.current', {});
    const [logicalTreeResult, visualTreeResult, overlayResult] = await Promise.allSettled([
      session.sendDesignCommand('tree.logical', {}),
      session.sendDesignCommand('tree.visual', {}),
      session.sendDesignCommand('overlay.current', {})
    ]);
    if (this.currentSession !== session) {
      return;
    }

    this.workspace = workspace || null;
    this.logicalTree = logicalTreeResult.status === 'fulfilled'
      ? logicalTreeResult.value || null
      : null;
    this.visualTree = visualTreeResult.status === 'fulfilled'
      ? visualTreeResult.value || null
      : null;
    this.overlay = overlayResult.status === 'fulfilled'
      ? overlayResult.value || null
      : null;
  }

  async ensureSessionPreferencesAsync(session) {
    if (!this.workspace || this.currentSession !== session || this.preferencesAppliedSessions.has(session)) {
      return;
    }

    this.preferencesAppliedSessions.add(session);

    let changed = false;
    if (!sameText(this.workspace.mode, this.workspaceMode)) {
      await session.sendDesignCommand('setWorkspaceMode', { mode: this.workspaceMode });
      changed = true;
    }

    if (!sameText(this.workspace.hitTestMode, this.hitTestMode)) {
      await session.sendDesignCommand('setHitTestMode', { mode: this.hitTestMode });
      changed = true;
    }

    const hasPreferredDocument = this.selectedDocumentBuildUri &&
      this.getWorkspaceDocuments().some(document => sameText(document.buildUri, this.selectedDocumentBuildUri));
    if (hasPreferredDocument && !sameText(this.workspace.activeBuildUri, this.selectedDocumentBuildUri)) {
      await session.sendDesignCommand('selectDocument', { buildUri: this.selectedDocumentBuildUri });
      changed = true;
    }

    if (changed) {
      await this.loadSessionStateAsync(session);
    }
  }

  clearState() {
    if (this.currentSession) {
      this.currentSession.setDesignState(null);
    }

    this.currentSession = null;
    this.workspace = null;
    this.logicalTree = null;
    this.visualTree = null;
    this.overlay = null;
    this.refreshProviders();
  }

  refreshProviders() {
    this.documentsProvider.refresh();
    this.toolboxProvider.refresh();
    this.logicalTreeProvider.refresh();
    this.visualTreeProvider.refresh();
    this.propertiesProvider.refresh();
  }

  publishPreviewDesignState() {
    if (!this.currentSession) {
      return;
    }

    if (!this.workspace) {
      this.currentSession.setDesignState({ available: false });
      return;
    }

    this.currentSession.setDesignState({
      available: true,
      workspaceMode: this.workspace.mode || DEFAULT_WORKSPACE_MODE,
      hitTestMode: this.workspace.hitTestMode || DEFAULT_HIT_TEST_MODE,
      canUndo: !!this.workspace.canUndo,
      canRedo: !!this.workspace.canRedo,
      overlay: this.overlay,
      selectedElement: this.getSelectedElement()
    });
  }

  async revealCurrentSelectionAsync() {
    await Promise.all([
      this.revealActiveDocumentAsync(),
      this.revealSelectedElementInTreeAsync(this.logicalTreeView, this.logicalTree),
      this.revealSelectedElementInTreeAsync(this.visualTreeView, this.visualTree)
    ]);
  }

  async revealActiveDocumentAsync() {
    if (!this.documentsTreeView || !this.workspace || !this.workspace.activeBuildUri) {
      return;
    }

    const document = this.getWorkspaceDocuments().find(item => sameText(item.buildUri, this.workspace.activeBuildUri));
    if (!document) {
      return;
    }

    try {
      await this.documentsTreeView.reveal(document, { focus: false, select: true });
    } catch {
      // The tree view may be hidden or not expanded yet.
    }
  }

  async revealSelectedElementInTreeAsync(treeView, treeSnapshot) {
    if (!treeView || !treeSnapshot || !this.workspace || !this.workspace.selectedElementId) {
      return;
    }

    const element = this.findElementForSelection(treeSnapshot.elements, this.workspace.selectedElementId);
    if (!element) {
      return;
    }

    try {
      await treeView.reveal(element, { focus: false, select: true, expand: 10 });
    } catch {
      // Best effort tree synchronization.
    }
  }

  async selectDocument(buildUri, options = {}) {
    if (!this.currentSession || !buildUri) {
      return;
    }

    this.selectedDocumentBuildUri = buildUri;
    await this.context.workspaceState.update(SELECTED_DOCUMENT_STATE_KEY, buildUri);
    await this.currentSession.sendDesignCommand('selectDocument', { buildUri });
    await this.refreshFromSession(this.currentSession, 'selectDocument');

    if (options.revealEditor) {
      await this.openSourceForBuildUri(buildUri);
    }
  }

  async selectElement(element, options = {}) {
    if (!this.currentSession || !element || !element.id) {
      return;
    }

    const selectionElementId = this.getSelectableElementId(element);
    if (!selectionElementId) {
      return;
    }

    const buildUri = element.sourceBuildUri || this.workspace?.activeBuildUri || undefined;
    if (buildUri) {
      this.selectedDocumentBuildUri = buildUri;
      await this.context.workspaceState.update(SELECTED_DOCUMENT_STATE_KEY, buildUri);
    }

    await this.currentSession.sendDesignCommand('selectElement', {
      buildUri,
      elementId: selectionElementId
    });
    await this.refreshFromSession(this.currentSession, 'selectElement');

    if (options.revealEditor) {
      await this.revealElementInEditor(this.getSelectedElement() || element);
    }
  }

  async selectAtPoint(session, x, y, updateSelection) {
    if (!session || typeof x !== 'number' || typeof y !== 'number') {
      return;
    }

    try {
      const result = await session.sendDesignCommand('selectAtPoint', {
        buildUri: this.workspace?.activeBuildUri || undefined,
        x,
        y,
        updateSelection,
        hitTestMode: this.workspace?.hitTestMode || this.hitTestMode
      });

      if (result && result.overlay) {
        this.overlay = result.overlay;
      }

      if (updateSelection) {
        await this.refreshFromSession(session, 'selectAtPoint');
        const selectedElement = this.getSelectedElement();
        if (selectedElement) {
          await this.revealElementInEditor(selectedElement);
        }
      } else {
        this.publishPreviewDesignState();
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.handleDesignTransportFailure(session, message);
      this.getOutputChannel().appendLine(`[design] selectAtPoint failed: ${message}`);
    }
  }

  handleDesignTransportFailure(session, message) {
    if (!session || this.currentSession !== session || !this.isDisconnectedTransportMessage(message)) {
      return;
    }

    this.workspace = null;
    this.logicalTree = null;
    this.visualTree = null;
    this.overlay = null;
    this.refreshProviders();
    session.setDesignState({ available: false });
  }

  isDisconnectedTransportMessage(message) {
    if (!message) {
      return false;
    }

    return message.includes('Preview design server returned an empty response') ||
      message.includes('Preview host was disposed.') ||
      message.includes('Broken pipe') ||
      message.includes('Unable to write data to the transport connection') ||
      message.includes('Connection reset by peer');
  }

  async setWorkspaceMode(session, mode) {
    if (!session || !mode) {
      return;
    }

    this.workspaceMode = mode;
    await this.context.workspaceState.update(WORKSPACE_MODE_STATE_KEY, mode);
    await session.sendDesignCommand('setWorkspaceMode', { mode });
    await this.refreshFromSession(session, 'setWorkspaceMode');
  }

  async setHitTestMode(session, mode) {
    if (!session || !mode) {
      return;
    }

    this.hitTestMode = mode;
    await this.context.workspaceState.update(HIT_TEST_MODE_STATE_KEY, mode);
    await session.sendDesignCommand('setHitTestMode', { mode });
    await this.refreshFromSession(session, 'setHitTestMode');
  }

  async setPropertyFilterMode(mode) {
    if (!this.currentSession || !mode) {
      return;
    }

    await this.currentSession.sendDesignCommand('setPropertyFilterMode', { mode });
    await this.refreshFromSession(this.currentSession, 'setPropertyFilterMode');
  }

  async applyPropertyUpdate(propertyName, propertyValue, removeProperty = false) {
    return this.applyMutation('applyPropertyUpdate', {
      buildUri: this.workspace?.activeBuildUri || undefined,
      elementId: this.workspace?.selectedElementId || undefined,
      propertyName,
      propertyValue,
      removeProperty,
      persistChangesToSource: false,
      waitForHotReload: false,
      fallbackToRuntimeApplyOnTimeout: false
    });
  }

  async insertToolboxItem(toolboxItem) {
    if (!toolboxItem) {
      return;
    }

    await this.applyMutation('insertElement', {
      buildUri: this.workspace?.activeBuildUri || undefined,
      parentElementId: this.workspace?.selectedElementId || undefined,
      elementName: toolboxItem.name || toolboxItem.displayName,
      xamlFragment: toolboxItem.xamlSnippet || undefined,
      persistChangesToSource: false,
      waitForHotReload: false,
      fallbackToRuntimeApplyOnTimeout: false
    });
  }

  async insertToolboxItemAtPoint(session, toolboxItem, x, y) {
    if (!session || !toolboxItem) {
      return;
    }

    await this.selectAtPoint(session, x, y, true);
    await this.insertToolboxItem(toolboxItem);
  }

  async applyMutation(operation, payload) {
    if (!this.currentSession) {
      return;
    }

    const response = await this.currentSession.sendDesignCommand(operation, payload || {});
    if (!response || !response.applyResult) {
      await this.refreshFromSession(this.currentSession, operation);
      return;
    }

    if (!response.applyResult.succeeded) {
      const message = response.applyResult.message || 'Design mutation failed.';
      await vscode.window.showErrorMessage(`AXSG Design: ${message}`);
      return;
    }

    if (response.workspace) {
      this.workspace = response.workspace;
    }

    if (response.overlay) {
      this.overlay = response.overlay;
    }

    await this.applyMinimalDiffToEditor(response.applyResult, response.workspace || this.workspace);
    await this.refreshFromSession(this.currentSession, operation);
  }

  async applyMinimalDiffToEditor(applyResult, workspace) {
    const sourcePath = this.resolveSourcePath(applyResult.buildUri, workspace);
    if (!sourcePath || !workspace || typeof workspace.currentXamlText !== 'string') {
      return;
    }

    const uri = vscode.Uri.file(sourcePath);
    const document = await vscode.workspace.openTextDocument(uri);
    const startOffset = Math.max(0, Number(applyResult.minimalDiffStart) || 0);
    const removedLength = Math.max(0, Number(applyResult.minimalDiffRemovedLength) || 0);
    const insertedLength = Math.max(0, Number(applyResult.minimalDiffInsertedLength) || 0);
    const insertedText = insertedLength > 0
      ? workspace.currentXamlText.substring(startOffset, startOffset + insertedLength)
      : '';
    const range = new vscode.Range(
      document.positionAt(startOffset),
      document.positionAt(startOffset + removedLength));
    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, range, insertedText);

    this.suppressEditorSync = true;
    try {
      await vscode.workspace.applyEdit(edit);
    } finally {
      setTimeout(() => {
        this.suppressEditorSync = false;
      }, 0);
    }
  }

  handleEditorSelectionChanged(event) {
    if (this.suppressEditorSync || !event || !event.textEditor || !this.isXamlDocument(event.textEditor.document)) {
      return;
    }

    if (this.editorSelectionTimer) {
      clearTimeout(this.editorSelectionTimer);
    }

    this.editorSelectionTimer = setTimeout(() => {
      this.editorSelectionTimer = null;
      void this.syncEditorSelectionAsync(event.textEditor);
    }, EDITOR_SELECTION_DEBOUNCE_MS);
  }

  async syncEditorSelectionAsync(editor) {
    if (!editor || this.suppressEditorSync || !this.isXamlDocument(editor.document)) {
      return;
    }

    const session = this.previewController.getSession(editor.document.uri.toString());
    if (!session) {
      return;
    }

    if (!this.currentSession || session !== this.currentSession) {
      await this.refreshFromSession(session, 'editorSelection');
    }

    if (!this.workspace || !Array.isArray(this.workspace.elements) || this.workspace.elements.length === 0) {
      return;
    }

    const activeSelection = Array.isArray(editor.selections) && editor.selections.length > 0
      ? editor.selections[0]
      : editor.selection;
    if (!activeSelection) {
      return;
    }

    const offset = editor.document.offsetAt(activeSelection.active);
    const element = this.findDeepestElementByOffset(this.workspace.elements, offset);
    if (!element || element.id === this.workspace.selectedElementId) {
      return;
    }

    await session.sendDesignCommand('selectElement', {
      buildUri: element.sourceBuildUri || this.workspace.activeBuildUri || undefined,
      elementId: element.id
    });
    await this.refreshFromSession(session, 'editorSelectionSync');
  }

  async revealElementInEditor(element) {
    const sourceRange = element && element.sourceRange;
    const sourcePath = this.resolveSourcePath(element?.sourceBuildUri || this.workspace?.activeBuildUri, this.workspace);
    if (!sourceRange || !sourcePath) {
      return;
    }

    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(sourcePath));
    const editor = await vscode.window.showTextDocument(document, { preserveFocus: false, preview: false });
    const range = new vscode.Range(
      document.positionAt(sourceRange.startOffset),
      document.positionAt(sourceRange.endOffset));

    this.suppressEditorSync = true;
    try {
      editor.selection = new vscode.Selection(range.start, range.end);
      editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
    } finally {
      setTimeout(() => {
        this.suppressEditorSync = false;
      }, 0);
    }
  }

  async openSourceForBuildUri(buildUri) {
    const sourcePath = this.resolveSourcePath(buildUri, this.workspace);
    if (!sourcePath) {
      return;
    }

    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(sourcePath));
    await vscode.window.showTextDocument(document, { preserveFocus: false, preview: false });
  }

  getWorkspaceDocuments() {
    return this.workspace && Array.isArray(this.workspace.documents)
      ? this.workspace.documents
      : [];
  }

  getSelectedElement() {
    return this.findElementById(this.workspace && Array.isArray(this.workspace.elements) ? this.workspace.elements : [], this.workspace?.selectedElementId);
  }

  getSelectableElementId(element) {
    if (!element) {
      return null;
    }

    if (typeof element.sourceElementId === 'string' && element.sourceElementId.trim().length > 0) {
      return element.sourceElementId.trim();
    }

    return typeof element.id === 'string' && element.id.trim().length > 0
      ? element.id.trim()
      : null;
  }

  getLiveTree(kind) {
    const tree = kind === 'logical' ? this.logicalTree : this.visualTree;
    return tree && Array.isArray(tree.elements)
      ? tree.elements
      : [];
  }

  isExpanded(kind, elementId, fallbackExpanded = false) {
    if (!elementId) {
      return fallbackExpanded;
    }

    const expandedIds = kind === 'logical' ? this.logicalExpandedIds : this.visualExpandedIds;
    return expandedIds.has(elementId) || fallbackExpanded;
  }

  resolveSourcePath(buildUri, workspace) {
    const documents = workspace && Array.isArray(workspace.documents) ? workspace.documents : [];
    const match = documents.find(document => sameText(document.buildUri, buildUri));
    return match && typeof match.sourcePath === 'string'
      ? match.sourcePath
      : null;
  }

  findElementById(elements, elementId) {
    if (!Array.isArray(elements) || !elementId) {
      return null;
    }

    for (const element of elements) {
      if (!element) {
        continue;
      }

      if (element.id === elementId) {
        return element;
      }

      const child = this.findElementById(element.children, elementId);
      if (child) {
        return child;
      }
    }

    return null;
  }

  findElementForSelection(elements, elementId) {
    if (!Array.isArray(elements) || !elementId) {
      return null;
    }

    for (const element of elements) {
      if (!element) {
        continue;
      }

      if (element.id === elementId || element.sourceElementId === elementId) {
        return element;
      }

      const child = this.findElementForSelection(element.children, elementId);
      if (child) {
        return child;
      }
    }

    return null;
  }

  findDeepestElementByOffset(elements, offset) {
    let best = null;

    const visit = node => {
      if (!node || !node.sourceRange) {
        return;
      }

      if (offset < node.sourceRange.startOffset || offset > node.sourceRange.endOffset) {
        return;
      }

      if (!best || (node.sourceRange.endOffset - node.sourceRange.startOffset) <= (best.sourceRange.endOffset - best.sourceRange.startOffset)) {
        best = node;
      }

      for (const child of Array.isArray(node.children) ? node.children : []) {
        visit(child);
      }
    };

    for (const element of elements || []) {
      visit(element);
    }

    return best;
  }
}

class DesignDocumentsProvider {
  constructor(controller) {
    this.controller = controller;
    this.eventEmitter = new vscode.EventEmitter();
    this.onDidChangeTreeData = this.eventEmitter.event;
  }

  refresh() {
    this.eventEmitter.fire(undefined);
  }

  getChildren() {
    return this.controller.getWorkspaceDocuments();
  }

  getTreeItem(document) {
    const selected = sameText(document.buildUri, this.controller.workspace?.activeBuildUri);
    const item = new vscode.TreeItem(path.basename(document.sourcePath || document.buildUri || 'Document'));
    item.description = selected ? 'Active' : '';
    item.tooltip = document.sourcePath || document.buildUri;
    item.command = {
      command: 'axsg.design.selectDocument',
      title: 'Select AXSG design document',
      arguments: [document]
    };
    item.contextValue = 'axsgDesignDocument';
    return item;
  }
}

class DesignToolboxProvider {
  constructor(controller) {
    this.controller = controller;
    this.eventEmitter = new vscode.EventEmitter();
    this.onDidChangeTreeData = this.eventEmitter.event;
  }

  refresh() {
    this.eventEmitter.fire(undefined);
  }

  getChildren(element) {
    if (!element) {
      return this.controller.workspace && Array.isArray(this.controller.workspace.toolbox)
        ? this.controller.workspace.toolbox
        : [];
    }

    return Array.isArray(element.items)
      ? element.items
      : [];
  }

  getTreeItem(element) {
    if (Array.isArray(element.items)) {
      const item = new vscode.TreeItem(element.name || 'Category', vscode.TreeItemCollapsibleState.Expanded);
      item.contextValue = 'axsgDesignToolboxCategory';
      return item;
    }

    const item = new vscode.TreeItem(element.displayName || element.name || 'Control');
    item.description = element.category || '';
    item.tooltip = element.xamlSnippet || element.name;
    item.command = {
      command: 'axsg.design.insertToolboxItem',
      title: 'Insert AXSG toolbox item',
      arguments: [element]
    };
    item.contextValue = 'axsgDesignToolboxItem';
    return item;
  }
}

class DesignToolboxDragAndDropController {
  constructor() {
    this.dragMimeTypes = [
      DESIGN_TOOLBOX_ITEM_MIME,
      DESIGN_TOOLBOX_TEXT_MIME
    ];
    this.dropMimeTypes = [];
  }

  async handleDrag(source, dataTransfer) {
    if (!dataTransfer || typeof dataTransfer.set !== 'function') {
      return;
    }

    const toolboxItem = Array.isArray(source)
      ? source.find(candidate => candidate && !Array.isArray(candidate.items))
      : null;
    const serialized = serializeToolboxItem(toolboxItem);
    if (!serialized) {
      return;
    }

    dataTransfer.set(DESIGN_TOOLBOX_ITEM_MIME, new vscode.DataTransferItem(serialized));

    const textPayload = serializeToolboxItemAsText(toolboxItem);
    if (textPayload) {
      dataTransfer.set(DESIGN_TOOLBOX_TEXT_MIME, new vscode.DataTransferItem(textPayload));
    }
  }
}

class DesignToolboxDocumentDropProvider {
  async provideDocumentDropEdits(_document, _position, dataTransfer) {
    const toolboxItem = await readToolboxItemFromDataTransfer(dataTransfer);
    if (!toolboxItem || !toolboxItem.name) {
      return undefined;
    }

    const xamlSnippet = toolboxItem.xamlSnippet || `<${toolboxItem.name} />`;
    return new vscode.DocumentDropEdit(
      xamlSnippet,
      `Insert ${toolboxItem.displayName || toolboxItem.name || 'AXSG toolbox item'}`);
  }
}

class DesignElementTreeProvider {
  constructor(controller, kind) {
    this.controller = controller;
    this.kind = kind;
    this.eventEmitter = new vscode.EventEmitter();
    this.onDidChangeTreeData = this.eventEmitter.event;
  }

  refresh() {
    this.eventEmitter.fire(undefined);
  }

  getChildren(element) {
    if (!element) {
      return this.controller.getLiveTree(this.kind);
    }

    return Array.isArray(element.children)
      ? element.children
      : [];
  }

  getTreeItem(element) {
    const hasChildren = Array.isArray(element.children) && element.children.length > 0;
    const collapsibleState = hasChildren
      ? (this.controller.isExpanded(this.kind, element.id, !!element.isExpanded)
          ? vscode.TreeItemCollapsibleState.Expanded
          : vscode.TreeItemCollapsibleState.Collapsed)
      : vscode.TreeItemCollapsibleState.None;
    const item = new vscode.TreeItem(element.displayName || element.typeName || element.id, collapsibleState);
    item.description = element.isLive ? 'Live' : '';
    item.tooltip = buildElementTooltip(element);
    item.command = {
      command: 'axsg.design.selectElement',
      title: 'Select AXSG design element',
      arguments: [element]
    };
    item.contextValue = 'axsgDesignElement';
    return item;
  }
}

class DesignPropertiesViewProvider {
  constructor(controller) {
    this.controller = controller;
    this.view = null;
  }

  resolveWebviewView(webviewView) {
    this.view = webviewView;
    webviewView.webview.options = {
      enableScripts: true
    };
    webviewView.webview.onDidReceiveMessage(async message => {
      if (!message || !message.type) {
        return;
      }

      if (message.type === 'applyProperty') {
        await this.controller.applyPropertyUpdate(message.propertyName, message.propertyValue || '', false);
      } else if (message.type === 'removeProperty') {
        await this.controller.applyPropertyUpdate(message.propertyName, null, true);
      } else if (message.type === 'quickSetProperty') {
        await this.controller.applyPropertyUpdate(message.propertyName, message.propertyValue || '', false);
      } else if (message.type === 'setPropertyFilterMode' && message.mode) {
        await this.controller.setPropertyFilterMode(message.mode);
      }
    });
    this.refresh();
  }

  refresh() {
    if (!this.view) {
      return;
    }

    const properties = this.controller.workspace && Array.isArray(this.controller.workspace.properties)
      ? this.controller.workspace.properties
      : [];
    this.view.webview.html = renderPropertiesViewHtml(
      this.view.webview,
      this.controller.getSelectedElement(),
      properties,
      this.controller.workspace?.propertyFilterMode || 'Smart');
  }
}

function renderPropertiesViewHtml(webview, selectedElement, properties, propertyFilterMode) {
  const cspSource = webview.cspSource;
  const selectedTitle = selectedElement
    ? escapeHtml(selectedElement.displayName || selectedElement.typeName || selectedElement.id)
    : 'No selection';
  const selectedSubtitle = selectedElement
    ? escapeHtml([selectedElement.typeName, selectedElement.xamlName ? `x:Name=${selectedElement.xamlName}` : null].filter(Boolean).join(' • '))
    : 'Select an element in the preview or tree to inspect its properties.';
  const propertyCount = Array.isArray(properties) ? properties.length : 0;
  const propertyGroups = buildPropertyGroups(properties);

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src 'unsafe-inline' ${cspSource};">
  <style>
    :root {
      color-scheme: light dark;
    }

    body {
      font-family: var(--vscode-font-family);
      color: var(--vscode-foreground);
      background: var(--vscode-sideBar-background);
      margin: 0;
    }

    .shell {
      display: grid;
      grid-template-rows: auto 1fr;
      min-height: 100vh;
    }

    .header {
      position: sticky;
      top: 0;
      z-index: 1;
      display: grid;
      gap: 10px;
      padding: 12px;
      background: var(--vscode-sideBar-background);
      border-bottom: 1px solid var(--vscode-sideBarSectionHeader-border, rgba(127,127,127,0.15));
    }

    .title-block {
      display: grid;
      gap: 4px;
    }

    .title {
      font-size: 12px;
      font-weight: 700;
    }

    .subtitle {
      font-size: 11px;
      color: var(--vscode-descriptionForeground);
      line-height: 1.35;
    }

    .toolbar {
      display: grid;
      gap: 8px;
    }

    .toolbar-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 8px;
      align-items: center;
    }

    .search-input,
    .toolbar select,
    .editor-input,
    .editor-select,
    .editor-textarea {
      width: 100%;
      box-sizing: border-box;
      background: var(--vscode-input-background);
      color: var(--vscode-input-foreground);
      border: 1px solid var(--vscode-input-border);
      padding: 6px 8px;
      border-radius: 2px;
      font: inherit;
    }

    .search-input::placeholder {
      color: var(--vscode-input-placeholderForeground, var(--vscode-descriptionForeground));
    }

    .toolbar select {
      min-width: 92px;
      background: var(--vscode-dropdown-background);
      color: var(--vscode-dropdown-foreground);
      border-color: var(--vscode-dropdown-border, var(--vscode-input-border));
    }

    .toolbar-meta {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      align-items: center;
      font-size: 11px;
      color: var(--vscode-descriptionForeground);
    }

    .content {
      padding: 12px;
      display: grid;
      gap: 14px;
      align-content: start;
    }

    .group {
      display: grid;
      gap: 6px;
    }

    .group-title {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--vscode-descriptionForeground);
    }

    .group-table {
      border: 1px solid var(--vscode-sideBarSectionHeader-border, rgba(127,127,127,0.15));
      background: color-mix(in srgb, var(--vscode-editor-background, var(--vscode-sideBar-background)) 72%, transparent);
    }

    .row {
      display: grid;
      grid-template-columns: minmax(110px, 38%) minmax(0, 1fr);
      align-items: stretch;
      border-top: 1px solid var(--vscode-sideBarSectionHeader-border, rgba(127,127,127,0.12));
    }

    .row:first-child {
      border-top: none;
    }

    .name-cell {
      display: grid;
      gap: 6px;
      padding: 8px 10px;
      background: color-mix(in srgb, var(--vscode-sideBarSectionHeader-background, var(--vscode-sideBar-background)) 80%, transparent);
      border-right: 1px solid var(--vscode-sideBarSectionHeader-border, rgba(127,127,127,0.12));
      align-content: start;
    }

    .name-line {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 8px;
    }

    .name {
      font-size: 11px;
      font-weight: 600;
      line-height: 1.3;
      word-break: break-word;
    }

    .hint {
      font-size: 10px;
      color: var(--vscode-descriptionForeground);
      line-height: 1.35;
      word-break: break-word;
    }

    .value-cell {
      display: grid;
      gap: 8px;
      padding: 8px 10px;
      align-content: start;
    }

    .editor-textarea {
      min-height: 72px;
      resize: vertical;
      line-height: 1.35;
    }

    .editor-input[readonly],
    .editor-select:disabled,
    .editor-textarea[readonly] {
      opacity: 0.72;
    }

    .badge-row,
    .actions,
    .quick-sets {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
      align-items: center;
    }

    .badge {
      padding: 1px 6px;
      border-radius: 999px;
      font-size: 10px;
      line-height: 1.6;
      background: var(--vscode-badge-background);
      color: var(--vscode-badge-foreground);
    }

    .badge.subtle {
      background: color-mix(in srgb, var(--vscode-badge-background) 50%, transparent);
    }

    button {
      background: var(--vscode-button-background);
      color: var(--vscode-button-foreground);
      border: none;
      padding: 5px 8px;
      cursor: pointer;
      border-radius: 2px;
      font: inherit;
    }

    button.secondary {
      background: var(--vscode-button-secondaryBackground);
      color: var(--vscode-button-secondaryForeground);
    }

    button:disabled {
      cursor: default;
      opacity: 0.6;
    }

    .empty {
      font-size: 11px;
      color: var(--vscode-descriptionForeground);
      line-height: 1.45;
      padding: 4px 0;
    }

    @media (max-width: 420px) {
      .toolbar-row {
        grid-template-columns: 1fr;
      }

      .row {
        grid-template-columns: 1fr;
      }

      .name-cell {
        border-right: none;
        border-bottom: 1px solid var(--vscode-sideBarSectionHeader-border, rgba(127,127,127,0.12));
      }
    }
  </style>
</head>
<body>
  <div class="shell">
    <div class="header">
      <div class="title-block">
        <div class="title">${selectedTitle}</div>
        <div class="subtitle">${selectedSubtitle}</div>
      </div>
      <div class="toolbar">
        <div class="toolbar-row">
          <input
            id="property-name-filter"
            class="search-input"
            type="search"
            spellcheck="false"
            placeholder="Filter properties by name"
            aria-label="Filter properties by name">
          <select id="property-filter-mode" aria-label="Property filter mode">
            <option value="Smart"${propertyFilterMode === 'Smart' ? ' selected' : ''}>Smart</option>
            <option value="All"${propertyFilterMode === 'All' ? ' selected' : ''}>All</option>
          </select>
        </div>
        <div class="toolbar-meta">
          <span>AXSG Inspector Properties</span>
          <span id="property-summary">${propertyCount === 1 ? '1 property' : `${propertyCount} properties`}</span>
        </div>
      </div>
    </div>
    <div class="content" id="property-groups">
      ${propertyGroups || '<div class="empty">Select an element in the preview or tree to inspect its properties.</div>'}
    </div>
  </div>
  <script>
    const vscodeApi = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
    const filterMode = document.getElementById('property-filter-mode');
    const propertyNameFilter = document.getElementById('property-name-filter');
    const propertySummary = document.getElementById('property-summary');
    const persistedState = vscodeApi && typeof vscodeApi.getState === 'function'
      ? (vscodeApi.getState() || {})
      : {};

    function normalizeSearchText(value) {
      return String(value || '').trim().toLowerCase();
    }

    function updatePropertySummary(visibleCount, totalCount, hasQuery) {
      if (!propertySummary) {
        return;
      }

      if (!totalCount) {
        propertySummary.textContent = 'No properties';
        return;
      }

      propertySummary.textContent = hasQuery
        ? (visibleCount === 1 ? '1 of ' + totalCount + ' properties' : visibleCount + ' of ' + totalCount + ' properties')
        : (totalCount === 1 ? '1 property' : totalCount + ' properties');
    }

    function persistViewState() {
      if (!vscodeApi || typeof vscodeApi.setState !== 'function') {
        return;
      }

      vscodeApi.setState({
        propertyNameFilter: propertyNameFilter ? propertyNameFilter.value : ''
      });
    }

    function applyPropertyNameFilter() {
      const rows = Array.from(document.querySelectorAll('[data-property-row]'));
      const query = normalizeSearchText(propertyNameFilter ? propertyNameFilter.value : '');
      let visibleCount = 0;
      rows.forEach(row => {
        const searchText = row.getAttribute('data-property-search') || '';
        const visible = !query || searchText.includes(query);
        row.hidden = !visible;
        if (visible) {
          visibleCount += 1;
        }
      });

      document.querySelectorAll('[data-property-group]').forEach(group => {
        const hasVisibleRows = Array.from(group.querySelectorAll('[data-property-row]'))
          .some(row => !row.hidden);
        group.hidden = !hasVisibleRows;
      });

      updatePropertySummary(visibleCount, rows.length, !!query);
      persistViewState();
    }

    if (propertyNameFilter && typeof persistedState.propertyNameFilter === 'string') {
      propertyNameFilter.value = persistedState.propertyNameFilter;
    }

    if (propertyNameFilter) {
      propertyNameFilter.addEventListener('input', applyPropertyNameFilter);
      propertyNameFilter.addEventListener('search', applyPropertyNameFilter);
    }

    if (filterMode) {
      filterMode.addEventListener('change', () => {
        if (!vscodeApi) {
          return;
        }

        vscodeApi.postMessage({
          type: 'setPropertyFilterMode',
          mode: filterMode.value
        });
      });
    }
    document.querySelectorAll('[data-apply-property]').forEach(button => {
      button.addEventListener('click', () => {
        if (!vscodeApi) {
          return;
        }

        const propertyName = button.getAttribute('data-apply-property');
        const row = button.closest('[data-property-row]');
        const input = row ? row.querySelector('[data-property-input]') : null;
        vscodeApi.postMessage({
          type: 'applyProperty',
          propertyName,
          propertyValue: input ? input.value : ''
        });
      });
    });
    document.querySelectorAll('[data-remove-property]').forEach(button => {
      button.addEventListener('click', () => {
        if (!vscodeApi) {
          return;
        }

        vscodeApi.postMessage({
          type: 'removeProperty',
          propertyName: button.getAttribute('data-remove-property')
        });
      });
    });
    document.querySelectorAll('[data-quick-set-property]').forEach(button => {
      button.addEventListener('click', () => {
        if (!vscodeApi) {
          return;
        }

        vscodeApi.postMessage({
          type: 'quickSetProperty',
          propertyName: button.getAttribute('data-quick-set-property'),
          propertyValue: button.getAttribute('data-quick-set-value') || ''
        });
      });
    });
    applyPropertyNameFilter();
  </script>
</body>
</html>`;
}

function buildPropertyGroups(properties) {
  const groups = new Map();
  for (const property of properties || []) {
    const category = property.category || 'General';
    if (!groups.has(category)) {
      groups.set(category, []);
    }

    groups.get(category).push(property);
  }

  return Array.from(groups.entries())
    .map(([category, items]) => {
      const rows = items.map(property => buildPropertyRow(property)).join('');
      return `<section class="group" data-property-group>
        <div class="group-title">${escapeHtml(category)}</div>
        <div class="group-table">${rows}</div>
      </section>`;
    })
    .join('');
}

function buildPropertyRow(property) {
  const propertyNameValue = String(property.name || '');
  const propertyName = escapeHtml(propertyNameValue);
  const propertyNameAttribute = escapeHtmlAttribute(propertyNameValue);
  const hint = escapeHtml([
    property.typeName,
    property.source,
    property.ownerTypeName,
    property.editorKind
  ].filter(Boolean).join(' • '));
  const editor = buildPropertyEditor(property);
  const quickSets = Array.isArray(property.quickSets) && property.quickSets.length > 0
    ? `<div class="quick-sets">${property.quickSets.map(quickSet => `
        <button
          class="secondary"
          type="button"
          data-quick-set-property="${propertyNameAttribute}"
          data-quick-set-value="${escapeHtmlAttribute(quickSet.value || '')}">${escapeHtml(quickSet.label || quickSet.value || 'Set')}</button>`).join('')}</div>`
    : '';
  const badges = [
    property.isPinned ? '<span class="badge">Pinned</span>' : '',
    property.isReadOnly ? '<span class="badge subtle">Read only</span>' : ''
  ].filter(Boolean).join('');

  return `<div class="row" data-property-row data-property-search="${escapeHtmlAttribute(normalizePropertySearchText(propertyNameValue))}">
    <div class="name-cell">
      <div class="name-line">
        <div class="name">${propertyName}</div>
        ${badges ? `<div class="badge-row">${badges}</div>` : ''}
      </div>
      <div class="hint">${hint}</div>
    </div>
    <div class="value-cell">
      ${editor}
      ${quickSets}
      <div class="actions">
        <button type="button" data-apply-property="${propertyNameAttribute}"${property.isReadOnly ? ' disabled' : ''}>Apply</button>
        <button class="secondary" type="button" data-remove-property="${propertyNameAttribute}"${!property.canReset || property.isReadOnly ? ' disabled' : ''}>Reset</button>
      </div>
    </div>
  </div>`;
}

function buildPropertyEditor(property) {
  const escapedValue = escapeHtmlAttribute(property.value || '');
  const isReadOnly = !!property.isReadOnly;
  const enumOptions = Array.isArray(property.enumOptions) ? property.enumOptions : [];
  if (enumOptions.length > 0) {
    return `<select class="editor-select" data-property-input${isReadOnly ? ' disabled' : ''}>
      ${enumOptions.map(option => `<option value="${escapeHtmlAttribute(option)}"${option === property.value ? ' selected' : ''}>${escapeHtml(option)}</option>`).join('')}
    </select>`;
  }

  const isMultiLine = typeof property.value === 'string' && property.value.length > 80;
  if (isMultiLine) {
    return `<textarea class="editor-textarea" data-property-input${isReadOnly ? ' readonly' : ''}>${escapeHtml(property.value || '')}</textarea>`;
  }

  return `<input class="editor-input" data-property-input value="${escapedValue}"${isReadOnly ? ' readonly' : ''}>`;
}

function normalizePropertySearchText(value) {
  return String(value || '').trim().toLowerCase();
}

function buildElementTooltip(element) {
  return [
    element.displayName || element.typeName || element.id,
    element.typeName || null,
    element.xamlName ? `x:Name=${element.xamlName}` : null,
    element.classes ? `Classes=${element.classes}` : null
  ].filter(Boolean).join('\n');
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function escapeHtmlAttribute(value) {
  return escapeHtml(value).replace(/`/g, '&#96;');
}

function sameText(left, right) {
  return String(left || '').trim().toLowerCase() === String(right || '').trim().toLowerCase();
}

module.exports = {
  DesignSessionController,
  DesignToolboxDocumentDropProvider,
  DesignToolboxDragAndDropController,
  renderPropertiesViewHtml
};
