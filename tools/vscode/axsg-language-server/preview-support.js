const path = require('path');
const fs = require('fs');
const cp = require('child_process');
const readline = require('readline');
const { EventEmitter } = require('events');
const vscode = require('vscode');
const {
  DEFAULT_REQUEST_TIMEOUT_MS,
  delayAsync,
  fetchPreviewPageHtmlAsync
} = require('./preview-fetch');
const {
  buildArguments,
  createCommandFailureMessage,
  createPreviewBuildPlan,
  createPreviewStartPlan,
  enumeratePreviewAssemblyArtifacts,
  extractPreviewSecurityCookie,
  hasPendingPreviewText,
  PREVIEW_COMPILER_MODE_AUTO,
  PREVIEW_COMPILER_MODE_AVALONIA,
  PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
  isResolvablePreviewHostProjectInfo,
  isUsablePreviewHostProjectInfo,
  isPreviewableProjectInfo,
  isUnderBuildOutput,
  normalizeFilePath,
  normalizePreviewCompilerMode,
  normalizePreviewViewportMetrics,
  normalizeMaybeEmptyPath,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  projectReferencesProject,
  resolveConfiguredProjectPath,
  resolveAvaloniaPreviewerToolPaths,
  resolvePreviewDesignAssemblyPath,
  resolveEffectivePreviewMode,
  resolvePreviewBuildMode,
  resolveLoopbackPreviewWebviewTarget,
  resolvePreviewDocumentText,
  resolvePreviewCompilerMode,
  samePath,
  shouldUseInlineLoopbackPreviewClient,
  shouldUseNoRestoreBuild,
  supportsSourceGeneratedLivePreview,
  tryParseMsbuildJson
} = require('./preview-utils');
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
} = require('./preview-webview-helpers');
const {
  DESIGN_TOOLBOX_ITEM_MIME,
  DESIGN_TOOLBOX_TEXT_PREFIX
} = require('./design-toolbox-dnd');

const PROJECT_INFO_PROPERTIES = [
  'TargetPath',
  'AvaloniaPreviewerNetCoreToolPath',
  'TargetFramework',
  'TargetFrameworks',
  'OutputType',
  'MSBuildProjectDirectory'
];

const DEFAULT_UPDATE_DELAY_MS = 300;
const DEFAULT_PREVIEW_SIZE_WAIT_MS = 750;
const HOST_PROJECT_STATE_PREFIX = 'axsg.preview.hostProject::';
const PREVIEW_HOST_ASSEMBLY_NAME = 'XamlToCSharpGenerator.PreviewerHost.dll';
const SOURCE_GENERATED_DESIGNER_HOST_ASSEMBLY_NAME = 'XamlToCSharpGenerator.Previewer.DesignerHost.dll';

class JsonLineHelperClient extends EventEmitter {
  constructor(command, args, options) {
    super();
    this.command = command;
    this.args = args;
    this.cwd = options.cwd;
    this.outputChannel = options.outputChannel;
    this.process = cp.spawn(command, args, {
      cwd: this.cwd,
      env: process.env,
      stdio: ['pipe', 'pipe', 'pipe']
    });
    this.pendingRequests = new Map();
    this.nextRequestId = 1;
    this.disposed = false;
    this.disposing = false;
    this.stdoutReader = readline.createInterface({ input: this.process.stdout });
    this.stdoutReader.on('line', line => this.handleStdoutLine(line));
    this.process.stderr.on('data', chunk => {
      this.appendOutput(`[preview-host stderr] ${String(chunk).trimEnd()}`);
    });
    this.process.on('error', error => {
      this.rejectAllPending(error);
      this.emit('exit', { exitCode: null, signal: null, error });
    });
    this.process.on('exit', (exitCode, signal) => {
      const error = this.disposed || this.disposing
        ? null
        : new Error(`Preview host exited unexpectedly (${exitCode ?? 'null'}${signal ? `, ${signal}` : ''}).`);
      if (error) {
        this.rejectAllPending(error);
      }

      this.emit('exit', { exitCode, signal, error });
    });
  }

  async sendCommand(command, payload, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS, allowWhileDisposing = false) {
    if (this.disposed || (this.disposing && !allowWhileDisposing)) {
      throw new Error('Preview host is not running.');
    }

    const requestId = String(this.nextRequestId++);
    const message = JSON.stringify({
      command,
      requestId,
      payload
    });

    const responsePromise = new Promise((resolve, reject) => {
      const timeoutHandle = setTimeout(() => {
        this.pendingRequests.delete(requestId);
        reject(new Error(`Preview host command '${command}' timed out.`));
      }, timeoutMs);

      this.pendingRequests.set(requestId, { resolve, reject, timeoutHandle, command });
    });

    this.process.stdin.write(`${message}\n`);
    return responsePromise;
  }

  async dispose() {
    if (this.disposed || this.disposing) {
      return;
    }

    this.disposing = true;

    try {
      await this.sendCommand('stop', {}, 5000, true);
    } catch {
      // Best effort shutdown.
    }

    this.disposed = true;
    this.disposing = false;

    this.rejectAllPending(new Error('Preview host was disposed.'));

    if (!this.process.killed) {
      this.process.kill();
    }

    this.stdoutReader.close();
  }

  handleStdoutLine(line) {
    if (!line || !line.trim()) {
      return;
    }

    let message;
    try {
      message = JSON.parse(line);
    } catch (error) {
      this.appendOutput(`[preview-host stdout] ${line}`);
      return;
    }

    if (message.kind === 'response') {
      const pending = this.pendingRequests.get(message.requestId ?? '');
      if (!pending) {
        return;
      }

      clearTimeout(pending.timeoutHandle);
      this.pendingRequests.delete(message.requestId);
      if (message.ok) {
        pending.resolve(message.payload ?? null);
      } else {
        pending.reject(new Error(message.error || `Preview host command '${pending.command}' failed.`));
      }
      return;
    }

    if (message.kind === 'event') {
      this.emit('event', message);
      return;
    }

    this.appendOutput(`[preview-host stdout] ${line}`);
  }

  rejectAllPending(error) {
    for (const pending of this.pendingRequests.values()) {
      clearTimeout(pending.timeoutHandle);
      pending.reject(error);
    }

    this.pendingRequests.clear();
  }

  appendOutput(text) {
    if (!text || !this.outputChannel) {
      return;
    }

    this.outputChannel.appendLine(text);
  }
}

class AvaloniaPreviewSession {
  constructor(controller, document, launchInfo) {
    this.controller = controller;
    this.documentUri = document.uri.toString();
    this.fileName = path.basename(document.fileName);
    this.launchInfo = launchInfo;
    this.helper = null;
    this.panel = null;
    this.currentPreviewUrl = '';
    this.currentLoopbackPreview = null;
    this.currentStatus = 'Starting preview...';
    this.pendingUpdateText = null;
    this.updateTimer = null;
    this.updateChain = Promise.resolve();
    this.disposed = false;
    this.startPromise = null;
    this.activeCompilerMode = null;
    this.previewUrlUpdateToken = 0;
    this.rawPreviewUrl = '';
    this.previewSize = null;
    this.previewSizeReady = createDeferred();
    this.previewSizeReadyResolved = false;
    this.designState = null;
  }

  reveal() {
    if (this.panel) {
      this.panel.reveal(vscode.ViewColumn.Beside, true);
    }
  }

  async start() {
    if (this.startPromise) {
      return this.startPromise;
    }

    this.createPanel();
    this.startPromise = this.startCore()
      .catch(error => {
        this.startPromise = null;
        throw error;
      });
    return this.startPromise;
  }

  isSourceGeneratedPreviewActive() {
    return this.activeCompilerMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED;
  }

  usesSourceGeneratedRefreshFlow() {
    return this.isSourceGeneratedPreviewActive() ||
      (!this.activeCompilerMode &&
        this.launchInfo.previewPlan &&
        resolveEffectivePreviewMode(this.launchInfo.previewPlan) === PREVIEW_COMPILER_MODE_SOURCE_GENERATED);
  }

  handleDocumentChanged(document) {
    if (this.disposed) {
      return;
    }

    this.scheduleLiveUpdate(document);
  }

  async handleDocumentSaved(document) {
    if (!this.usesSourceGeneratedRefreshFlow()) {
      return;
    }

    if (!this.controller.getConfiguration().get('preview.buildBeforeLaunch', true)) {
      this.setStatus('Source-generated preview is using live XAML updates. Build the project manually or enable axsg.preview.buildBeforeLaunch to realign generated output.');
      return;
    }

    await this.refreshSourceGeneratedPreview(document, 'Refreshing source-generated preview...');
  }

  async handleOpenRequest(document) {
    this.scheduleLiveUpdate(document);
  }

  scheduleLiveUpdate(document) {
    if (this.disposed) {
      return;
    }

    this.pendingUpdateText = document.getText();
    if (this.launchInfo) {
      this.launchInfo.documentText = this.pendingUpdateText;
    }
    this.setStatus('Preview update queued...');

    if (this.updateTimer) {
      clearTimeout(this.updateTimer);
    }

    const delayMs = this.controller.getConfiguration().get('preview.autoUpdateDelayMs', DEFAULT_UPDATE_DELAY_MS);
    this.updateTimer = setTimeout(() => {
      this.updateTimer = null;
      void this.flushPendingUpdate();
    }, Math.max(0, delayMs));
  }

  async refreshSourceGeneratedPreview(document, statusText) {
    if (this.disposed) {
      return;
    }

    this.updateChain = this.updateChain
      .then(async () => {
        this.setStatus(statusText);
        const refreshedLaunchInfo = await this.controller.refreshLaunchInfoForSession(this.launchInfo, document);
        this.launchInfo = refreshedLaunchInfo;

        const helper = this.helper;
        if (helper) {
          await this.resetHelperAsync(helper);
        } else {
          this.startPromise = null;
          this.currentPreviewUrl = '';
          this.activeCompilerMode = null;
        }

        await this.start();
      })
      .catch(error => {
        const message = error instanceof Error ? error.message : String(error);
        this.setStatus(`Source-generated preview refresh failed: ${message}`);
        void vscode.window.showWarningMessage(`AXSG source-generated preview refresh failed for ${this.fileName}: ${message}`);
      });

    await this.updateChain;
  }

  async dispose() {
    if (this.disposed) {
      return;
    }

    this.disposed = true;

    if (this.updateTimer) {
      clearTimeout(this.updateTimer);
      this.updateTimer = null;
    }

    if (this.panel) {
      const panel = this.panel;
      this.panel = null;
      panel.dispose();
    }

    if (this.helper) {
      await this.helper.dispose();
      this.helper.removeAllListeners();
      this.helper = null;
    }

    this.activeCompilerMode = null;
  }

  createPanel() {
    if (this.panel) {
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      'axsgPreview',
      `AXSG Preview: ${this.fileName}`,
      vscode.ViewColumn.Beside,
      Object.assign(
        {
          retainContextWhenHidden: true
        },
        createPreviewWebviewOptions()));

    panel.webview.html = createPreviewWebviewHtml(panel.webview, this.fileName, this.currentPreviewUrl, this.currentStatus);
    panel.webview.onDidReceiveMessage(message => {
      if (!message || !message.type) {
        return;
      }

      if (message.type === 'viewportSize') {
        this.handleViewportSizeMessage(message);
        return;
      }

      if (message.type === 'previewInput') {
        this.handlePreviewInputMessage(message);
        return;
      }

      if (message.type === 'transportLog' && message.message) {
        this.controller.getOutputChannel().appendLine(`[preview-webview] ${message.message}`);
        return;
      }

      if (this.controller.handleSessionWebviewMessage(this, message)) {
        return;
      }
    });
    panel.onDidDispose(() => {
      if (!this.disposed) {
        this.controller.removeSession(this.documentUri);
        void this.dispose();
      }
    });
    panel.onDidChangeViewState(event => {
      if (!this.disposed && event && event.webviewPanel && event.webviewPanel.active) {
        this.controller.notifySessionEvent(this, 'panelActivated', {});
      }
    });

    this.panel = panel;
    this.applyWebviewOptions();
  }

