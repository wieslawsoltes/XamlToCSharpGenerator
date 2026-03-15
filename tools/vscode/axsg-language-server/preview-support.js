const path = require('path');
const fs = require('fs');
const cp = require('child_process');
const readline = require('readline');
const { EventEmitter } = require('events');
const vscode = require('vscode');
const {
  buildArguments,
  isPreviewableProjectInfo,
  isUnderBuildOutput,
  normalizeFilePath,
  normalizeMaybeEmptyPath,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  resolveConfiguredProjectPath,
  samePath,
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
    this.currentStatus = 'Starting preview...';
    this.pendingUpdateText = null;
    this.updateTimer = null;
    this.updateChain = Promise.resolve();
    this.disposed = false;
    this.startPromise = null;
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
    this.startPromise = this.startCore();
    return this.startPromise;
  }

  scheduleUpdate(document) {
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
  }

  createPanel() {
    if (this.panel) {
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      'axsgPreview',
      `AXSG Preview: ${this.fileName}`,
      vscode.ViewColumn.Beside,
      {
        enableScripts: true,
        retainContextWhenHidden: true
      });

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

    this.helper = new JsonLineHelperClient(dotNetCommand, [helperPath], {
      cwd: this.launchInfo.workspaceRoot,
      outputChannel
    });
    const helper = this.helper;
    helper.on('event', message => this.handleHelperEvent(message));
    helper.on('exit', ({ exitCode, error }) => {
      if (this.disposed) {
        return;
      }

      const exitText = error ? error.message : `Preview host exited (${exitCode ?? 'null'}).`;
      void this.handleHostUnavailableAsync(helper, exitText, true);
    });

    const payload = {
      dotNetCommand,
      hostAssemblyPath: this.launchInfo.hostProject.targetPath,
      previewerToolPath: this.launchInfo.hostProject.previewerToolPath,
      sourceAssemblyPath: this.launchInfo.sourceProject.targetPath,
      xamlFileProjectPath: normalizePreviewTargetPath(this.launchInfo.projectContext.targetPath),
      xamlText: this.launchInfo.documentText
    };

    const startResult = await helper.sendCommand('start', payload);
    const previewUrl = startResult?.previewUrl || '';
    if (!previewUrl) {
      throw new Error('Avalonia preview host did not return a preview URL.');
    }

    this.currentPreviewUrl = previewUrl;
    this.setStatus(`Preview ready for ${this.fileName}.`);
    this.updatePanel();
  }

  async flushPendingUpdate() {
    if (this.disposed || !this.pendingUpdateText) {
      return;
    }

    const updateText = this.pendingUpdateText;
    this.pendingUpdateText = null;
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
      this.currentPreviewUrl = payload.previewUrl;
      this.setStatus(`Preview ready for ${this.fileName}.`);
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
      this.currentPreviewUrl = '';
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
      status: this.currentStatus
    });
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
        session.scheduleUpdate(event.document);
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
      existing.scheduleUpdate(document);
      return;
    }

    const session = await vscode.window.withProgress(
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
        try {
          await createdSession.start();
          return createdSession;
        } catch (error) {
          await createdSession.dispose();
          throw error;
        }
      });

    this.sessions.set(document.uri.toString(), session);
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

    progress.report({ message: 'Evaluating source project...' });
    const sourceProject = await this.getProjectInfo(
      projectContext.projectPath,
      preferredTargetFramework,
      dotNetCommand,
      false,
      workspaceRoot);

    progress.report({ message: 'Selecting preview host project...' });
    const hostProject = await this.resolveHostProject(
      projectContext.projectPath,
      sourceProject,
      dotNetCommand,
      workspaceRoot);

    if (configuration.get('preview.buildBeforeLaunch', true)) {
      progress.report({ message: `Building ${path.basename(hostProject.projectPath)}...` });
      await runDotNetCommand(
        dotNetCommand,
        buildArguments(hostProject.projectPath, hostProject.targetFramework),
        workspaceRoot,
        this.getOutputChannel());

      if (!samePath(hostProject.projectPath, sourceProject.projectPath)) {
        progress.report({ message: `Building ${path.basename(sourceProject.projectPath)}...` });
        await runDotNetCommand(
          dotNetCommand,
          buildArguments(sourceProject.projectPath, sourceProject.targetFramework),
          workspaceRoot,
          this.getOutputChannel());
      }

      progress.report({ message: 'Refreshing build outputs...' });
      const refreshedHostProject = await this.getProjectInfo(
        hostProject.projectPath,
        hostProject.targetFramework || preferredTargetFramework,
        dotNetCommand,
        true,
        workspaceRoot);
      const refreshedSourceProject = samePath(hostProject.projectPath, sourceProject.projectPath)
        ? refreshedHostProject
        : await this.getProjectInfo(
          projectContext.projectPath,
          sourceProject.targetFramework || preferredTargetFramework,
          dotNetCommand,
          true,
          workspaceRoot);

      return {
        projectContext,
        workspaceRoot,
        documentText: document.getText(),
        sourceProject: refreshedSourceProject,
        hostProject: refreshedHostProject
      };
    }

    return {
      projectContext,
      workspaceRoot,
      documentText: document.getText(),
      sourceProject,
      hostProject
    };
  }

  async resolveHostProject(sourceProjectPath, sourceProjectInfo, dotNetCommand, workspaceRoot) {
    if (isPreviewableProjectInfo(sourceProjectInfo)) {
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
      if (!isPreviewableProjectInfo(configuredInfo)) {
        throw new Error(`Configured preview host project is not previewable: ${resolvedConfiguredProject}`);
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
      if (isPreviewableProjectInfo(rememberedInfo)) {
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
      if (isPreviewableProjectInfo(candidateInfo)) {
        previewableCandidates.push(candidateInfo);
      }
    }

    if (previewableCandidates.length === 0) {
      throw new Error('No previewable Avalonia executable project was found in the workspace. Set axsg.preview.hostProject to choose one explicitly.');
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
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-src http: https:;">
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

    function updatePreview(previewUrl, statusText) {
      status.textContent = statusText || 'Preview ready.';
      if (previewUrl) {
        const escapedUrl = String(previewUrl);
        let iframe = document.getElementById('preview');
        if (!iframe) {
          content.innerHTML = '';
          iframe = document.createElement('iframe');
          iframe.id = 'preview';
          iframe.setAttribute('sandbox', 'allow-same-origin allow-scripts allow-forms allow-pointer-lock');
          content.appendChild(iframe);
        }

        if (iframe.src !== escapedUrl) {
          iframe.src = escapedUrl;
        }
        return;
      }

      content.innerHTML = '<div class="placeholder">Preview is starting...</div>';
    }

    window.addEventListener('message', event => {
      const message = event.data || {};
      if (message.type === 'update') {
        updatePreview(message.previewUrl, message.status);
      }
    });
  </script>
</body>
</html>`;
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

    let stderrText = '';
    child.stdout.on('data', chunk => {
      outputChannel.appendLine(String(chunk).trimEnd());
    });
    child.stderr.on('data', chunk => {
      const text = String(chunk).trimEnd();
      stderrText += text;
      outputChannel.appendLine(text);
    });
    child.on('error', error => {
      reject(error);
    });
    child.on('exit', exitCode => {
      if (exitCode === 0) {
        resolve();
        return;
      }

      reject(new Error(stderrText || `Command '${command} ${args.join(' ')}' failed with exit code ${exitCode}.`));
    });
  });
}

function resolveBundledPreviewHostPath(extensionPath) {
  return path.join(extensionPath, 'preview-host', PREVIEW_HOST_ASSEMBLY_NAME);
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
