const path = require('path');
const fs = require('fs');
const vscode = require('vscode');
const lc = require('vscode-languageclient/node');

let client;
let outputChannel;
let statusBarItem;
const AXSG_METADATA_SCHEME = 'axsg-metadata';
const AXSG_SOURCELINK_SCHEME = 'axsg-sourcelink';
const sourceLinkDocumentCache = new Map();
const sourceLinkUriSubscriptions = new Map();
let sourceLinkChangeEmitter;

function decodeQueryValue(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function renderMetadataDocument(uri) {
  const query = new URLSearchParams(uri.query || '');
  const kind = query.get('kind');

  if (kind === 'type') {
    const fullTypeName = decodeQueryValue(query.get('type') || 'Unknown.Type');
    const lastSeparator = fullTypeName.lastIndexOf('.');
    const namespaceName = lastSeparator > 0 ? fullTypeName.substring(0, lastSeparator) : 'GlobalNamespace';
    const typeName = lastSeparator > 0 ? fullTypeName.substring(lastSeparator + 1) : fullTypeName;

    return `// AXSG metadata projection\n// Generated for external symbol navigation.\n\nnamespace ${namespaceName}\n{\n    public class ${typeName}\n    {\n    }\n}\n`;
  }

  if (kind === 'property') {
    const ownerTypeName = decodeQueryValue(query.get('owner') || 'Unknown.Type');
    const propertyName = decodeQueryValue(query.get('name') || 'Property');
    const propertyTypeName = decodeQueryValue(query.get('type') || 'object');
    const isAttached = (query.get('attached') || '').toLowerCase() === 'true';
    const isSettable = (query.get('settable') || '').toLowerCase() === 'true';
    const lastSeparator = ownerTypeName.lastIndexOf('.');
    const namespaceName = lastSeparator > 0 ? ownerTypeName.substring(0, lastSeparator) : 'GlobalNamespace';
    const typeName = lastSeparator > 0 ? ownerTypeName.substring(lastSeparator + 1) : ownerTypeName;

    const setterSuffix = isSettable ? ' set;' : '';
    const declaration = isAttached
      ? `public static ${propertyTypeName} ${propertyName} { get;${setterSuffix} }`
      : `public ${propertyTypeName} ${propertyName} { get;${setterSuffix} }`;

    return `// AXSG metadata projection\n// Generated for external symbol navigation.\n\nnamespace ${namespaceName}\n{\n    public class ${typeName}\n    {\n        ${declaration}\n    }\n}\n`;
  }

  return '// AXSG metadata projection\n// No symbol details available.\n';
}

function createSourceLinkLoadingDocument(sourceUrl) {
  return `// AXSG source-link projection\n// Loading source from ${sourceUrl}...\n`;
}

function updateSourceLinkCacheAndNotify(sourceUrl, state, text) {
  sourceLinkDocumentCache.set(sourceUrl, { state, text });

  const subscribers = sourceLinkUriSubscriptions.get(sourceUrl);
  if (!subscribers || !sourceLinkChangeEmitter) {
    return;
  }

  for (const uriString of subscribers) {
    try {
      sourceLinkChangeEmitter.fire(vscode.Uri.parse(uriString));
    } catch {
      // Ignore malformed URI entries.
    }
  }
}

function trackSourceLinkUri(sourceUrl, uri) {
  let subscribers = sourceLinkUriSubscriptions.get(sourceUrl);
  if (!subscribers) {
    subscribers = new Set();
    sourceLinkUriSubscriptions.set(sourceUrl, subscribers);
  }

  subscribers.add(uri.toString());
}

async function fetchAndCacheSourceLinkDocument(sourceUrl) {
  const cached = sourceLinkDocumentCache.get(sourceUrl);
  if (cached && cached.state !== 'loading') {
    return;
  }

  try {
    const response = await fetch(sourceUrl, {
      headers: {
        'User-Agent': 'axsg-language-server'
      }
    });

    if (!response.ok) {
      const failure = `// AXSG source-link projection\n// Failed to load source from ${sourceUrl}.\n// HTTP ${response.status} ${response.statusText}\n`;
      updateSourceLinkCacheAndNotify(sourceUrl, 'error', failure);
      return;
    }

    const text = await response.text();
    updateSourceLinkCacheAndNotify(sourceUrl, 'ready', text);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    const failure = `// AXSG source-link projection\n// Failed to load source from ${sourceUrl}.\n// ${message}\n`;
    updateSourceLinkCacheAndNotify(sourceUrl, 'error', failure);
  }
}

function renderSourceLinkDocument(uri) {
  const query = new URLSearchParams(uri.query || '');
  const encodedUrl = query.get('url');
  if (!encodedUrl) {
    return '// AXSG source-link projection\n// Missing source URL.\n';
  }

  const sourceUrl = decodeQueryValue(encodedUrl);
  trackSourceLinkUri(sourceUrl, uri);

  const cached = sourceLinkDocumentCache.get(sourceUrl);
  if (cached && cached.state !== 'loading') {
    return cached.text;
  }

  if (!cached) {
    const loadingText = createSourceLinkLoadingDocument(sourceUrl);
    sourceLinkDocumentCache.set(sourceUrl, { state: 'loading', text: loadingText });
    void fetchAndCacheSourceLinkDocument(sourceUrl);
    return loadingText;
  }

  return cached.text;
}

function resolveWorkspaceRoot() {
  const folders = vscode.workspace.workspaceFolders;
  if (!folders || folders.length === 0) {
    return process.cwd();
  }

  return folders[0].uri.fsPath;
}

function resolveBundledServerPath(context) {
  return path.join(context.extensionPath, 'server', 'XamlToCSharpGenerator.LanguageServer.dll');
}

function resolveServerOptions(context) {
  const configuration = vscode.workspace.getConfiguration('axsg');
  const requestedMode = configuration.get('languageServer.mode', 'bundled');
  const command = configuration.get('languageServer.command', 'axsg-lsp');
  const args = configuration.get('languageServer.args', []);
  const workspaceRoot = resolveWorkspaceRoot();
  const sharedArgs = [...args, '--workspace', workspaceRoot];

  let resolvedCommand = command;
  let resolvedArgs = sharedArgs;
  let effectiveMode = requestedMode;
  let bundledServerPath = null;

  if (requestedMode === 'bundled') {
    bundledServerPath = resolveBundledServerPath(context);
    if (fs.existsSync(bundledServerPath)) {
      resolvedCommand = 'dotnet';
      resolvedArgs = [bundledServerPath, ...sharedArgs];
    } else {
      effectiveMode = 'custom';
      vscode.window.showWarningMessage(
        `AXSG bundled language server not found at ${bundledServerPath}. Falling back to custom command.`
      );
    }
  }

  return {
    serverOptions: {
      command: resolvedCommand,
      args: resolvedArgs,
      transport: lc.TransportKind.stdio,
      options: {
        cwd: workspaceRoot,
        env: process.env
      }
    },
    details: {
      requestedMode,
      effectiveMode,
      workspaceRoot,
      command: resolvedCommand,
      args: resolvedArgs,
      bundledServerPath
    }
  };
}

function resolveClientOptions(context) {
  outputChannel = outputChannel ?? vscode.window.createOutputChannel('AXSG Language Server');
  return {
    documentSelector: [
      { scheme: 'file', language: 'axaml' },
      { scheme: 'file', language: 'xaml' }
    ],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{xaml,axaml}')
    },
    outputChannel,
    initializationOptions: {
      extensionPath: context.extensionPath
    }
  };
}