  async startCore() {
    const helperPath = resolveBundledPreviewHostPath(this.controller.extensionPath);
    if (!fs.existsSync(helperPath)) {
      throw new Error(`Bundled preview host not found at ${helperPath}. Run the extension packaging step first.`);
    }

    const dotNetCommand = this.controller.getConfiguration().get('preview.dotNetCommand', 'dotnet');
    const outputChannel = this.controller.getOutputChannel();
    outputChannel.appendLine(`[preview] starting ${this.fileName}`);

    const attempts = buildStartAttempts(this.controller.extensionPath, this.launchInfo);
    if (attempts.length === 0) {
      throw new Error('No preview start strategy is available for the selected project.');
    }

    const requestedMode = this.launchInfo.previewPlan && this.launchInfo.previewPlan.requestedMode
      ? this.launchInfo.previewPlan.requestedMode
      : PREVIEW_COMPILER_MODE_AUTO;
    let lastError = null;

    for (let attemptIndex = 0; attemptIndex < attempts.length; attemptIndex += 1) {
      const attempt = attempts[attemptIndex];
      const nextAttempt = attemptIndex < attempts.length - 1
        ? attempts[attemptIndex + 1]
        : null;
      try {
        this.setStatus(`Starting ${attempt.label} preview...`);
        await this.startAttemptAsync(helperPath, dotNetCommand, outputChannel, attempt);
        this.activeCompilerMode = attempt.mode;
        if (this.launchInfo && this.launchInfo.previewPlan) {
          this.launchInfo.previewPlan.resolvedMode = attempt.mode;
        }
        this.setStatus(getPreviewReadyStatus(this.fileName, attempt.mode));
        this.updatePanel();
        return;
      } catch (error) {
        lastError = error;
        const message = error instanceof Error ? error.message : String(error);
        if (requestedMode === PREVIEW_COMPILER_MODE_AUTO && nextAttempt) {
          outputChannel.appendLine(
            `[preview] ${attempt.label} preview was unavailable: ${message}. Falling back to ${nextAttempt.label}.`);
        } else {
          outputChannel.appendLine(`[preview] ${attempt.label} preview start failed: ${message}`);
        }

        const helper = this.helper;
        if (helper) {
          await this.resetHelperAsync(helper);
        }

        if (nextAttempt) {
          this.setStatus(`Falling back to ${nextAttempt.label} preview...`);
        }
      }
    }

    throw lastError || new Error('Failed to start the preview session.');
  }

  async startAttemptAsync(helperPath, dotNetCommand, outputChannel, attempt) {
    this.helper = new JsonLineHelperClient(dotNetCommand, [helperPath], {
      cwd: this.launchInfo.workspaceRoot,
      outputChannel
    });

    const helper = this.helper;
    helper.on('event', message => this.handleHelperEvent(message));
    helper.on('exit', ({ exitCode, error }) => {
      if (this.disposed || this.helper !== helper) {
        return;
      }

      const exitText = error ? error.message : `Preview host exited (${exitCode ?? 'null'}).`;
      void this.handleHostUnavailableAsync(helper, exitText, true);
    });

    const previewSize = await this.waitForInitialPreviewSizeAsync();
    const payload = {
      dotNetCommand,
      hostAssemblyPath: this.launchInfo.hostProject.targetPath,
      previewerToolPath: attempt.previewerToolPath,
      sourceAssemblyPath: this.launchInfo.previewSourceAssemblyPath || this.launchInfo.sourceProject.targetPath,
      sourceFilePath: this.launchInfo.projectContext.filePath || '',
      xamlFileProjectPath: normalizePreviewTargetPath(this.launchInfo.projectContext.targetPath),
      xamlText: this.launchInfo.documentText,
      previewCompilerMode: attempt.mode
    };
    if (previewSize) {
      payload.previewWidth = previewSize.width;
      payload.previewHeight = previewSize.height;
      payload.previewScale = previewSize.scale;
    }

    const startResult = await helper.sendCommand('start', payload);
    const previewUrl = startResult?.previewUrl || '';
    if (!previewUrl) {
      throw new Error('Avalonia preview host did not return a preview URL.');
    }

    await this.updatePreviewUrlAsync(previewUrl);
  }

  async flushPendingUpdate() {
    if (this.disposed || !hasPendingPreviewText(this.pendingUpdateText)) {
      return;
    }

    const updateText = this.pendingUpdateText;
    this.pendingUpdateText = null;
    if (this.launchInfo) {
      this.launchInfo.documentText = updateText;
    }
    this.setStatus('Applying XAML update...');

    this.updateChain = this.updateChain
      .then(async () => {
        await this.start();
        if (!this.helper) {
          throw new Error('Preview host is not available.');
        }

        await this.helper.sendCommand('update', { xamlText: updateText });
      })
      .catch(error => {
        const message = error instanceof Error ? error.message : String(error);
        this.setStatus(`Preview update failed: ${message}`);
        void vscode.window.showWarningMessage(`AXSG preview update failed for ${this.fileName}: ${message}`);
      });

    await this.updateChain;
  }

  handleHelperEvent(message) {
    const eventName = message.event;
    const payload = message.payload || {};
    if (eventName === 'previewStarted' && payload.previewUrl) {
      void this.updatePreviewUrlAsync(payload.previewUrl);
      this.setStatus(getPreviewReadyStatus(this.fileName, this.activeCompilerMode));
      this.updatePanel();
      this.controller.notifySessionEvent(this, 'previewStarted', payload);
      return;
    }

    if (eventName === 'updateResult') {
      if (payload.succeeded) {
        this.setStatus(`Preview updated at ${new Date().toLocaleTimeString()}.`);
      } else {
        const exceptionText = payload.exception && payload.exception.message
          ? ` ${payload.exception.message}`
          : '';
        this.setStatus(`Preview error: ${payload.error || 'Unknown error.'}${exceptionText}`);
      }
      this.controller.notifySessionEvent(this, 'updateResult', payload);
      return;
    }

    if (eventName === 'hostExited') {
      const exitText = `Preview host exited (${payload.exitCode ?? 'null'}).`;
      const helper = this.helper;
      if (!helper) {
        this.currentPreviewUrl = '';
        this.setStatus(exitText);
        return;
      }

      void this.handleHostUnavailableAsync(helper, exitText, true);
      this.controller.notifySessionEvent(this, 'hostExited', payload);
      return;
    }

    if (eventName === 'log' && payload.message) {
      this.controller.getOutputChannel().appendLine(payload.message);
    }
  }

  setStatus(text) {
    this.currentStatus = text;
    this.updatePanel();
  }

  async handleHostUnavailableAsync(helper, statusText, notifyUser) {
    await this.resetHelperAsync(helper);
    if (this.disposed) {
      return;
    }

    this.setStatus(statusText);
    if (notifyUser) {
      await vscode.window.showWarningMessage(`AXSG preview stopped for ${this.fileName}: ${statusText}`);
    }
  }

  async resetHelperAsync(helper) {
    if (this.helper === helper) {
      this.helper = null;
      this.startPromise = null;
      this.previewUrlUpdateToken += 1;
      this.rawPreviewUrl = '';
      this.currentPreviewUrl = '';
      this.currentLoopbackPreview = null;
      this.activeCompilerMode = null;
      this.applyWebviewOptions();
    }

    helper.removeAllListeners();

    try {
      await helper.dispose();
    } catch {
      // Best effort cleanup of a failed preview helper.
    }
  }

  updatePanel() {
    if (!this.panel) {
      return;
    }

    this.panel.webview.postMessage({
      type: 'update',
      previewUrl: this.currentPreviewUrl,
      loopbackPreview: this.currentLoopbackPreview,
      status: this.currentStatus,
      designState: this.designState
    });
  }

  applyWebviewOptions() {
    if (!this.panel) {
      return;
    }

    const portMapping = [];
    if (this.currentLoopbackPreview && Number.isInteger(this.currentLoopbackPreview.port)) {
      portMapping.push({
        webviewPort: this.currentLoopbackPreview.port,
        extensionHostPort: this.currentLoopbackPreview.port
      });
    }

    this.panel.webview.options = createPreviewWebviewOptions(portMapping);
  }

  handleViewportSizeMessage(message) {
    if (this.disposed) {
      return;
    }

    const previewSize = normalizePreviewViewportMetrics(
      message.width,
      message.height,
      message.devicePixelRatio);
    if (!previewSize) {
      return;
    }

    if (this.previewSizeReadyResolved) {
      return;
    }

    this.previewSize = previewSize;
    this.previewSizeReadyResolved = true;
    this.previewSizeReady.resolve(previewSize);
  }

  handlePreviewInputMessage(message) {
    if (this.disposed || !this.helper || !this.currentLoopbackPreview) {
      return;
    }

    const inputs = Array.isArray(message.inputs)
      ? message.inputs.filter(input => input && typeof input === 'object' && typeof input.eventType === 'string')
      : [];
    if (inputs.length === 0) {
      return;
    }

    const helper = this.helper;
    const pending = [];
    for (const input of inputs) {
      pending.push(helper.sendCommand('input', input, 5000));
    }

    void Promise.allSettled(pending).then(results => {
      const firstRejected = results.find(result => result.status === 'rejected');
      if (!firstRejected || this.disposed || this.helper !== helper) {
        return;
      }

      const error = firstRejected.reason instanceof Error
        ? firstRejected.reason.message
        : String(firstRejected.reason);
      if (error === 'Preview host is not running.') {
        return;
      }

      this.controller.getOutputChannel().appendLine(
        `[preview] failed to forward keyboard input for ${this.fileName}: ${error}`);
    });
  }

  async sendDesignCommand(operation, argumentsPayload = {}) {
    if (this.disposed) {
      throw new Error('Preview session is not available.');
    }

    await this.start();
    if (!this.helper) {
      throw new Error('Preview host is not available.');
    }

    return this.helper.sendCommand('design', {
      operation,
      arguments: argumentsPayload || {}
    });
  }

  setDesignState(nextState) {
    this.designState = nextState || null;
    this.updatePanel();
  }

  async waitForInitialPreviewSizeAsync() {
    if (this.previewSize) {
      return this.previewSize;
    }

    const timeoutMs = DEFAULT_PREVIEW_SIZE_WAIT_MS;
    if (timeoutMs === 0) {
      return null;
    }

    return Promise.race([
      this.previewSizeReady.promise.then(() => this.previewSize),
      delayAsync(timeoutMs).then(() => this.previewSize)
    ]);
  }

  async updatePreviewUrlAsync(previewUrl) {
    const normalizedPreviewUrl = String(previewUrl || '').trim();
    const updateToken = ++this.previewUrlUpdateToken;
    this.rawPreviewUrl = normalizedPreviewUrl;

    if (!normalizedPreviewUrl) {
      this.currentPreviewUrl = '';
      this.currentLoopbackPreview = null;
      this.applyWebviewOptions();
      this.updatePanel();
      return;
    }

    const loopbackTarget = resolveLoopbackPreviewWebviewTarget(normalizedPreviewUrl);
    if (loopbackTarget && shouldUseInlineLoopbackPreviewClient(vscode.env.remoteName, vscode.env.uiKind)) {
      const previewHtml = await fetchPreviewPageHtmlAsync(loopbackTarget.previewUrl);
      const securityCookie = extractPreviewSecurityCookie(previewHtml);
      if (!securityCookie) {
        throw new Error(`Could not extract the Avalonia preview security cookie from ${loopbackTarget.previewUrl}.`);
      }

      if (this.disposed || updateToken !== this.previewUrlUpdateToken || this.rawPreviewUrl !== normalizedPreviewUrl) {
        return;
      }

      this.currentPreviewUrl = '';
      this.currentLoopbackPreview = {
        previewUrl: loopbackTarget.previewUrl,
        webSocketUrl: loopbackTarget.webSocketUrl,
        port: loopbackTarget.port,
        securityCookie,
        previewScale: this.previewSize && Number.isFinite(this.previewSize.scale)
          ? this.previewSize.scale
          : 1
      };
      this.applyWebviewOptions();
      this.updatePanel();
      return;
    }

    let resolvedPreviewUrl = normalizedPreviewUrl;
    try {
      const externalUri = await vscode.env.asExternalUri(vscode.Uri.parse(normalizedPreviewUrl));
      resolvedPreviewUrl = externalUri.toString(true);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.controller.getOutputChannel().appendLine(
        `[preview] failed to externalize preview URL '${normalizedPreviewUrl}': ${message}`);
    }

    if (this.disposed || updateToken !== this.previewUrlUpdateToken || this.rawPreviewUrl !== normalizedPreviewUrl) {
      return;
    }

    this.currentLoopbackPreview = null;
    this.currentPreviewUrl = resolvedPreviewUrl;
    this.applyWebviewOptions();
    this.updatePanel();
  }
}

