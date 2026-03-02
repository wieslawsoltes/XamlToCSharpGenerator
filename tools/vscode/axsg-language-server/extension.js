const path = require('path');
const vscode = require('vscode');
const lc = require('vscode-languageclient/node');

let client;

function resolveWorkspaceRoot() {
  const folders = vscode.workspace.workspaceFolders;
  if (!folders || folders.length === 0) {
    return process.cwd();
  }

  return folders[0].uri.fsPath;
}

function resolveServerOptions() {
  const configuration = vscode.workspace.getConfiguration('axsg');
  const command = configuration.get('languageServer.command', 'axsg-lsp');
  const args = configuration.get('languageServer.args', []);
  const workspaceRoot = resolveWorkspaceRoot();

  const resolvedArgs = [...args, '--workspace', workspaceRoot];
  return {
    command,
    args: resolvedArgs,
    transport: lc.TransportKind.stdio,
    options: {
      cwd: workspaceRoot,
      env: process.env
    }
  };
}

function resolveClientOptions(context) {
  return {
    documentSelector: [
      { scheme: 'file', language: 'axaml' },
      { scheme: 'file', language: 'xaml' }
    ],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{xaml,axaml}')
    },
    outputChannel: vscode.window.createOutputChannel('AXSG Language Server'),
    initializationOptions: {
      extensionPath: context.extensionPath
    }
  };
}

async function activate(context) {
  const serverOptions = resolveServerOptions();
  const clientOptions = resolveClientOptions(context);

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
    }
  });

  await client.start();
}

async function deactivate() {
  if (!client) {
    return;
  }

  await client.stop();
  client = undefined;
}

module.exports = {
  activate,
  deactivate
};