function setStatusBarState(state, details, errorMessage) {
  if (!statusBarItem) {
    return;
  }

  if (state === 'starting') {
    statusBarItem.text = '$(sync~spin) AXSG';
  } else if (state === 'running') {
    statusBarItem.text = '$(info) AXSG';
  } else {
    statusBarItem.text = '$(error) AXSG';
  }

  const argsText = details.args.length > 0
    ? details.args.join(' ')
    : '<none>';
  const errorText = errorMessage
    ? `\nError: ${errorMessage}`
    : '';
  statusBarItem.tooltip = `AXSG Language Server\nState: ${state}\nMode: ${details.effectiveMode} (requested: ${details.requestedMode})\nCommand: ${details.command}\nArgs: ${argsText}\nWorkspace: ${details.workspaceRoot}${errorText}`;
}

async function activate(context) {
  const { serverOptions, details } = resolveServerOptions(context);
  const clientOptions = resolveClientOptions(context);
  const metadataProvider = {
    provideTextDocumentContent(uri) {
      return renderMetadataDocument(uri);
    }
  };
  sourceLinkChangeEmitter = new vscode.EventEmitter();
  context.subscriptions.push(sourceLinkChangeEmitter);
  const sourceLinkProvider = {
    onDidChange: sourceLinkChangeEmitter.event,
    provideTextDocumentContent(uri) {
      return renderSourceLinkDocument(uri);
    }
  };
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(
    AXSG_METADATA_SCHEME,
    metadataProvider));
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(
    AXSG_SOURCELINK_SCHEME,
    sourceLinkProvider));
  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(async document => {
    if (document.uri.scheme !== AXSG_METADATA_SCHEME &&
        document.uri.scheme !== AXSG_SOURCELINK_SCHEME) {
      return;
    }

    if (document.languageId === 'csharp') {
      return;
    }

    try {
      await vscode.languages.setTextDocumentLanguage(document, 'csharp');
    } catch {
      // Ignore language switch failures for virtual metadata docs.
    }
  }));

  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 110);
  statusBarItem.name = 'AXSG Language Server';
  statusBarItem.command = 'axsg.languageServer.showInfo';
  context.subscriptions.push(statusBarItem);
  statusBarItem.show();
  setStatusBarState('starting', details);

  context.subscriptions.push(vscode.commands.registerCommand('axsg.languageServer.showInfo', async () => {
    const info = `AXSG Language Server (${details.effectiveMode})`;
    const selection = await vscode.window.showInformationMessage(
      info,
      'Open Output');
    if (selection === 'Open Output' && outputChannel) {
      outputChannel.show(true);
    }
  }));

  client = new lc.LanguageClient(
    'axsgLanguageServer',
    'AXSG Language Server',
    serverOptions,
    clientOptions);

  const trace = vscode.workspace.getConfiguration('axsg').get('languageServer.trace', 'off');
  client.setTrace(trace);

  context.subscriptions.push({
    dispose: async () => {
      if (client) {
        await client.stop();
        client = undefined;
      }
      if (statusBarItem) {
        statusBarItem.dispose();
        statusBarItem = undefined;
      }
    }
  });

  try {
    await client.start();
    setStatusBarState('running', details);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    setStatusBarState('error', details, message);
    throw error;
  }
}

async function deactivate() {
  if (!client) {
    if (statusBarItem) {
      statusBarItem.dispose();
      statusBarItem = undefined;
    }
    return;
  }

  await client.stop();
  client = undefined;
  if (statusBarItem) {
    statusBarItem.dispose();
    statusBarItem = undefined;
  }

  if (sourceLinkChangeEmitter) {
    sourceLinkChangeEmitter.dispose();
    sourceLinkChangeEmitter = undefined;
  }
}

module.exports = {
  activate,
  deactivate
};