class AvaloniaPreviewController {
  constructor(options) {
    this.eventEmitter = new EventEmitter();
    this.context = options.context;
    this.extensionPath = options.context.extensionPath;
    this.ensureClientStarted = options.ensureClientStarted;
    this.getOutputChannel = options.getOutputChannel;
    this.isXamlDocument = options.isXamlDocument;
    this.workspaceRoot = options.workspaceRoot;
    this.sessions = new Map();
    this.projectInfoCache = new Map();
    this.projectReferenceCache = new Map();
    this.designController = null;
  }

  register(context) {
    context.subscriptions.push(vscode.commands.registerCommand('axsg.preview.open', async () => {
      try {
        await this.openPreviewForActiveEditor();
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        const choice = await vscode.window.showErrorMessage(`AXSG preview failed: ${message}`, 'Show Log');
        if (choice === 'Show Log') {
          const channel = this.getOutputChannel && this.getOutputChannel();
          if (channel && typeof channel.show === 'function') {
            channel.show(true);
          }
        }
      }
    }));
    context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
      const session = this.sessions.get(event.document.uri.toString());
      if (session) {
        session.handleDocumentChanged(event.document);
      }
    }));
    context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => {
      const session = this.sessions.get(document.uri.toString());
      if (session) {
        void session.handleDocumentSaved(document);
      }
    }));
    context.subscriptions.push({
      dispose: () => {
        for (const session of this.sessions.values()) {
          void session.dispose();
        }
        this.sessions.clear();
      }
    });
  }

  async dispose() {
    const sessions = Array.from(this.sessions.values());
    this.sessions.clear();
    for (const session of sessions) {
      await session.dispose();
    }
  }

  async openPreviewForActiveEditor() {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !this.isXamlDocument(editor.document)) {
      throw new Error('Open an AXAML or XAML document to start the preview.');
    }

    const document = editor.document;
    const existing = this.sessions.get(document.uri.toString());
    if (existing) {
      existing.reveal();
      await existing.handleOpenRequest(document);
      return;
    }

    await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: `AXSG Preview: ${path.basename(document.fileName)}`,
        cancellable: false
      },
      async progress => {
        progress.report({ message: 'Resolving project context...' });
        const launchInfo = await this.resolveLaunchInfo(document, progress);
        progress.report({ message: 'Starting preview host...' });
        const createdSession = new AvaloniaPreviewSession(this, document, launchInfo);
        this.sessions.set(document.uri.toString(), createdSession);
        try {
          await createdSession.start();
          return;
        } catch (error) {
          this.removeSession(document.uri.toString());
          await createdSession.dispose();
          throw error;
        }
      });
  }

  removeSession(documentUri) {
    const existing = this.sessions.get(documentUri);
    this.sessions.delete(documentUri);
    if (existing) {
      this.notifySessionEvent(existing, 'disposed', {});
    }
  }

  getConfiguration() {
    return vscode.workspace.getConfiguration('axsg');
  }

  onSessionEvent(listener) {
    this.eventEmitter.on('sessionEvent', listener);
    return new vscode.Disposable(() => this.eventEmitter.off('sessionEvent', listener));
  }

  notifySessionEvent(session, event, payload) {
    const descriptor = { session, event, payload: payload || {} };
    this.eventEmitter.emit('sessionEvent', descriptor);
    if (this.designController && typeof this.designController.handleSessionEvent === 'function') {
      void this.designController.handleSessionEvent(descriptor);
    }
  }

  handleSessionWebviewMessage(session, message) {
    if (!this.designController || typeof this.designController.handleSessionWebviewMessage !== 'function') {
      return false;
    }

    return this.designController.handleSessionWebviewMessage(session, message);
  }

  setDesignController(designController) {
    this.designController = designController || null;
  }

  getSession(documentUri) {
    return this.sessions.get(typeof documentUri === 'string' ? documentUri : documentUri.toString()) || null;
  }

  getActiveSession() {
    for (const session of this.sessions.values()) {
      if (session && session.panel && session.panel.active) {
        return session;
      }
    }

    const editor = vscode.window.activeTextEditor;
    if (editor) {
      const editorSession = this.getSession(editor.document.uri.toString());
      if (editorSession) {
        return editorSession;
      }
    }

    return null;
  }

  async resolveLaunchInfo(document, progress) {
    const workspaceRoot = this.getWorkspaceRootForUri(document.uri);
    const client = await this.ensureClientStarted();
    if (!client) {
      throw new Error('AXSG language server is not available.');
    }

    const projectContext = await client.sendRequest('axsg/preview/projectContext', {
      textDocument: {
        uri: document.uri.toString()
      },
      workspaceRoot
    });
    if (!projectContext || !projectContext.projectPath || !projectContext.targetPath) {
      throw new Error('Could not resolve the containing project for this XAML file.');
    }

    const configuration = this.getConfiguration();
    const dotNetCommand = configuration.get('preview.dotNetCommand', 'dotnet');
    const preferredTargetFramework = configuration.get('preview.targetFramework', '');
    const requestedCompilerMode = normalizePreviewCompilerMode(
      configuration.get('preview.compilerMode', PREVIEW_COMPILER_MODE_AUTO));
    const projectState = await this.resolveProjectLaunchState({
      projectContext,
      workspaceRoot,
      dotNetCommand,
      preferredTargetFramework,
      progress,
      requestedCompilerMode,
      buildReason: 'launch',
      documentFilePath: document.fileName
    });
    const launchInfo = this.createLaunchInfo(
      projectContext,
      workspaceRoot,
      document.getText(),
      projectState.sourceProject,
      projectState.hostProject,
      await this.resolvePreviewSourceAssemblyPath(projectState.sourceProject, projectState.hostProject),
      requestedCompilerMode);
    launchInfo.documentText = resolvePreviewDocumentText(
      document.getText(),
      tryReadDocumentTextFromDisk(document.fileName),
      document.isDirty,
      resolveEffectivePreviewMode(launchInfo.previewPlan));
    return launchInfo;
  }

  async refreshLaunchInfoForSession(launchInfo, document) {
    const configuration = this.getConfiguration();
    const dotNetCommand = configuration.get('preview.dotNetCommand', 'dotnet');
    const preferredTargetFramework = configuration.get('preview.targetFramework', '');
    const requestedCompilerMode = normalizePreviewCompilerMode(
      configuration.get('preview.compilerMode', launchInfo.previewPlan.requestedMode || PREVIEW_COMPILER_MODE_AUTO));
    const projectState = await this.resolveProjectLaunchState({
      projectContext: launchInfo.projectContext,
      workspaceRoot: launchInfo.workspaceRoot,
      dotNetCommand,
      preferredTargetFramework,
      progress: null,
      hostProjectPath: launchInfo.hostProject.projectPath,
      requestedCompilerMode,
      buildReason: 'save',
      documentFilePath: document.fileName,
      activePreviewMode: resolveEffectivePreviewMode(launchInfo.previewPlan)
    });
    const refreshedLaunchInfo = this.createLaunchInfo(
      launchInfo.projectContext,
      launchInfo.workspaceRoot,
      document.getText(),
      projectState.sourceProject,
      projectState.hostProject,
      await this.resolvePreviewSourceAssemblyPath(projectState.sourceProject, projectState.hostProject),
      requestedCompilerMode);
    refreshedLaunchInfo.documentText = resolvePreviewDocumentText(
      document.getText(),
      tryReadDocumentTextFromDisk(document.fileName),
      document.isDirty,
      resolveEffectivePreviewMode(refreshedLaunchInfo.previewPlan));
    return refreshedLaunchInfo;
  }

  createLaunchInfo(projectContext, workspaceRoot, documentText, sourceProject, hostProject, previewSourceAssemblyPath, requestedCompilerMode) {
    return {
      projectContext,
      workspaceRoot,
      documentText,
      sourceProject,
      hostProject,
      previewSourceAssemblyPath,
      previewPlan: this.createPreviewPlan(requestedCompilerMode, sourceProject)
    };
  }

  async resolvePreviewSourceAssemblyPath(sourceProject, hostProject) {
    const sourceTargetPath = normalizeMaybeEmptyPath(sourceProject && sourceProject.targetPath);
    if (!sourceTargetPath) {
      return sourceTargetPath;
    }

    const sourceProjectPath = normalizeMaybeEmptyPath(sourceProject && sourceProject.projectPath);
    const hostProjectPath = normalizeMaybeEmptyPath(hostProject && hostProject.projectPath);
    const hostTargetPath = normalizeMaybeEmptyPath(hostProject && hostProject.targetPath);
    const hostReferencesSource = sourceProjectPath && hostProjectPath
      ? await this.hostProjectReferencesSourceProject(hostProjectPath, sourceProjectPath)
      : false;
    const previewSourceAssemblyPath = resolvePreviewDesignAssemblyPath(
      sourceTargetPath,
      hostTargetPath,
      hostReferencesSource);

    synchronizePreviewSourceAssemblyArtifacts(
      sourceTargetPath,
      previewSourceAssemblyPath,
      this.getOutputChannel());
    return previewSourceAssemblyPath;
  }

  createPreviewPlan(requestedCompilerMode, sourceProject) {
    const previewPlan = resolvePreviewCompilerMode(requestedCompilerMode, sourceProject);
    if (previewPlan.requestedMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED &&
        !previewPlan.sourceGeneratedSupported) {
      throw new Error(
        'AXSG source-generated preview was requested, but the project output does not contain XamlToCSharpGenerator.Runtime.Avalonia. Build the project or switch axsg.preview.compilerMode to avalonia.');
    }

    return previewPlan;
  }

  async resolveProjectLaunchState(options) {
    const projectContext = options.projectContext;
    const workspaceRoot = options.workspaceRoot;
    const dotNetCommand = options.dotNetCommand;
    const preferredTargetFramework = options.preferredTargetFramework;
    const progress = options.progress;
    const hostProjectPath = options.hostProjectPath || '';
    const requestedCompilerMode = options.requestedCompilerMode || PREVIEW_COMPILER_MODE_AUTO;
    const buildReason = options.buildReason || 'launch';
    const documentFilePath = options.documentFilePath || '';
    const activePreviewMode = options.activePreviewMode || '';
    const buildBeforeLaunch = this.getConfiguration().get('preview.buildBeforeLaunch', true);
    const allowProvisionalAutoHost = requestedCompilerMode === PREVIEW_COMPILER_MODE_AUTO && buildBeforeLaunch;

    if (progress) {
      progress.report({ message: 'Evaluating source project...' });
    }

    const sourceProject = await this.getProjectInfo(
      projectContext.projectPath,
      preferredTargetFramework,
      dotNetCommand,
      false,
      workspaceRoot);

    let hostProject;
    if (hostProjectPath) {
      hostProject = await this.getProjectInfo(
        hostProjectPath,
        preferredTargetFramework,
        dotNetCommand,
        false,
        workspaceRoot);
    } else {
      if (progress) {
        progress.report({ message: 'Selecting preview host project...' });
      }

      hostProject = await this.resolveHostProject(
        projectContext.projectPath,
        sourceProject,
        dotNetCommand,
        workspaceRoot,
        requestedCompilerMode,
        {
          allowAutoExecutableFallback: allowProvisionalAutoHost
        });
    }

    let resolvedState = await this.buildAndRefreshProjectState({
      projectContext,
      workspaceRoot,
      dotNetCommand,
      preferredTargetFramework,
      sourceProject,
      hostProject,
      progress,
      requestedCompilerMode,
      buildReason,
      documentFilePath,
      activePreviewMode
    });

    if (!isUsablePreviewHostProjectInfo(resolvedState.hostProject, resolvedState.sourceProject, requestedCompilerMode)) {
      if (progress) {
        progress.report({ message: 'Re-evaluating preview host project...' });
      }

      const resolvedHostProject = await this.resolveHostProject(
        projectContext.projectPath,
        resolvedState.sourceProject,
        dotNetCommand,
        workspaceRoot,
        requestedCompilerMode,
        {
          allowAutoExecutableFallback: false
        });

      if (!samePath(resolvedHostProject.projectPath, resolvedState.hostProject.projectPath)) {
        resolvedState = await this.buildAndRefreshProjectState({
          projectContext,
          workspaceRoot,
          dotNetCommand,
          preferredTargetFramework,
          sourceProject: resolvedState.sourceProject,
          hostProject: resolvedHostProject,
          progress,
          requestedCompilerMode,
          buildReason,
          documentFilePath,
          activePreviewMode: options.activePreviewMode ||
            resolvePreviewBuildMode(requestedCompilerMode, resolvedState.sourceProject)
        });
      }
    }

    return resolvedState;
  }

  async buildAndRefreshProjectState(options) {
    const configuration = this.getConfiguration();
    if (!configuration.get('preview.buildBeforeLaunch', true)) {
      return {
        sourceProject: options.sourceProject,
        hostProject: options.hostProject
      };
    }

    const buildPreviewMode = options.activePreviewMode ||
      resolvePreviewBuildMode(options.requestedCompilerMode, options.sourceProject);
    const hostBuildIncludesSource = await this.hostProjectReferencesSourceProject(
      options.hostProject.projectPath,
      options.sourceProject.projectPath);
    const buildPlan = createPreviewBuildPlan({
      buildReason: options.buildReason,
      previewMode: buildPreviewMode,
      documentFilePath: options.documentFilePath,
      sourceProjectPath: options.sourceProject.projectPath,
      sourceTargetPath: options.sourceProject.targetPath,
      hostProjectPath: options.hostProject.projectPath,
      hostTargetPath: options.hostProject.targetPath,
      hostBuildIncludesSource,
      requiresSourceGeneratedCapabilityRefresh:
        buildPreviewMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED &&
        !supportsSourceGeneratedLivePreview(options.sourceProject)
    });

    if (!buildPlan.buildHost && !buildPlan.buildSource) {
      return {
        sourceProject: options.sourceProject,
        hostProject: options.hostProject
      };
    }

    if (buildPlan.buildSource) {
      if (options.progress) {
        options.progress.report({ message: `Building ${path.basename(options.sourceProject.projectPath)}...` });
      }

      await runDotNetBuildCommand(
        options.dotNetCommand,
        options.sourceProject.projectPath,
        options.sourceProject.targetFramework,
        options.workspaceRoot,
        this.getOutputChannel());
    }

    if (buildPlan.buildHost) {
      if (options.progress) {
        options.progress.report({ message: `Building ${path.basename(options.hostProject.projectPath)}...` });
      }

      await runDotNetBuildCommand(
        options.dotNetCommand,
        options.hostProject.projectPath,
        options.hostProject.targetFramework,
        options.workspaceRoot,
        this.getOutputChannel());
    }

    if (options.progress) {
      options.progress.report({ message: 'Refreshing build outputs...' });
    }

    const refreshedHostProject = buildPlan.buildHost
      ? await this.getProjectInfo(
        options.hostProject.projectPath,
        options.hostProject.targetFramework || options.preferredTargetFramework,
        options.dotNetCommand,
        true,
        options.workspaceRoot)
      : options.hostProject;
    const refreshedSourceProject = samePath(options.hostProject.projectPath, options.sourceProject.projectPath)
      ? refreshedHostProject
      : buildPlan.buildSource
        ? await this.getProjectInfo(
          options.projectContext.projectPath,
          options.sourceProject.targetFramework || options.preferredTargetFramework,
          options.dotNetCommand,
          true,
          options.workspaceRoot)
        : options.sourceProject;

    return {
      sourceProject: refreshedSourceProject,
      hostProject: refreshedHostProject
    };
  }

  async hostProjectReferencesSourceProject(hostProjectPath, sourceProjectPath) {
    if (!hostProjectPath || !sourceProjectPath || samePath(hostProjectPath, sourceProjectPath)) {
      return true;
    }

    const cacheKey = `${hostProjectPath}::${sourceProjectPath}`;
    if (this.projectReferenceCache.has(cacheKey)) {
      return this.projectReferenceCache.get(cacheKey);
    }

    const result = projectReferencesProject(hostProjectPath, sourceProjectPath);
    this.projectReferenceCache.set(cacheKey, result);
    return result;
  }

  async resolveHostProject(sourceProjectPath, sourceProjectInfo, dotNetCommand, workspaceRoot, requestedCompilerMode, options = {}) {
    const allowAutoExecutableFallback = Boolean(options.allowAutoExecutableFallback);
    const isHostCandidateUsable = projectInfo =>
      isResolvablePreviewHostProjectInfo(projectInfo, sourceProjectInfo, requestedCompilerMode, allowAutoExecutableFallback);

    if (isHostCandidateUsable(sourceProjectInfo)) {
      return sourceProjectInfo;
    }

    const configuration = this.getConfiguration();
    const configuredHostProject = configuration.get('preview.hostProject', '');
    if (configuredHostProject) {
      const resolvedConfiguredProject = resolveConfiguredProjectPath(configuredHostProject, workspaceRoot);
      const configuredInfo = await this.getProjectInfo(
        resolvedConfiguredProject,
        configuration.get('preview.targetFramework', ''),
        dotNetCommand,
        false,
        workspaceRoot);
      if (!isHostCandidateUsable(configuredInfo)) {
        throw new Error(`Configured preview host project is not usable for the selected preview mode: ${resolvedConfiguredProject}`);
      }

      return configuredInfo;
    }

    const rememberedKey = `${HOST_PROJECT_STATE_PREFIX}${sourceProjectPath}`;
    const rememberedHostProject = this.context.workspaceState.get(rememberedKey);
    if (rememberedHostProject && fs.existsSync(rememberedHostProject)) {
      const rememberedInfo = await this.getProjectInfo(
        rememberedHostProject,
        configuration.get('preview.targetFramework', ''),
        dotNetCommand,
        false,
        workspaceRoot);
      if (isHostCandidateUsable(rememberedInfo)) {
        return rememberedInfo;
      }
    }

    const projectUris = await vscode.workspace.findFiles('**/*.csproj');
    const candidatePaths = projectUris
      .map(uri => uri.fsPath)
      .filter(projectPath => !isUnderBuildOutput(projectPath) && !samePath(projectPath, sourceProjectPath))
      .sort((left, right) => left.localeCompare(right));

    const previewableCandidates = [];
    for (const candidatePath of candidatePaths) {
      const candidateInfo = await this.getProjectInfo(
        candidatePath,
        configuration.get('preview.targetFramework', ''),
        dotNetCommand,
        false,
        workspaceRoot);
      if (isHostCandidateUsable(candidateInfo)) {
        previewableCandidates.push(candidateInfo);
      }
    }

    if (previewableCandidates.length === 0) {
      throw new Error('No usable Avalonia executable project was found in the workspace. Set axsg.preview.hostProject to choose one explicitly.');
    }

    if (previewableCandidates.length === 1) {
      await this.context.workspaceState.update(rememberedKey, previewableCandidates[0].projectPath);
      return previewableCandidates[0];
    }

    const selection = await vscode.window.showQuickPick(
      previewableCandidates.map(candidate => ({
        label: path.basename(candidate.projectPath),
        description: path.relative(workspaceRoot, candidate.projectPath),
        detail: `${candidate.targetFramework || '<default>'} -> ${candidate.targetPath}`,
        candidate
      })),
      {
        title: 'Select an Avalonia preview host project'
      });

    if (!selection) {
      throw new Error('Preview start canceled because no host project was selected.');
    }

    await this.context.workspaceState.update(rememberedKey, selection.candidate.projectPath);
    return selection.candidate;
  }

  async getProjectInfo(projectPath, preferredTargetFramework, dotNetCommand, forceRefresh = false, workspaceRoot = '') {
    const effectiveWorkspaceRoot = workspaceRoot || this.getWorkspaceRootForFile(projectPath);
    const cacheKey = `${effectiveWorkspaceRoot}::${projectPath}::${preferredTargetFramework || '<auto>'}`;
    if (!forceRefresh && this.projectInfoCache.has(cacheKey)) {
      return this.projectInfoCache.get(cacheKey);
    }

    let directInfo;
    try {
      directInfo = await evaluateProjectInfo(
        projectPath,
        preferredTargetFramework,
        dotNetCommand,
        effectiveWorkspaceRoot);
    } catch (error) {
      if (!preferredTargetFramework) {
        throw error;
      }

      directInfo = await evaluateProjectInfo(
        projectPath,
        '',
        dotNetCommand,
        effectiveWorkspaceRoot);
    }
    let resolvedInfo = directInfo;
    if ((!directInfo.targetFramework || directInfo.targetFrameworks) && directInfo.targetFrameworks) {
      const pickedTargetFramework = pickPreviewTargetFramework(
        directInfo.targetFrameworks,
        preferredTargetFramework);
      if (pickedTargetFramework && pickedTargetFramework !== directInfo.targetFramework) {
        resolvedInfo = await evaluateProjectInfo(
          projectPath,
          pickedTargetFramework,
          dotNetCommand,
          effectiveWorkspaceRoot);
      }
    }

    this.projectInfoCache.set(cacheKey, resolvedInfo);
    return resolvedInfo;
  }

  getWorkspaceRootForUri(uri) {
    const folder = uri ? vscode.workspace.getWorkspaceFolder(uri) : null;
    if (folder && folder.uri && folder.uri.fsPath) {
      return folder.uri.fsPath;
    }

    return this.workspaceRoot;
  }

  getWorkspaceRootForFile(filePath) {
    if (!filePath) {
      return this.workspaceRoot;
    }

    return this.getWorkspaceRootForUri(vscode.Uri.file(filePath));
  }
}

