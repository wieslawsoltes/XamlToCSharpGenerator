const path = require('path');
const fs = require('fs');
const cp = require('child_process');
const http = require('http');
const https = require('https');
const readline = require('readline');
const { EventEmitter } = require('events');
const vscode = require('vscode');
const {
  buildArguments,
  createCommandFailureMessage,
  createPreviewBuildPlan,
  createPreviewStartPlan,
  extractPreviewSecurityCookie,
  hasPendingPreviewText,
  PREVIEW_COMPILER_MODE_AUTO,
  PREVIEW_COMPILER_MODE_AVALONIA,
  PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
  isUsablePreviewHostProjectInfo,
  isPreviewableProjectInfo,
  isUnderBuildOutput,
  normalizeFilePath,
  normalizePreviewCompilerMode,
  normalizeMaybeEmptyPath,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  projectReferencesProject,
  resolveConfiguredProjectPath,
  resolveLoopbackPreviewWebviewTarget,
  resolvePreviewDocumentText,
  resolvePreviewCompilerMode,
  samePath,
  shouldUseInlineLoopbackPreviewClient,
  shouldUseNoRestoreBuild,
  tryParseMsbuildJson
} = require('./preview-utils');

const PROJECT_INFO_PROPERTIES = [
  'TargetPath',
  'AvaloniaPreviewerNetCoreToolPath',
  'TargetFramework',
  'TargetFrameworks',
  'OutputType',
  'MSBuildProjectDirectory'
];