async function evaluateProjectInfo(projectPath, preferredTargetFramework, dotNetCommand, workspaceRoot) {
  const args = ['msbuild', projectPath, '-nologo'];
  for (const property of PROJECT_INFO_PROPERTIES) {
    args.push(`-getProperty:${property}`);
  }

  if (preferredTargetFramework) {
    args.push(`-p:TargetFramework=${preferredTargetFramework}`);
  }

  const stdout = await execFileAsync(dotNetCommand, args, { cwd: workspaceRoot });
  const parsed = tryParseMsbuildJson(stdout);
  if (!parsed || !parsed.Properties) {
    throw new Error(`MSBuild did not return a valid property payload for ${projectPath}.`);
  }

  const properties = parsed.Properties;
  return {
    projectPath: normalizeFilePath(projectPath),
    projectDirectory: normalizeFilePath(properties.MSBuildProjectDirectory || path.dirname(projectPath)),
    targetPath: normalizeMaybeEmptyPath(properties.TargetPath),
    previewerToolPath: normalizeMaybeEmptyPath(properties.AvaloniaPreviewerNetCoreToolPath),
    targetFramework: properties.TargetFramework || '',
    targetFrameworks: properties.TargetFrameworks || '',
    outputType: properties.OutputType || ''
  };
}

function createPreviewWebviewHtml(webview, title, previewUrl, status) {
  const iframeUrl = previewUrl ? escapeHtml(previewUrl) : '';
  const statusText = escapeHtml(status || 'Preview starting...');
  const escapedTitle = escapeHtml(title);
  const frameSourcePolicy = `${webview.cspSource} http: https: vscode-remote: vscode-webview: vscode-webview-resource:`;
  const zoomOutIconPath = 'M11 8a.5.5 0 0 1 0 1H6a.5.5 0 0 1 0-1h5ZM8.5 2a6.5 6.5 0 0 1 4.94 10.73l3.41 3.42a.5.5 0 0 1-.63.76l-.07-.06-3.42-3.41A6.5 6.5 0 1 1 8.5 2Zm0 1a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11Z';
  const zoomResetIconPath = 'M4 10a6 6 0 0 1 10.47-4H12.5a.5.5 0 0 0 0 1h3a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-1 0v1.6a7 7 0 1 0 1.98 4.36.5.5 0 1 0-1 .08L16 10a6 6 0 0 1-12 0Z';
  const zoomInIconPath = 'M8.5 5.5c.28 0 .5.22.5.5v2h2a.5.5 0 0 1 0 1H9v2a.5.5 0 0 1-1 0V9H6a.5.5 0 0 1 0-1h2V6c0-.28.22-.5.5-.5Zm0-3.5a6.5 6.5 0 0 1 4.94 10.73l3.41 3.42a.5.5 0 0 1-.63.76l-.07-.06-3.42-3.41A6.5 6.5 0 1 1 8.5 2Zm0 1a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11Z';
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-src ${frameSourcePolicy}; connect-src ${frameSourcePolicy} ws: wss:;">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${escapedTitle}</title>
  <style>
    :root {
      color-scheme: light dark;
      font-family: var(--vscode-font-family);
      --axsg-toolbar-height: 56px;
      --axsg-stage-padding: 24px;
      --axsg-surface-radius: 0px;
      --axsg-overlay-selected: #0b76d1;
      --axsg-overlay-hover: #ff8c00;
    }

    body {
      margin: 0;
      padding: 0;
      background:
        radial-gradient(circle at top, rgba(56, 139, 253, 0.12), transparent 34%),
        linear-gradient(180deg, var(--vscode-editor-background) 0%, var(--vscode-sideBar-background, var(--vscode-editor-background)) 100%);
      color: var(--vscode-editor-foreground);
      overflow: hidden;
    }

    .shell {
      display: grid;
      grid-template-rows: var(--axsg-toolbar-height) minmax(0, 1fr);
      height: 100vh;
    }

    .toolbar {
      box-sizing: border-box;
      display: grid;
      grid-template-columns: minmax(220px, 1fr) auto;
      align-items: center;
      gap: 16px;
      min-height: var(--axsg-toolbar-height);
      padding: 0 16px;
      border-bottom: 1px solid var(--vscode-panel-border);
      background: color-mix(in srgb, var(--vscode-editorWidget-background) 94%, transparent);
      backdrop-filter: blur(18px);
    }

    .toolbar-copy {
      min-width: 0;
      display: grid;
      gap: 2px;
    }

    .toolbar-meta {
      display: inline-flex;
      align-items: center;
      justify-self: end;
      gap: 8px;
      flex-wrap: wrap;
    }

    .toolbar-label {
      font-size: 10px;
      font-weight: 600;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--vscode-descriptionForeground);
    }

    .status {
      min-width: 0;
      font-size: 12px;
      line-height: 1.4;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .toolbar-controls,
    .toolbar-actions {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      flex-shrink: 0;
      padding: 4px;
      border: 1px solid var(--vscode-widget-border, var(--vscode-panel-border));
      border-radius: 999px;
      background: color-mix(in srgb, var(--vscode-editor-background) 88%, transparent);
      box-shadow: 0 10px 30px rgba(0, 0, 0, 0.12);
    }

    .toolbar-field {
      display: inline-grid;
      gap: 2px;
      align-items: center;
      padding: 0 6px;
    }

    .toolbar-field span {
      font-size: 10px;
      font-weight: 600;
      color: var(--vscode-descriptionForeground);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .toolbar-field select {
      min-width: 96px;
      border: none;
      background: transparent;
      color: var(--vscode-editor-foreground);
      padding: 2px 0;
      font-size: 12px;
      outline: none;
    }

    .toolbar-field select:disabled {
      opacity: 0.55;
      cursor: default;
    }

    .icon-button {
      width: 30px;
      height: 30px;
      display: inline-grid;
      place-items: center;
      padding: 0;
      border: 0;
      border-radius: 999px;
      color: var(--vscode-editor-foreground);
      background: transparent;
      cursor: pointer;
      transition: background-color 120ms ease, color 120ms ease, transform 120ms ease;
    }

    .icon-button:hover {
      background: color-mix(in srgb, var(--vscode-button-background) 22%, transparent);
    }

    .icon-button:active {
      transform: translateY(1px);
    }

    .icon-button:focus-visible {
      outline: 1px solid var(--vscode-focusBorder);
      outline-offset: 2px;
    }

    .icon-button:disabled {
      opacity: 0.5;
      cursor: default;
      transform: none;
    }

    .icon-button svg {
      width: 16px;
      height: 16px;
      fill: currentColor;
    }

    .zoom-value {
      min-width: 48px;
      padding: 0 8px 0 4px;
      text-align: right;
      font-size: 12px;
      font-variant-numeric: tabular-nums;
      color: var(--vscode-descriptionForeground);
    }

    #content {
      min-height: 0;
      min-width: 0;
      overflow: auto;
      box-sizing: border-box;
      padding: var(--axsg-stage-padding);
      background:
        linear-gradient(rgba(127, 127, 127, 0.08) 1px, transparent 1px),
        linear-gradient(90deg, rgba(127, 127, 127, 0.08) 1px, transparent 1px),
        linear-gradient(rgba(127, 127, 127, 0.03) 1px, transparent 1px),
        linear-gradient(90deg, rgba(127, 127, 127, 0.03) 1px, transparent 1px),
        linear-gradient(180deg, rgba(255, 255, 255, 0.03), rgba(0, 0, 0, 0.03));
      background-size: 96px 96px, 96px 96px, 24px 24px, 24px 24px, auto;
      background-position: -1px -1px, -1px -1px, -1px -1px, -1px -1px, 0 0;
    }

    .preview-stage {
      min-width: 100%;
      min-height: 100%;
      display: grid;
      place-items: center;
      box-sizing: border-box;
    }

    .preview-host {
      width: fit-content;
      height: fit-content;
    }

    .preview-surface {
      position: relative;
      display: inline-block;
      border: 1px solid rgba(127, 127, 127, 0.18);
      border-radius: var(--axsg-surface-radius);
      overflow: hidden;
      background: #ffffff;
      box-shadow:
        0 22px 48px rgba(0, 0, 0, 0.18),
        0 6px 16px rgba(0, 0, 0, 0.12);
    }

    .preview-surface-content {
      position: relative;
      z-index: 1;
      width: 100%;
      height: 100%;
    }

    .preview-overlay-layer {
      position: absolute;
      inset: 0;
      z-index: 5;
      pointer-events: none;
    }

    .preview-overlay-layer.design-active {
      pointer-events: auto;
      cursor: crosshair;
    }

    .preview-overlay-layer.toolbox-drop-active {
      pointer-events: auto;
      cursor: copy;
    }

    .preview-surface.toolbox-drop-active {
      box-shadow:
        0 22px 48px rgba(0, 0, 0, 0.18),
        0 6px 16px rgba(0, 0, 0, 0.12),
        0 0 0 2px rgba(11, 118, 209, 0.22);
    }

    .preview-overlay-box {
      position: absolute;
      box-sizing: border-box;
      min-width: 2px;
      min-height: 2px;
      border: 2px solid transparent;
      box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.45);
    }

    .preview-overlay-box.selected {
      border-color: var(--axsg-overlay-selected);
      background: rgba(11, 118, 209, 0.08);
    }

    .preview-overlay-box.hover {
      border-color: var(--axsg-overlay-hover);
      background: rgba(255, 140, 0, 0.08);
      border-style: dashed;
    }

    .preview-overlay-label {
      position: absolute;
      left: -2px;
      top: -24px;
      max-width: 280px;
      padding: 3px 8px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 600;
      line-height: 1.2;
      color: white;
      background: rgba(17, 24, 39, 0.92);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      box-shadow: 0 8px 18px rgba(0, 0, 0, 0.18);
    }

    .preview-canvas {
      display: block;
      margin: 0;
      outline: none;
      image-rendering: pixelated;
    }

    iframe {
      display: block;
      border: 0;
      background: white;
    }

    .placeholder {
      display: grid;
      place-items: center;
      min-height: 100%;
      color: var(--vscode-descriptionForeground);
      font-size: 13px;
      text-align: center;
    }

    .placeholder-card {
      max-width: 320px;
      padding: 20px 22px;
      border: 1px solid rgba(127, 127, 127, 0.14);
      border-radius: 16px;
      background: color-mix(in srgb, var(--vscode-editorWidget-background) 94%, transparent);
      box-shadow: 0 14px 34px rgba(0, 0, 0, 0.12);
    }

    @media (max-width: 1040px) {
      .toolbar {
        grid-template-columns: 1fr;
        padding: 8px 12px;
        min-height: auto;
      }

      .toolbar-meta {
        justify-self: start;
      }
    }

    @media (max-width: 720px) {
      #content {
        padding: 16px;
      }
    }
  </style>