const DEFAULT_UPDATE_DELAY_MS = 300;
const DEFAULT_REQUEST_TIMEOUT_MS = 30000;
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
        this.launchInfo.previewPlan.preferredMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED);
  }

  handleDocumentChanged(document) {
    if (this.disposed) {
      return;
    }

    if (this.usesSourceGeneratedRefreshFlow()) {
      this.pendingUpdateText = null;
      this.setStatus('Source-generated preview shows the last successful build. Save to rebuild and refresh.');
      return;
    }

    this.scheduleLiveUpdate(document);
  }

  async handleDocumentSaved(document) {
    if (!this.usesSourceGeneratedRefreshFlow()) {
      return;
    }

    if (!this.controller.getConfiguration().get('preview.buildBeforeLaunch', true)) {
      this.setStatus('Source-generated preview shows the last successful build. Build the project manually or enable axsg.preview.buildBeforeLaunch, then reopen the preview to refresh.');
      return;
    }

    await this.refreshSourceGeneratedPreview(document, 'Refreshing source-generated preview...');
  }

  async handleOpenRequest(document) {
    if (this.usesSourceGeneratedRefreshFlow()) {
      if (document.isDirty) {
        this.setStatus('Source-generated preview shows the last successful build. Save to rebuild and refresh.');
        return;
      }

      await this.refreshSourceGeneratedPreview(document, 'Refreshing source-generated preview...');
      return;
    }

    this.scheduleLiveUpdate(document);
  }

  scheduleLiveUpdate(document) {
    if (this.disposed) {
      return;
    }

    this.pendingUpdateText = document.getText();
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
    panel.onDidDispose(() => {
      if (!this.disposed) {
        this.controller.removeSession(this.documentUri);
        void this.dispose();
      }
    });

    this.panel = panel;
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

    let lastError = null;

    for (let attemptIndex = 0; attemptIndex < attempts.length; attemptIndex += 1) {
      const attempt = attempts[attemptIndex];
      try {
        this.setStatus(`Starting ${attempt.label} preview...`);
        await this.startAttemptAsync(helperPath, dotNetCommand, outputChannel, attempt);
        this.activeCompilerMode = attempt.mode;
        this.setStatus(getPreviewReadyStatus(this.fileName, attempt.mode));
        this.updatePanel();
        return;
      } catch (error) {
        lastError = error;
        const message = error instanceof Error ? error.message : String(error);
        outputChannel.appendLine(`[preview] ${attempt.label} preview start failed: ${message}`);

        const helper = this.helper;
        if (helper) {
          await this.resetHelperAsync(helper);
        }

        if (attemptIndex < attempts.length - 1) {
          this.setStatus(`Falling back to ${attempts[attemptIndex + 1].label} preview...`);
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

    const payload = {
      dotNetCommand,
      hostAssemblyPath: this.launchInfo.hostProject.targetPath,
      previewerToolPath: attempt.previewerToolPath,
      sourceAssemblyPath: this.launchInfo.sourceProject.targetPath,
      xamlFileProjectPath: normalizePreviewTargetPath(this.launchInfo.projectContext.targetPath),
      xamlText: this.launchInfo.documentText
    };

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
    this.setStatus('Applying XAML update...');

    this.updateChain = this.updateChain
      .then(async () => {
        await this.start();
        if (this.usesSourceGeneratedRefreshFlow()) {
          this.setStatus('Source-generated preview shows the last successful build. Save to rebuild and refresh.');
          return;
        }

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
      status: this.currentStatus
    });
  }

  async updatePreviewUrlAsync(previewUrl) {
    const normalizedPreviewUrl = String(previewUrl || '').trim();
    const updateToken = ++this.previewUrlUpdateToken;
    this.rawPreviewUrl = normalizedPreviewUrl;

    if (!normalizedPreviewUrl) {
      this.currentPreviewUrl = '';
      this.currentLoopbackPreview = null;
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
        securityCookie
      };
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
    this.updatePanel();
  }
}

class AvaloniaPreviewController {
  constructor(options) {
    this.context = options.context;
    this.extensionPath = options.context.extensionPath;
    this.ensureClientStarted = options.ensureClientStarted;
    this.getOutputChannel = options.getOutputChannel;
    this.isXamlDocument = options.isXamlDocument;
    this.workspaceRoot = options.workspaceRoot;
    this.sessions = new Map();
    this.projectInfoCache = new Map();
    this.projectReferenceCache = new Map();
  }

  register(context) {
    context.subscriptions.push(vscode.commands.registerCommand('axsg.preview.open', async () => {
      try {
        await this.openPreviewForActiveEditor();
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        await vscode.window.showErrorMessage(`AXSG preview failed: ${message}`);
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
    this.sessions.delete(documentUri);
  }

  getConfiguration() {
    return vscode.workspace.getConfiguration('axsg');
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
      }
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
      requestedCompilerMode);
    launchInfo.documentText = resolvePreviewDocumentText(
      document.getText(),
      tryReadDocumentTextFromDisk(document.fileName),
      document.isDirty,
      launchInfo.previewPlan.preferredMode);
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
      activePreviewMode: launchInfo.previewPlan.preferredMode
    });
    const refreshedLaunchInfo = this.createLaunchInfo(
      launchInfo.projectContext,
      launchInfo.workspaceRoot,
      document.getText(),
      projectState.sourceProject,
      projectState.hostProject,
      requestedCompilerMode);
    refreshedLaunchInfo.documentText = resolvePreviewDocumentText(
      document.getText(),
      tryReadDocumentTextFromDisk(document.fileName),
      document.isDirty,
      refreshedLaunchInfo.previewPlan.preferredMode);
    return refreshedLaunchInfo;
  }

  createLaunchInfo(projectContext, workspaceRoot, documentText, sourceProject, hostProject, requestedCompilerMode) {
    return {
      projectContext,
      workspaceRoot,
      documentText,
      sourceProject,
      hostProject,
      previewPlan: this.createPreviewPlan(requestedCompilerMode, sourceProject)
    };
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
        requestedCompilerMode);
    }

    return this.buildAndRefreshProjectState({
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
      resolvePreviewCompilerMode(options.requestedCompilerMode, options.sourceProject).preferredMode;
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
      hostBuildIncludesSource
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

  async resolveHostProject(sourceProjectPath, sourceProjectInfo, dotNetCommand, workspaceRoot, requestedCompilerMode) {
    if (isUsablePreviewHostProjectInfo(sourceProjectInfo, sourceProjectInfo, requestedCompilerMode)) {
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
      if (!isUsablePreviewHostProjectInfo(configuredInfo, sourceProjectInfo, requestedCompilerMode)) {
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
      if (isUsablePreviewHostProjectInfo(rememberedInfo, sourceProjectInfo, requestedCompilerMode)) {
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
      if (isUsablePreviewHostProjectInfo(candidateInfo, sourceProjectInfo, requestedCompilerMode)) {
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
    }

    body {
      margin: 0;
      padding: 0;
      background: var(--vscode-editor-background);
      color: var(--vscode-editor-foreground);
      overflow: hidden;
    }

    .shell {
      display: grid;
      grid-template-rows: auto 1fr;
      height: 100vh;
    }

    .status {
      padding: 8px 12px;
      border-bottom: 1px solid var(--vscode-panel-border);
      background: var(--vscode-editorWidget-background);
      font-size: 12px;
      line-height: 1.4;
    }

    #content {
      min-height: 0;
    }

    .preview-frame {
      display: grid;
      place-items: start center;
      overflow: auto;
      height: 100%;
      background: white;
    }

    .preview-canvas {
      display: block;
      margin: 0;
      outline: none;
      image-rendering: pixelated;
    }

    iframe {
      width: 100%;
      height: 100%;
      border: 0;
      background: white;
    }

    .placeholder {
      display: grid;
      place-items: center;
      height: 100%;
      color: var(--vscode-descriptionForeground);
      font-size: 13px;
    }
  </style>
</head>
<body>
  <div class="shell">
    <div class="status" id="status">${statusText}</div>
    <div id="content">${iframeUrl
      ? `<iframe id="preview" src="${iframeUrl}" sandbox="allow-same-origin allow-scripts allow-forms allow-pointer-lock"></iframe>`
      : `<div class="placeholder">Preview is starting...</div>`}</div>
  </div>
  <script>
    const content = document.getElementById('content');
    const status = document.getElementById('status');
    let previewSocket = null;
    let previewSocketKey = '';
    let nextFrame = null;

    function disposePreviewSocket() {
      previewSocketKey = '';
      nextFrame = null;
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

    function renderPlaceholder(text) {
      disposePreviewSocket();
      content.innerHTML = '';
      const placeholder = document.createElement('div');
      placeholder.className = 'placeholder';
      placeholder.textContent = text || 'Preview is starting...';
      content.appendChild(placeholder);
    }

    function ensureCanvas() {
      let canvas = document.getElementById('preview-canvas');
      if (canvas) {
        return canvas;
      }

      content.innerHTML = '';
      const frame = document.createElement('div');
      frame.className = 'preview-frame';
      canvas = document.createElement('canvas');
      canvas.id = 'preview-canvas';
      canvas.className = 'preview-canvas';
      canvas.tabIndex = 0;
      wireCanvasInput(canvas);
      frame.appendChild(canvas);
      content.appendChild(frame);
      return canvas;
    }

    function renderIframe(previewUrl) {
      disposePreviewSocket();
      let iframe = document.getElementById('preview');
      if (!iframe) {
        content.innerHTML = '';
        iframe = document.createElement('iframe');
        iframe.id = 'preview';
        iframe.setAttribute('sandbox', 'allow-same-origin allow-scripts allow-forms allow-pointer-lock');
        content.appendChild(iframe);
      }

      if (iframe.src !== previewUrl) {
        iframe.src = previewUrl;
      }
    }

    function getCanvasPoint(event, canvas) {
      const rect = canvas.getBoundingClientRect();
      const scale = window.devicePixelRatio || 1;
      return {
        x: (event.clientX - rect.left) * scale,
        y: (event.clientY - rect.top) * scale
      };
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
      if (!previewSocket || previewSocket.readyState !== WebSocket.OPEN) {
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
      if (!previewSocket || previewSocket.readyState !== WebSocket.OPEN) {
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

    function wireCanvasInput(canvas) {
      canvas.addEventListener('pointerdown', event => {
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
        event.preventDefault();
        sendWheelMessage(event);
      }, { passive: false });
      canvas.addEventListener('contextmenu', event => event.preventDefault());
    }

    function renderFrame(frameBuffer) {
      if (!nextFrame) {
        return;
      }

      const canvas = ensureCanvas();
      canvas.width = nextFrame.width;
      canvas.height = nextFrame.height;
      canvas.style.width = (nextFrame.width / (window.devicePixelRatio || 1)) + 'px';
      canvas.style.height = (nextFrame.height / (window.devicePixelRatio || 1)) + 'px';

      const context = canvas.getContext('2d');
      const imageData = new ImageData(new Uint8ClampedArray(frameBuffer), nextFrame.width, nextFrame.height);
      context.putImageData(imageData, 0, 0);
      if (previewSocket && previewSocket.readyState === WebSocket.OPEN) {
        previewSocket.send('frame-received:' + nextFrame.sequenceId);
      }
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

      const socket = previewSocket = new WebSocket(loopbackPreview.webSocketUrl);
      socket.binaryType = 'arraybuffer';
      socket.onopen = () => {
        if (previewSocket !== socket) {
          return;
        }

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
              height: Number.parseInt(parts[3], 10)
            };
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

        renderPlaceholder('Preview transport failed to connect.');
      };
      socket.onclose = () => {
        if (previewSocket !== socket) {
          return;
        }

        previewSocket = null;
        previewSocketKey = '';
        if (!document.getElementById('preview-canvas')) {
          renderPlaceholder('Preview transport disconnected.');
        }
      };
    }

    function updatePreview(previewUrl, loopbackPreview, statusText) {
      status.textContent = statusText || 'Preview ready.';
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

    window.addEventListener('message', event => {
      const message = event.data || {};
      if (message.type === 'update') {
        updatePreview(message.previewUrl, message.loopbackPreview, message.status);
      }
    });
  </script>
</body>
</html>`;
}

function createPreviewWebviewOptions() {
  return {
    enableScripts: true
  };
}

async function fetchPreviewPageHtmlAsync(previewUrl, attemptCount = 10, delayMs = 150) {
  let lastError = null;

  for (let attemptIndex = 0; attemptIndex < attemptCount; attemptIndex += 1) {
    try {
      return await fetchTextFromUrlAsync(previewUrl);
    } catch (error) {
      lastError = error;
      if (attemptIndex < attemptCount - 1) {
        await delayAsync(delayMs);
      }
    }
  }

  throw lastError || new Error(`Failed to fetch ${previewUrl}.`);
}

async function fetchTextFromUrlAsync(urlText) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const parsedUrl = new URL(urlText);
    const client = parsedUrl.protocol === 'https:' ? https : http;
    const request = client.get(parsedUrl, response => {
      if (response.statusCode && response.statusCode >= 400) {
        settled = true;
        response.resume();
        reject(new Error(`HTTP ${response.statusCode} while fetching ${urlText}.`));
        return;
      }

      const chunks = [];
      response.setEncoding('utf8');
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        if (settled) {
          return;
        }

        settled = true;
        resolve(chunks.join(''));
      });
      response.on('error', error => {
        if (settled) {
          return;
        }

        settled = true;
        reject(error);
      });
    });

    request.setTimeout(DEFAULT_REQUEST_TIMEOUT_MS, () => {
      if (settled) {
        return;
      }

      settled = true;
      request.destroy(new Error(`Timed out fetching ${urlText}.`));
    });
    request.on('error', error => {
      if (settled) {
        return;
      }

      settled = true;
      reject(error);
    });
  });
}

function delayAsync(delayMs) {
  return new Promise(resolve => setTimeout(resolve, delayMs));
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
  const startPlan = createPreviewStartPlan({
    requestedMode,
    preferredMode,
    hasSourceGeneratedDesignerHost: fs.existsSync(designerHostPath),
    hasAvaloniaPreviewer: Boolean(launchInfo.hostProject.previewerToolPath)
  });

  if (startPlan.requiresSourceGeneratedDesignerHost) {
    throw new Error(`Bundled source-generated designer host not found at ${designerHostPath}. Run the extension packaging step first.`);
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
      attempts.push({
        label: 'Avalonia XamlX',
        mode,
        previewerToolPath: launchInfo.hostProject.previewerToolPath
      });
    }
  }

  return attempts;
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
    return `Source-generated preview ready for ${fileName}. Showing the last successful build; save to rebuild and refresh.`;
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