</head>
<body>
  <div class="shell">
    <div class="toolbar">
      <div class="toolbar-copy">
        <div class="toolbar-label">AXSG Preview</div>
        <div class="status" id="status">${statusText}</div>
      </div>
      <div class="toolbar-meta">
        <div class="toolbar-controls" aria-label="AXSG design controls">
          <label class="toolbar-field">
            <span>Mode</span>
            <select id="workspace-mode" aria-label="AXSG design mode">
              <option value="Interactive">Interactive</option>
              <option value="Design">Design</option>
              <option value="Agent">Agent</option>
            </select>
          </label>
          <label class="toolbar-field">
            <span>Tree</span>
            <select id="hit-test-mode" aria-label="AXSG hit test mode">
              <option value="Logical">Logical</option>
              <option value="Visual">Visual</option>
            </select>
          </label>
        </div>
        <div class="toolbar-actions" aria-label="Preview zoom controls">
          <button class="icon-button" id="zoom-out" type="button" title="Zoom out" aria-label="Zoom out">
            <svg viewBox="0 0 20 20" aria-hidden="true"><path d="${zoomOutIconPath}"></path></svg>
          </button>
          <button class="icon-button" id="zoom-reset" type="button" title="Reset zoom to 100%" aria-label="Reset zoom to 100%">
            <svg viewBox="0 0 20 20" aria-hidden="true"><path d="${zoomResetIconPath}"></path></svg>
          </button>
          <button class="icon-button" id="zoom-in" type="button" title="Zoom in" aria-label="Zoom in">
            <svg viewBox="0 0 20 20" aria-hidden="true"><path d="${zoomInIconPath}"></path></svg>
          </button>
          <div class="zoom-value" id="zoom-value">100%</div>
        </div>
      </div>
    </div>
    <div id="content">${iframeUrl
      ? `<div class="preview-stage" id="preview-stage"><div class="preview-host" id="preview-host"><div class="preview-surface" id="preview-surface"><div class="preview-surface-content" id="preview-surface-content"><iframe id="preview" src="${iframeUrl}" sandbox="allow-same-origin allow-scripts allow-forms allow-pointer-lock"></iframe></div><div class="preview-overlay-layer" id="preview-overlay-layer"></div></div></div></div>`
      : `<div class="preview-stage" id="preview-stage"><div class="placeholder"><div class="placeholder-card">Preview is starting...</div></div></div>`}</div>
  </div>
  <script>
    ${calculatePreviewSurfaceBounds.toString()}
    ${clampPreviewZoom.toString()}
    ${stepPreviewZoom.toString()}
    ${formatPreviewZoomLabel.toString()}
    ${normalizePreviewRenderScale.toString()}
    ${mapPreviewClientPointToDesignPoint.toString()}
    ${mapPreviewClientPointToRemotePoint.toString()}
    ${projectPreviewOverlayBounds.toString()}
    ${getPreviewKeyboardModifiers.toString()}
    ${getPreviewKeyboardText.toString()}
    ${createPreviewKeyInputPayload.toString()}
    ${createPreviewTextInputPayload.toString()}
    ${createPreviewKeyboardInputPayloads.toString()}
    const TOOLBOX_ITEM_MIME = ${JSON.stringify(DESIGN_TOOLBOX_ITEM_MIME)};
    const TOOLBOX_ITEM_TEXT_PREFIX = ${JSON.stringify(DESIGN_TOOLBOX_TEXT_PREFIX)};
    const content = document.getElementById('content');
    const status = document.getElementById('status');
    const workspaceModeSelect = document.getElementById('workspace-mode');
    const hitTestModeSelect = document.getElementById('hit-test-mode');
    const zoomOutButton = document.getElementById('zoom-out');
    const zoomResetButton = document.getElementById('zoom-reset');
    const zoomInButton = document.getElementById('zoom-in');
    const zoomValue = document.getElementById('zoom-value');
    const vscodeApi = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
    const persistedState = vscodeApi && typeof vscodeApi.getState === 'function'
      ? vscodeApi.getState()
      : null;
    let previewSocket = null;
    let previewSocketKey = '';
    let nextFrame = null;
    let activePreviewRenderScale = 1;
    let pendingViewportReport = 0;
    let pendingHoverDispatch = 0;
    let pendingHoverPoint = null;
    let pendingToolboxDragClear = 0;
    let displayScaleWatcher = null;
    let currentDesignState = null;
    let currentToolboxDragItem = null;
    let toolboxDragActive = false;
    let currentZoom = clampPreviewZoom(persistedState && persistedState.zoom, 1);

    function getViewportScale() {
      const scale = Number(window.devicePixelRatio || 1);
      if (!Number.isFinite(scale) || scale <= 0) {
        return 1;
      }

      return scale;
    }

    function persistViewState() {
      if (!vscodeApi || typeof vscodeApi.setState !== 'function') {
        return;
      }

      vscodeApi.setState({ zoom: currentZoom });
    }

    function reportViewportSize() {
      if (!vscodeApi) {
        return;
      }

      const bounds = getPreviewSurfaceBounds();
      const width = Math.max(1, Math.round(bounds.width));
      const height = Math.max(1, Math.round(bounds.height));
      vscodeApi.postMessage({
        type: 'viewportSize',
        width,
        height,
        devicePixelRatio: getViewportScale()
      });
    }

    function updateStatusText(text) {
      const nextText = text || 'Preview ready.';
      status.textContent = nextText;
      status.title = nextText;
    }

    function updateZoomUi() {
      const zoomText = formatPreviewZoomLabel(currentZoom);
      zoomValue.textContent = zoomText;
      zoomValue.title = 'Preview zoom ' + zoomText;
      zoomOutButton.disabled = currentZoom <= 0.25;
      zoomResetButton.disabled = Math.abs(currentZoom - 1) < 0.001;
      zoomInButton.disabled = currentZoom >= 3;
    }

    function getPreviewSurfaceBounds() {
      const bounds = content.getBoundingClientRect();
      const computedStyle = typeof window.getComputedStyle === 'function'
        ? window.getComputedStyle(content)
        : null;
      const horizontalPadding = computedStyle
        ? (Number.parseFloat(computedStyle.paddingLeft || '0') + Number.parseFloat(computedStyle.paddingRight || '0'))
        : 48;
      const verticalPadding = computedStyle
        ? (Number.parseFloat(computedStyle.paddingTop || '0') + Number.parseFloat(computedStyle.paddingBottom || '0'))
        : 48;
      return calculatePreviewSurfaceBounds(
        bounds.width || 0,
        bounds.height || 0,
        horizontalPadding,
        verticalPadding);
    }

    function isInteractiveWorkspaceMode() {
      return !currentDesignState ||
        !currentDesignState.available ||
        String(currentDesignState.workspaceMode || 'Interactive') === 'Interactive';
    }

    function getOverlaySnapshot() {
      return currentDesignState && currentDesignState.overlay && typeof currentDesignState.overlay === 'object'
        ? currentDesignState.overlay
        : null;
    }

    function canAcceptToolboxDrop() {
      return !!(currentDesignState && currentDesignState.available && currentToolboxDragItem);
    }

    function normalizeToolboxDragItem(candidate) {
      if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate.items)) {
        return null;
      }

      const name = typeof candidate.name === 'string' && candidate.name.trim().length > 0
        ? candidate.name.trim()
        : '';
      const displayName = typeof candidate.displayName === 'string' && candidate.displayName.trim().length > 0
        ? candidate.displayName.trim()
        : name;
      const xamlSnippet = typeof candidate.xamlSnippet === 'string' && candidate.xamlSnippet.trim().length > 0
        ? candidate.xamlSnippet
        : '';
      if (!name && !displayName) {
        return null;
      }

      return {
        name: name || displayName,
        displayName: displayName || name,
        category: typeof candidate.category === 'string' ? candidate.category : '',
        xamlSnippet,
        isProjectControl: !!candidate.isProjectControl,
        tags: Array.isArray(candidate.tags)
          ? candidate.tags.filter(tag => typeof tag === 'string' && tag.trim().length > 0)
          : []
      };
    }

    function tryParseToolboxDragItem(value) {
      if (typeof value !== 'string' || value.length === 0) {
        return null;
      }

      let payload = value;
      if (payload.startsWith(TOOLBOX_ITEM_TEXT_PREFIX)) {
        payload = payload.slice(TOOLBOX_ITEM_TEXT_PREFIX.length);
      }

      try {
        return normalizeToolboxDragItem(JSON.parse(payload));
      } catch {
        return null;
      }
    }

    function readToolboxDragItem(dataTransfer) {
      if (!dataTransfer || typeof dataTransfer.getData !== 'function') {
        return null;
      }

      const customPayload = tryParseToolboxDragItem(dataTransfer.getData(TOOLBOX_ITEM_MIME));
      if (customPayload) {
        return customPayload;
      }

      return tryParseToolboxDragItem(dataTransfer.getData('text/plain'));
    }

    function cancelToolboxDragClear() {
      if (!pendingToolboxDragClear) {
        return;
      }

      window.clearTimeout(pendingToolboxDragClear);
      pendingToolboxDragClear = 0;
    }

    function clearToolboxDragState(resetHover = true) {
      cancelToolboxDragClear();
      currentToolboxDragItem = null;
      if (!toolboxDragActive) {
        return;
      }

      toolboxDragActive = false;
      updateDesignControls();
      if (resetHover) {
        scheduleHoverDispatch(-1, -1);
      }
    }

    function scheduleToolboxDragClear() {
      cancelToolboxDragClear();
      pendingToolboxDragClear = window.setTimeout(() => {
        pendingToolboxDragClear = 0;
        clearToolboxDragState();
      }, 120);
    }

    function updateToolboxDragState(dataTransfer) {
      const toolboxItem = readToolboxDragItem(dataTransfer);
      if (!toolboxItem || !currentDesignState || !currentDesignState.available) {
        scheduleToolboxDragClear();
        return null;
      }

      cancelToolboxDragClear();
      currentToolboxDragItem = toolboxItem;
      if (!toolboxDragActive) {
        toolboxDragActive = true;
        updateDesignControls();
      }

      return toolboxItem;
    }

    function applyCanvasZoom() {
      const canvas = document.getElementById('preview-canvas');
      if (!canvas || !nextFrame) {
        return;
      }

      const scale = getViewportScale();
      canvas.style.width = ((nextFrame.width / scale) * currentZoom) + 'px';
      canvas.style.height = ((nextFrame.height / scale) * currentZoom) + 'px';
    }

    function applyIframeZoom(host, surface, iframe) {
      if (!host || !surface || !iframe) {
        return;
      }

      const bounds = getPreviewSurfaceBounds();
      surface.style.width = bounds.width + 'px';
      surface.style.height = bounds.height + 'px';
      surface.style.transform = 'scale(' + currentZoom + ')';
      surface.style.transformOrigin = 'top center';
      host.style.width = (bounds.width * currentZoom) + 'px';
      host.style.height = (bounds.height * currentZoom) + 'px';
      iframe.style.width = bounds.width + 'px';
      iframe.style.height = bounds.height + 'px';
    }

    function applyCurrentZoom() {
      const canvas = document.getElementById('preview-canvas');
      if (canvas) {
        applyCanvasZoom();
      } else {
        const host = document.getElementById('preview-host');
        const surface = document.getElementById('preview-surface');
        const iframe = document.getElementById('preview');
        if (host && surface && iframe) {
          applyIframeZoom(host, surface, iframe);
        }
      }

      renderDesignOverlay();
    }

    function setZoom(nextZoom) {
      const normalizedZoom = clampPreviewZoom(nextZoom, currentZoom);
      if (Math.abs(normalizedZoom - currentZoom) < 0.001) {
        updateZoomUi();
        return;
      }

      currentZoom = normalizedZoom;
      persistViewState();
      updateZoomUi();
      applyCurrentZoom();
    }

    function scheduleViewportSizeReport() {
      if (pendingViewportReport) {
        return;
      }

      pendingViewportReport = window.requestAnimationFrame(() => {
        pendingViewportReport = 0;
        reportViewportSize();
      });
    }

    function scheduleHoverDispatch(x, y) {
      pendingHoverPoint = { x, y };
      if (pendingHoverDispatch) {
        return;
      }

      pendingHoverDispatch = window.requestAnimationFrame(() => {
        pendingHoverDispatch = 0;
        if (!vscodeApi || !pendingHoverPoint) {
          return;
        }

        vscodeApi.postMessage({
          type: 'designHoverAtPoint',
          x: pendingHoverPoint.x,
          y: pendingHoverPoint.y
        });
        pendingHoverPoint = null;
      });
    }

    function logTransport(message) {
      if (!message || !vscodeApi) {
        return;
      }

      vscodeApi.postMessage({
        type: 'transportLog',
        message
      });
    }

    function watchDisplayScaleChanges() {
      if (typeof window.matchMedia !== 'function') {
        return;
      }

      if (displayScaleWatcher) {
        const watcher = displayScaleWatcher;
        if (typeof watcher.removeEventListener === 'function') {
          watcher.removeEventListener('change', watcher._axsgHandler);
        } else if (typeof watcher.removeListener === 'function') {
          watcher.removeListener(watcher._axsgHandler);
        }
      }

      const watcher = window.matchMedia('(resolution: ' + getViewportScale() + 'dppx)');
      const handleChange = () => {
        watchDisplayScaleChanges();
        scheduleViewportSizeReport();
      };
      watcher._axsgHandler = handleChange;
      if (typeof watcher.addEventListener === 'function') {
        watcher.addEventListener('change', handleChange);
      } else if (typeof watcher.addListener === 'function') {
        watcher.addListener(handleChange);
      }

      displayScaleWatcher = watcher;
    }

    function disposePreviewSocket() {
      previewSocketKey = '';
      nextFrame = null;
      activePreviewRenderScale = 1;
      if (!previewSocket) {
        return;
      }

      const socket = previewSocket;
      previewSocket = null;
      socket.onopen = null;
      socket.onmessage = null;
      socket.onerror = null;
      socket.onclose = null;
      try {
        socket.close();
      } catch {
        // Best effort cleanup.
      }
    }

    function renderPlaceholder(text, disposeTransport = true) {
      clearToolboxDragState();
      if (disposeTransport) {
        disposePreviewSocket();
      }

      content.innerHTML = '';
      const stage = document.createElement('div');
      stage.id = 'preview-stage';
      stage.className = 'preview-stage';
      const placeholder = document.createElement('div');
      placeholder.className = 'placeholder';
      const placeholderCard = document.createElement('div');
      placeholderCard.className = 'placeholder-card';
      placeholderCard.textContent = text || 'Preview is starting...';
      placeholder.appendChild(placeholderCard);
      stage.appendChild(placeholder);
      content.appendChild(stage);
    }

    function ensurePreviewStage() {
      let stage = document.getElementById('preview-stage');
      if (!stage) {
        content.innerHTML = '';
        stage = document.createElement('div');
        stage.id = 'preview-stage';
        stage.className = 'preview-stage';
        content.appendChild(stage);
      }

      return stage;
    }

    function wireDesignOverlayInput(layer) {
      if (!layer || layer._axsgDesignWired) {
        return;
      }

      layer._axsgDesignWired = true;
      layer.addEventListener('pointermove', event => {
        if (!currentDesignState || !currentDesignState.available || isInteractiveWorkspaceMode()) {
          return;
        }

        const rect = layer.getBoundingClientRect();
        const point = mapPreviewClientPointToDesignPoint(
          event.clientX - rect.left,
          event.clientY - rect.top,
          currentZoom);
        scheduleHoverDispatch(point.x, point.y);
      });
      layer.addEventListener('pointerleave', () => {
        if (!currentDesignState || !currentDesignState.available || isInteractiveWorkspaceMode()) {
          return;
        }

        scheduleHoverDispatch(-1, -1);
      });
      layer.addEventListener('pointerdown', event => {
        if (!currentDesignState || !currentDesignState.available || isInteractiveWorkspaceMode() || !vscodeApi) {
          return;
        }

        const rect = layer.getBoundingClientRect();
        const point = mapPreviewClientPointToDesignPoint(
          event.clientX - rect.left,
          event.clientY - rect.top,
          currentZoom);
        event.preventDefault();
        event.stopPropagation();
        vscodeApi.postMessage({
          type: 'designSelectAtPoint',
          x: point.x,
          y: point.y
        });
      });
      layer.addEventListener('contextmenu', event => {
        if (!currentDesignState || !currentDesignState.available || isInteractiveWorkspaceMode()) {
          return;
        }

        event.preventDefault();
      });
    }

    function wireToolboxDropInput(layer) {
      if (!layer || layer._axsgToolboxDropWired) {
        return;
      }

      layer._axsgToolboxDropWired = true;
      layer.addEventListener('dragenter', event => {
        const toolboxItem = updateToolboxDragState(event.dataTransfer);
        if (!toolboxItem) {
          return;
        }

        event.preventDefault();
      });
      layer.addEventListener('dragover', event => {
        const toolboxItem = updateToolboxDragState(event.dataTransfer);
        if (!toolboxItem || !vscodeApi || !canAcceptToolboxDrop()) {
          return;
        }

        const rect = layer.getBoundingClientRect();
        const point = mapPreviewClientPointToDesignPoint(
          event.clientX - rect.left,
          event.clientY - rect.top,
          currentZoom);
        scheduleHoverDispatch(point.x, point.y);
        event.preventDefault();
        if (event.dataTransfer) {
          event.dataTransfer.dropEffect = 'copy';
        }
      });
      layer.addEventListener('dragleave', () => {
        scheduleToolboxDragClear();
      });
      layer.addEventListener('drop', event => {
        const toolboxItem = updateToolboxDragState(event.dataTransfer);
        if (!toolboxItem || !vscodeApi || !canAcceptToolboxDrop()) {
          clearToolboxDragState();
          return;
        }

        const rect = layer.getBoundingClientRect();
        const point = mapPreviewClientPointToDesignPoint(
          event.clientX - rect.left,
          event.clientY - rect.top,
          currentZoom);
        event.preventDefault();
        event.stopPropagation();
        vscodeApi.postMessage({
          type: 'designDropToolboxItem',
          toolboxItem,
          x: point.x,
          y: point.y
        });
        clearToolboxDragState(false);
      });
    }

    function ensurePreviewSurface() {
      const stage = ensurePreviewStage();
      let host = document.getElementById('preview-host');
      let surface = document.getElementById('preview-surface');
      if (!host || !surface) {
        host = document.createElement('div');
        host.id = 'preview-host';
        host.className = 'preview-host';
        surface = document.createElement('div');
        surface.id = 'preview-surface';
        surface.className = 'preview-surface';
        host.appendChild(surface);
        stage.innerHTML = '';
        stage.appendChild(host);
      }

      let surfaceContent = document.getElementById('preview-surface-content');
      if (!surfaceContent || surfaceContent.parentElement !== surface) {
        surface.innerHTML = '';
        surfaceContent = document.createElement('div');
        surfaceContent.id = 'preview-surface-content';
        surfaceContent.className = 'preview-surface-content';
        surface.appendChild(surfaceContent);
      }

      let overlayLayer = document.getElementById('preview-overlay-layer');
      if (!overlayLayer || overlayLayer.parentElement !== surface) {
        overlayLayer = document.createElement('div');
        overlayLayer.id = 'preview-overlay-layer';
        overlayLayer.className = 'preview-overlay-layer';
        surface.appendChild(overlayLayer);
      }

      wireDesignOverlayInput(overlayLayer);
      wireToolboxDropInput(overlayLayer);

      host.style.width = '';
      host.style.height = '';
      surface.style.width = '';
      surface.style.height = '';
      surface.style.transform = '';
      surface.style.transformOrigin = '';
      return { host, surface, surfaceContent, overlayLayer };
    }

    function ensureCanvas() {
      let canvas = document.getElementById('preview-canvas');
      if (canvas) {
        return canvas;
      }

      const { surfaceContent } = ensurePreviewSurface();
      surfaceContent.innerHTML = '';
      canvas = document.createElement('canvas');
      canvas.id = 'preview-canvas';
      canvas.className = 'preview-canvas';
      canvas.tabIndex = 0;
      wireCanvasInput(canvas);
      surfaceContent.appendChild(canvas);
      return canvas;
    }

    function renderIframe(previewUrl) {
      disposePreviewSocket();
      const { host, surface, surfaceContent } = ensurePreviewSurface();
      let iframe = document.getElementById('preview');
      if (!iframe || iframe.parentElement !== surfaceContent) {
        surfaceContent.innerHTML = '';
        iframe = document.createElement('iframe');
        iframe.id = 'preview';
        iframe.setAttribute('sandbox', 'allow-same-origin allow-scripts allow-forms allow-pointer-lock');
        surfaceContent.appendChild(iframe);
      }

      if (iframe.src !== previewUrl) {
        iframe.src = previewUrl;
      }

      applyIframeZoom(host, surface, iframe);
      updateDesignControls();
      renderDesignOverlay();
      scheduleViewportSizeReport();
    }

    function getCanvasPoint(event, canvas) {
      const rect = canvas.getBoundingClientRect();
      return mapPreviewClientPointToRemotePoint(
        event.clientX - rect.left,
        event.clientY - rect.top,
        getViewportScale(),
        activePreviewRenderScale,
        currentZoom);
    }

    function getMouseButton(event) {
      if (event.button === 0) {
        return 1;
      }
      if (event.button === 1) {
        return 3;
      }
      if (event.button === 2) {
        return 2;
      }

      return 0;
    }

    function getModifiers(event) {
      const modifiers = [];
      if (event.altKey) {
        modifiers.push(0);
      }
      if (event.ctrlKey) {
        modifiers.push(1);
      }
      if (event.shiftKey) {
        modifiers.push(2);
      }
      if (event.metaKey) {
        modifiers.push(3);
      }
      if (event.buttons !== 0) {
        if ((event.buttons & 1) !== 0) {
          modifiers.push(4);
        }
        if ((event.buttons & 2) !== 0) {
          modifiers.push(5);
        }
        if ((event.buttons & 4) !== 0) {
          modifiers.push(6);
        }
      }

      return modifiers.join(',');
    }

    function sendPointerMessage(kind, event, includeButton) {
      if (!previewSocket || previewSocket.readyState !== WebSocket.OPEN || !isInteractiveWorkspaceMode()) {
        return;
      }

      const canvas = document.getElementById('preview-canvas');
      if (!canvas) {
        return;
      }

      const point = getCanvasPoint(event, canvas);
      const modifiers = getModifiers(event);
      const parts = [kind, modifiers, point.x, point.y];
      if (includeButton) {
        parts.push(getMouseButton(event));
      }

      previewSocket.send(parts.join(':'));
    }

    function sendWheelMessage(event) {
      if (!previewSocket || previewSocket.readyState !== WebSocket.OPEN || !isInteractiveWorkspaceMode()) {
        return;
      }

      const canvas = document.getElementById('preview-canvas');
      if (!canvas) {
        return;
      }

      const point = getCanvasPoint(event, canvas);
      previewSocket.send([
        'scroll',
        getModifiers(event),
        point.x,
        point.y,
        -event.deltaX,
        -event.deltaY
      ].join(':'));
    }

    function postPreviewInputPayloads(payloads) {
      if (!vscodeApi || !Array.isArray(payloads) || payloads.length === 0 || !isInteractiveWorkspaceMode()) {
        return;
      }

      vscodeApi.postMessage({
        type: 'previewInput',
        inputs: payloads
      });
    }

    function wireCanvasInput(canvas) {
      canvas.addEventListener('pointerdown', event => {
        if (!isInteractiveWorkspaceMode()) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }

        canvas.focus();
        sendPointerMessage('pointer-pressed', event, true);
      });
      canvas.addEventListener('pointerup', event => {
        sendPointerMessage('pointer-released', event, true);
      });
      canvas.addEventListener('pointermove', event => {
        sendPointerMessage('pointer-moved', event, false);
      });
      canvas.addEventListener('wheel', event => {
        if (!isInteractiveWorkspaceMode()) {
          return;
        }

        event.preventDefault();
        sendWheelMessage(event);
      }, { passive: false });
      canvas.addEventListener('keydown', event => {
        const payloads = createPreviewKeyboardInputPayloads(event, true);
        if (payloads.length === 0) {
          return;
        }

        event.preventDefault();
        event.stopPropagation();
        postPreviewInputPayloads(payloads);
      });
      canvas.addEventListener('keyup', event => {
        const payloads = createPreviewKeyboardInputPayloads(event, false);
        if (payloads.length === 0) {
          return;
        }

        event.preventDefault();
        event.stopPropagation();
        postPreviewInputPayloads(payloads);
      });
      canvas.addEventListener('compositionend', event => {
        const payload = createPreviewTextInputPayload(event);
        if (!payload) {
          return;
        }

        event.preventDefault();
        event.stopPropagation();
        postPreviewInputPayloads([payload]);
      });
      canvas.addEventListener('contextmenu', event => event.preventDefault());
    }

    function updateDesignControls() {
      const overlayLayer = document.getElementById('preview-overlay-layer');
      const surface = document.getElementById('preview-surface');
      const iframe = document.getElementById('preview');
      const canvas = document.getElementById('preview-canvas');
      const available = !!(currentDesignState && currentDesignState.available);
      const interactive = isInteractiveWorkspaceMode();
      const dragActive = available && toolboxDragActive;

      workspaceModeSelect.disabled = !available;
      hitTestModeSelect.disabled = !available;
      workspaceModeSelect.value = available && currentDesignState.workspaceMode
        ? currentDesignState.workspaceMode
        : 'Interactive';
      hitTestModeSelect.value = available && currentDesignState.hitTestMode
        ? currentDesignState.hitTestMode
        : 'Logical';

      if (overlayLayer) {
        overlayLayer.classList.toggle('design-active', available && !interactive);
        overlayLayer.classList.toggle('toolbox-drop-active', dragActive);
      }

      if (surface) {
        surface.classList.toggle('toolbox-drop-active', dragActive);
      }

      if (iframe) {
        iframe.style.pointerEvents = available && (!interactive || dragActive) ? 'none' : 'auto';
      }

      if (canvas) {
        canvas.style.pointerEvents = available && (!interactive || dragActive) ? 'none' : 'auto';
      }
    }

    function renderDesignOverlay() {
      const overlayLayer = document.getElementById('preview-overlay-layer');
      const surface = document.getElementById('preview-surface');
      const overlay = getOverlaySnapshot();
      if (!overlayLayer || !surface) {
        return;
      }

      overlayLayer.innerHTML = '';
      updateDesignControls();
      if (!overlay || (isInteractiveWorkspaceMode() && !toolboxDragActive)) {
        return;
      }

      const surfaceWidth = surface.clientWidth || 0;
      const surfaceHeight = surface.clientHeight || 0;
      const rootWidth = Number(overlay.rootWidth) > 0 ? Number(overlay.rootWidth) : surfaceWidth;
      const rootHeight = Number(overlay.rootHeight) > 0 ? Number(overlay.rootHeight) : surfaceHeight;
      const overlayItems = [];
      if (overlay.selected) {
        overlayItems.push({ kind: 'selected', item: overlay.selected });
      }
      if (overlay.hover && (!overlay.selected || overlay.hover.elementId !== overlay.selected.elementId)) {
        overlayItems.push({ kind: 'hover', item: overlay.hover });
      }

      for (const entry of overlayItems) {
        const projectedBounds = projectPreviewOverlayBounds(
          entry.item.bounds,
          rootWidth,
          rootHeight,
          surfaceWidth,
          surfaceHeight);
        if (!projectedBounds) {
          continue;
        }

        const box = document.createElement('div');
        box.className = 'preview-overlay-box ' + entry.kind;
        box.style.left = projectedBounds.left + 'px';
        box.style.top = projectedBounds.top + 'px';
        box.style.width = projectedBounds.width + 'px';
        box.style.height = projectedBounds.height + 'px';

        const label = document.createElement('div');
        label.className = 'preview-overlay-label';
        label.textContent = entry.item.displayLabel || (entry.item.element && (entry.item.element.displayName || entry.item.element.typeName)) || entry.kind;
        box.appendChild(label);
        overlayLayer.appendChild(box);
      }
    }

    function updateDesignState(nextDesignState) {
      currentDesignState = nextDesignState && typeof nextDesignState === 'object'
        ? nextDesignState
        : null;
      if (!currentDesignState || !currentDesignState.available) {
        clearToolboxDragState(false);
      }
      updateDesignControls();
      renderDesignOverlay();
    }

    function renderFrame(frameBuffer) {
      if (!nextFrame) {
        return;
      }

      const canvas = ensureCanvas();
      canvas.width = nextFrame.width;
      canvas.height = nextFrame.height;
      applyCanvasZoom();

      const context = canvas.getContext('2d');
      const imageData = new ImageData(new Uint8ClampedArray(frameBuffer), nextFrame.width, nextFrame.height);
      context.putImageData(imageData, 0, 0);
      if (previewSocket && previewSocket.readyState === WebSocket.OPEN) {
        previewSocket.send('frame-received:' + nextFrame.sequenceId);
      }

      renderDesignOverlay();
      scheduleViewportSizeReport();
    }

    function connectLoopbackPreview(loopbackPreview) {
      const connectionKey = loopbackPreview.webSocketUrl + '|' + loopbackPreview.securityCookie;
      if (previewSocket &&
          previewSocketKey === connectionKey &&
          (previewSocket.readyState === WebSocket.CONNECTING || previewSocket.readyState === WebSocket.OPEN)) {
        return;
      }

      disposePreviewSocket();
      renderPlaceholder('Connecting to preview transport...');
      previewSocketKey = connectionKey;

      let socket;
      try {
        socket = new WebSocket(loopbackPreview.webSocketUrl);
      } catch (error) {
        const message = error && error.message ? error.message : String(error);
        logTransport('failed to create preview websocket: ' + message);
        renderPlaceholder('Preview transport failed to initialize.');
        return;
      }

      previewSocket = socket;
      activePreviewRenderScale = normalizePreviewRenderScale(loopbackPreview.previewScale, getViewportScale());
      socket.binaryType = 'arraybuffer';
      socket.onopen = () => {
        if (previewSocket !== socket) {
          return;
        }

        logTransport('connected to ' + loopbackPreview.webSocketUrl);
        renderPlaceholder('Waiting for first preview frame...', false);
        socket.send(loopbackPreview.securityCookie);
      };
      socket.onmessage = event => {
        if (previewSocket !== socket) {
          return;
        }

        if (typeof event.data === 'string') {
          if (event.data.startsWith('frame:')) {
            const parts = event.data.split(':');
            nextFrame = {
              sequenceId: parts[1],
              width: Number.parseInt(parts[2], 10),
              height: Number.parseInt(parts[3], 10),
              dpiX: Number.parseFloat(parts[5]),
              dpiY: Number.parseFloat(parts[6])
            };
            activePreviewRenderScale = normalizePreviewRenderScale(nextFrame.dpiX, activePreviewRenderScale);
          }
          return;
        }

        if (event.data instanceof ArrayBuffer) {
          renderFrame(event.data);
        }
      };
      socket.onerror = () => {
        if (previewSocket !== socket) {
          return;
        }

        logTransport('preview websocket error for ' + loopbackPreview.webSocketUrl);
        renderPlaceholder('Preview transport failed to connect.');
      };
      socket.onclose = () => {
        if (previewSocket !== socket) {
          return;
        }

        logTransport('preview websocket closed for ' + loopbackPreview.webSocketUrl);
        previewSocket = null;
        previewSocketKey = '';
        if (!document.getElementById('preview-canvas')) {
          renderPlaceholder('Preview transport disconnected.');
        }
      };
    }

    function updatePreview(previewUrl, loopbackPreview, statusText, designState) {
      updateStatusText(statusText);
      updateDesignState(designState);

      if (loopbackPreview && loopbackPreview.webSocketUrl && loopbackPreview.securityCookie) {
        connectLoopbackPreview(loopbackPreview);
        return;
      }

      if (previewUrl) {
        renderIframe(String(previewUrl));
        return;
      }

      renderPlaceholder('Preview is starting...');
    }

    zoomOutButton.addEventListener('click', () => {
      setZoom(stepPreviewZoom(currentZoom, -1));
    });
    zoomResetButton.addEventListener('click', () => {
      setZoom(1);
    });
    zoomInButton.addEventListener('click', () => {
      setZoom(stepPreviewZoom(currentZoom, 1));
    });
    workspaceModeSelect.addEventListener('change', () => {
      if (!vscodeApi) {
        return;
      }

      vscodeApi.postMessage({
        type: 'designSetWorkspaceMode',
        mode: workspaceModeSelect.value
      });
    });
    hitTestModeSelect.addEventListener('change', () => {
      if (!vscodeApi) {
        return;
      }

      vscodeApi.postMessage({
        type: 'designSetHitTestMode',
        mode: hitTestModeSelect.value
      });
    });
    window.addEventListener('dragenter', event => {
      updateToolboxDragState(event.dataTransfer);
    }, true);
    window.addEventListener('dragover', event => {
      updateToolboxDragState(event.dataTransfer);
    }, true);
    window.addEventListener('drop', () => {
      clearToolboxDragState();
    }, true);
    window.addEventListener('dragend', () => {
      clearToolboxDragState();
    }, true);

    window.addEventListener('message', event => {
      const message = event.data || {};
      if (message.type === 'update') {
        updatePreview(message.previewUrl, message.loopbackPreview, message.status, message.designState);
      }
    });
    window.addEventListener('resize', () => {
      applyCurrentZoom();
      scheduleViewportSizeReport();
    });
    if (typeof ResizeObserver === 'function') {
      const observer = new ResizeObserver(() => {
        applyCurrentZoom();
        scheduleViewportSizeReport();
      });
      observer.observe(content);
    }
    updateStatusText(status.textContent);
    updateZoomUi();
    updateDesignControls();
    applyCurrentZoom();
    watchDisplayScaleChanges();
    scheduleViewportSizeReport();
  </script>
</body>
</html>`;
}

function createPreviewWebviewOptions(portMapping = []) {
  return {
    enableScripts: true,
    portMapping
  };
}

function createDeferred() {
  const deferred = {};
  deferred.promise = new Promise(resolve => {
    deferred.resolve = resolve;
  });
  return deferred;
}

async function execFileAsync(command, args, options) {
  return new Promise((resolve, reject) => {
    cp.execFile(command, args, {
      cwd: options.cwd,
      maxBuffer: 10 * 1024 * 1024
    }, (error, stdout, stderr) => {
      if (error) {
        reject(new Error(stderr && stderr.trim()
          ? stderr.trim()
          : error.message));
        return;
      }

      resolve(stdout);
    });
  });
}

async function runDotNetCommand(command, args, cwd, outputChannel) {
  return new Promise((resolve, reject) => {
    outputChannel.appendLine(`[preview] ${command} ${args.join(' ')}`);
    const child = cp.spawn(command, args, {
      cwd,
      env: process.env,
      stdio: ['ignore', 'pipe', 'pipe']
    });

    let stdoutText = '';
    let stderrText = '';
    child.stdout.on('data', chunk => {
      const text = String(chunk).trimEnd();
      if (text) {
        stdoutText = stdoutText
          ? `${stdoutText}\n${text}`
          : text;
        outputChannel.appendLine(text);
      }
    });
    child.stderr.on('data', chunk => {
      const text = String(chunk).trimEnd();
      if (text) {
        stderrText = stderrText
          ? `${stderrText}\n${text}`
          : text;
        outputChannel.appendLine(text);
      }
    });
    child.on('error', error => {
      reject(error);
    });
    child.on('exit', exitCode => {
      if (exitCode === 0) {
        resolve();
        return;
      }

      reject(new Error(createCommandFailureMessage(command, args, stdoutText, stderrText, exitCode)));
    });
  });
}

async function runDotNetBuildCommand(command, projectPath, targetFramework, cwd, outputChannel) {
  const useNoRestore = shouldUseNoRestoreBuild(projectPath);
  const args = buildArguments(projectPath, targetFramework, { skipRestore: useNoRestore });

  try {
    await runDotNetCommand(command, args, cwd, outputChannel);
  } catch (error) {
    if (!useNoRestore || !isRestoreRequiredBuildError(error)) {
      throw error;
    }

    outputChannel.appendLine(`[preview] retrying ${path.basename(projectPath)} build with restore enabled`);
    await runDotNetCommand(command, buildArguments(projectPath, targetFramework), cwd, outputChannel);
  }
}

function isRestoreRequiredBuildError(error) {
  const message = error instanceof Error ? error.message : String(error);
  return message.includes('project.assets.json') ||
    message.includes('assets file') ||
    message.includes('Run a NuGet package restore') ||
    message.includes('NETSDK1004');
}

function resolveBundledPreviewHostPath(extensionPath) {
  return path.join(extensionPath, 'preview-host', PREVIEW_HOST_ASSEMBLY_NAME);
}

function resolveBundledSourceGeneratedDesignerHostPath(extensionPath) {
  return path.join(extensionPath, 'designer-host', SOURCE_GENERATED_DESIGNER_HOST_ASSEMBLY_NAME);
}

function buildStartAttempts(extensionPath, launchInfo) {
  const attempts = [];
  const requestedMode = launchInfo.previewPlan && launchInfo.previewPlan.requestedMode
    ? launchInfo.previewPlan.requestedMode
    : PREVIEW_COMPILER_MODE_AUTO;
  const preferredMode = launchInfo.previewPlan && launchInfo.previewPlan.preferredMode
    ? launchInfo.previewPlan.preferredMode
    : PREVIEW_COMPILER_MODE_AVALONIA;

  const designerHostPath = resolveBundledSourceGeneratedDesignerHostPath(extensionPath);
  const hasBundledDesignerHost = fs.existsSync(designerHostPath);
  const startPlan = createPreviewStartPlan({
    requestedMode,
    preferredMode,
    hasBundledDesignerHost,
    hasAvaloniaPreviewer: Boolean(launchInfo.hostProject.previewerToolPath)
  });

  if (startPlan.requiresBundledDesignerHost) {
    throw new Error(`Bundled designer host not found at ${designerHostPath}. Run the extension packaging step first.`);
  }

  if (startPlan.requiresAvaloniaPreviewer) {
    throw new Error('Avalonia previewer host path is unavailable for the selected project.');
  }

  for (const mode of startPlan.modes) {
    if (mode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
      attempts.push({
        label: 'AXSG source-generated',
        mode,
        previewerToolPath: designerHostPath
      });
      continue;
    }

    if (mode === PREVIEW_COMPILER_MODE_AVALONIA) {
      const previewerToolPaths = resolveAvaloniaPreviewerToolPaths(
        hasBundledDesignerHost,
        designerHostPath,
        launchInfo.hostProject.previewerToolPath);

      for (let index = 0; index < previewerToolPaths.length; index += 1) {
        attempts.push({
          label: index === 0
            ? 'Avalonia XamlX'
            : 'Avalonia XamlX (project host fallback)',
          mode,
          previewerToolPath: previewerToolPaths[index]
        });
      }
    }
  }

  return attempts;
}

function synchronizePreviewSourceAssemblyArtifacts(sourceAssemblyPath, previewAssemblyPath, outputChannel) {
  const normalizedSourceAssemblyPath = normalizeMaybeEmptyPath(sourceAssemblyPath);
  const normalizedPreviewAssemblyPath = normalizeMaybeEmptyPath(previewAssemblyPath);
  if (!normalizedSourceAssemblyPath ||
      !normalizedPreviewAssemblyPath ||
      samePath(normalizedSourceAssemblyPath, normalizedPreviewAssemblyPath) ||
      !fs.existsSync(normalizedSourceAssemblyPath)) {
    return;
  }

  for (const artifact of enumeratePreviewAssemblyArtifacts(normalizedSourceAssemblyPath, normalizedPreviewAssemblyPath)) {
    fs.mkdirSync(path.dirname(artifact.targetPath), { recursive: true });
    synchronizePreviewAssemblyArtifact(artifact.sourcePath, artifact.targetPath, outputChannel);
  }
}

function synchronizePreviewAssemblyArtifact(sourcePath, targetPath, outputChannel) {
  if (!shouldCopyPreviewAssemblyArtifact(sourcePath, targetPath)) {
    return;
  }

  fs.copyFileSync(sourcePath, targetPath);
  if (outputChannel) {
    outputChannel.appendLine(`[preview] synchronized ${path.basename(sourcePath)} into ${path.dirname(targetPath)}`);
  }
}

function shouldCopyPreviewAssemblyArtifact(sourcePath, targetPath) {
  if (!fs.existsSync(sourcePath)) {
    return false;
  }

  if (!fs.existsSync(targetPath)) {
    return true;
  }

  const sourceStat = fs.statSync(sourcePath);
  const targetStat = fs.statSync(targetPath);
  return sourceStat.size !== targetStat.size || sourceStat.mtimeMs > targetStat.mtimeMs;
}

function tryReadDocumentTextFromDisk(filePath) {
  if (!filePath || !fs.existsSync(filePath)) {
    return undefined;
  }

  try {
    return fs.readFileSync(filePath, 'utf8');
  } catch {
    return undefined;
  }
}

function getPreviewReadyStatus(fileName, compilerMode) {
  if (compilerMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
    return `Source-generated preview ready for ${fileName}. Live unsaved XAML updates are enabled; save to rebuild generated output.`;
  }

  return `Preview ready for ${fileName}.`;
}

function escapeHtml(text) {
  return String(text || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

module.exports = {
  AvaloniaPreviewController
};
