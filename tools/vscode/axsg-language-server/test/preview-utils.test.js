const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');

const {
  buildArguments,
  createPreviewBuildPlan,
  createPreviewStartPlan,
  extractPreviewSecurityCookie,
  hasPendingPreviewText,
  isExecutableProjectInfo,
  isInputNewerThanOutput,
  isPreviewableProjectInfo,
  isUsablePreviewHostProjectInfo,
  normalizePreviewCompilerMode,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  PREVIEW_COMPILER_MODE_AVALONIA,
  resolveLoopbackPreviewWebviewTarget,
  resolveConfiguredProjectPath,
  resolvePreviewDocumentText,
  resolvePreviewCompilerMode,
  PREVIEW_COMPILER_MODE_AUTO,
  PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
  projectReferencesProject,
  samePath,
  shouldUseInlineLoopbackPreviewClient,
  shouldUseNoRestoreBuild,
  supportsSourceGeneratedPreview,
  tryParseMsbuildJson
} = require('../preview-utils');

test('pickPreviewTargetFramework prefers configured desktop target framework', () => {
  const actual = pickPreviewTargetFramework(
    'net8.0;net10.0-ios;net10.0',
    'net8.0');

  assert.equal(actual, 'net8.0');
});

test('pickPreviewTargetFramework skips mobile targets and picks the best desktop framework', () => {
  const actual = pickPreviewTargetFramework(
    'net10.0-ios;net8.0-android;net9.0;net8.0',
    '');

  assert.equal(actual, 'net9.0');
});

test('normalizePreviewTargetPath prefixes a slash and normalizes separators', () => {
  assert.equal(normalizePreviewTargetPath('Views\\MainView.axaml'), '/Views/MainView.axaml');
  assert.equal(normalizePreviewTargetPath('/Views/MainView.axaml'), '/Views/MainView.axaml');
});

test('normalizePreviewCompilerMode falls back to auto for unknown values', () => {
  assert.equal(normalizePreviewCompilerMode('sourceGenerated'), PREVIEW_COMPILER_MODE_SOURCE_GENERATED);
  assert.equal(normalizePreviewCompilerMode('avalonia'), PREVIEW_COMPILER_MODE_AVALONIA);
  assert.equal(normalizePreviewCompilerMode('unexpected'), PREVIEW_COMPILER_MODE_AUTO);
});

test('hasPendingPreviewText keeps intentionally empty XAML updates', () => {
  assert.equal(hasPendingPreviewText(''), true);
  assert.equal(hasPendingPreviewText(null), false);
  assert.equal(hasPendingPreviewText(undefined), false);
});

test('isExecutableProjectInfo requires an executable output and target path', () => {
  assert.equal(isExecutableProjectInfo({
    outputType: 'Exe',
    targetPath: '/tmp/Demo.dll'
  }), true);
  assert.equal(isExecutableProjectInfo({
    outputType: 'Library',
    targetPath: '/tmp/Demo.dll'
  }), false);
});

test('isPreviewableProjectInfo requires an executable output and previewer path', () => {
  assert.equal(isPreviewableProjectInfo({
    outputType: 'WinExe',
    targetPath: '/tmp/Demo.dll',
    previewerToolPath: '/tmp/Avalonia.Designer.HostApp.dll'
  }), true);
  assert.equal(isPreviewableProjectInfo({
    outputType: 'Library',
    targetPath: '/tmp/Demo.dll',
    previewerToolPath: '/tmp/Avalonia.Designer.HostApp.dll'
  }), false);
});

test('isUsablePreviewHostProjectInfo accepts executable hosts without previewer path for source-generated mode', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const targetPath = path.join(tempRoot, 'App.dll');
    fs.writeFileSync(targetPath, '', 'utf8');
    fs.writeFileSync(path.join(tempRoot, 'XamlToCSharpGenerator.Runtime.Avalonia.dll'), '', 'utf8');

    const sourceProjectInfo = {
      targetPath
    };
    const hostProjectInfo = {
      outputType: 'Exe',
      targetPath,
      previewerToolPath: ''
    };

    assert.equal(
      isUsablePreviewHostProjectInfo(hostProjectInfo, sourceProjectInfo, PREVIEW_COMPILER_MODE_SOURCE_GENERATED),
      true);
    assert.equal(
      isUsablePreviewHostProjectInfo(hostProjectInfo, sourceProjectInfo, PREVIEW_COMPILER_MODE_AUTO),
      true);
    assert.equal(
      isUsablePreviewHostProjectInfo(hostProjectInfo, sourceProjectInfo, PREVIEW_COMPILER_MODE_AVALONIA),
      false);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('tryParseMsbuildJson reads the property payload from stdout', () => {
  const parsed = tryParseMsbuildJson('\n{\n  "Properties": {\n    "TargetPath": "/tmp/demo.dll"\n  }\n}\n');
  assert.deepEqual(parsed, {
    Properties: {
      TargetPath: '/tmp/demo.dll'
    }
  });
});

test('resolveConfiguredProjectPath accepts a direct project path and a folder path', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const projectPath = path.join(tempRoot, 'Demo.csproj');
    fs.writeFileSync(projectPath, '<Project Sdk="Microsoft.NET.Sdk" />', 'utf8');

    assert.equal(resolveConfiguredProjectPath(projectPath, tempRoot), projectPath);
    assert.equal(resolveConfiguredProjectPath('.', tempRoot), projectPath);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('buildArguments adds the target framework and no-restore when provided', () => {
  assert.deepEqual(buildArguments('/tmp/Demo.csproj', 'net10.0'), [
    'build',
    '/tmp/Demo.csproj',
    '-nologo',
    '-v:minimal',
    '-p:TargetFramework=net10.0'
  ]);
  assert.deepEqual(buildArguments('/tmp/Demo.csproj', 'net10.0', { skipRestore: true }), [
    'build',
    '/tmp/Demo.csproj',
    '-nologo',
    '-v:minimal',
    '--no-restore',
    '-p:TargetFramework=net10.0'
  ]);
});

test('supportsSourceGeneratedPreview detects the runtime assembly in the output directory', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const targetPath = path.join(tempRoot, 'Demo.dll');
    fs.writeFileSync(targetPath, '', 'utf8');
    fs.writeFileSync(path.join(tempRoot, 'XamlToCSharpGenerator.Runtime.Avalonia.dll'), '', 'utf8');

    assert.equal(supportsSourceGeneratedPreview({ targetPath }), true);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('supportsSourceGeneratedPreview detects the runtime dependency from deps.json', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const targetPath = path.join(tempRoot, 'Demo.dll');
    const depsPath = path.join(tempRoot, 'Demo.deps.json');
    fs.writeFileSync(targetPath, '', 'utf8');
    fs.writeFileSync(depsPath, JSON.stringify({
      libraries: {
        'XamlToCSharpGenerator.Runtime.Avalonia/1.0.0': {}
      }
    }), 'utf8');

    assert.equal(supportsSourceGeneratedPreview({ targetPath }), true);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('resolvePreviewCompilerMode prefers source-generated preview in auto mode when supported', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const targetPath = path.join(tempRoot, 'Demo.dll');
    fs.writeFileSync(targetPath, '', 'utf8');
    fs.writeFileSync(path.join(tempRoot, 'XamlToCSharpGenerator.Runtime.Avalonia.dll'), '', 'utf8');

    const actual = resolvePreviewCompilerMode('auto', { targetPath });
    assert.deepEqual(actual, {
      requestedMode: PREVIEW_COMPILER_MODE_AUTO,
      preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
      sourceGeneratedSupported: true
    });
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('resolvePreviewCompilerMode falls back to Avalonia mode when source-generated support is missing', () => {
  const actual = resolvePreviewCompilerMode('auto', { targetPath: '/tmp/Demo.dll' });
  assert.deepEqual(actual, {
    requestedMode: PREVIEW_COMPILER_MODE_AUTO,
    preferredMode: PREVIEW_COMPILER_MODE_AVALONIA,
    sourceGeneratedSupported: false
  });
});

test('resolvePreviewDocumentText uses persisted text for dirty source-generated startup', () => {
  assert.equal(
    resolvePreviewDocumentText('<UserControl Text="Dirty" />', '<UserControl Text="Saved" />', true, PREVIEW_COMPILER_MODE_SOURCE_GENERATED),
    '<UserControl Text="Saved" />');
  assert.equal(
    resolvePreviewDocumentText('<UserControl Text="Dirty" />', '<UserControl Text="Saved" />', true, PREVIEW_COMPILER_MODE_AVALONIA),
    '<UserControl Text="Dirty" />');
});

test('resolvePreviewDocumentText requires saved content for dirty source-generated startup', () => {
  assert.throws(
    () => resolvePreviewDocumentText('<UserControl />', undefined, true, PREVIEW_COMPILER_MODE_SOURCE_GENERATED),
    /requires the file to be saved/i);
});

test('resolveLoopbackPreviewWebviewTarget preserves the loopback host and builds the websocket URL', () => {
  assert.deepEqual(
    resolveLoopbackPreviewWebviewTarget('http://127.0.0.1:52704/'),
    {
      previewUrl: 'http://127.0.0.1:52704/',
      webSocketUrl: 'ws://127.0.0.1:52704/ws'
    });
});

test('resolveLoopbackPreviewWebviewTarget ignores non-loopback preview URLs', () => {
  assert.equal(
    resolveLoopbackPreviewWebviewTarget('https://example.com/preview'),
    null);
});

test('extractPreviewSecurityCookie reads the Avalonia preview cookie from index html', () => {
  assert.equal(
    extractPreviewSecurityCookie(`
      <script>
        window["avaloniaPreviewerSecurityCookie"] = "abc-123";
      </script>`),
    'abc-123');
  assert.equal(extractPreviewSecurityCookie('<html></html>'), '');
});

test('shouldUseInlineLoopbackPreviewClient only enables the direct websocket client for local desktop sessions', () => {
  assert.equal(shouldUseInlineLoopbackPreviewClient('', 1), true);
  assert.equal(shouldUseInlineLoopbackPreviewClient(undefined, 1), true);
  assert.equal(shouldUseInlineLoopbackPreviewClient('ssh-remote', 1), false);
  assert.equal(shouldUseInlineLoopbackPreviewClient('', 2), false);
});

test('samePath compares Windows paths case-insensitively', () => {
  const originalPlatform = Object.getOwnPropertyDescriptor(process, 'platform');
  Object.defineProperty(process, 'platform', { value: 'win32' });
  try {
    assert.equal(
      samePath('C:\\Repo\\Samples\\App.csproj', 'c:\\repo\\samples\\APP.csproj'),
      true);
  } finally {
    Object.defineProperty(process, 'platform', originalPlatform);
  }
});

test('createPreviewStartPlan allows forced source-generated preview without Avalonia host metadata', () => {
  const actual = createPreviewStartPlan({
    requestedMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    hasSourceGeneratedDesignerHost: true,
    hasAvaloniaPreviewer: false
  });

  assert.deepEqual(actual, {
    modes: [PREVIEW_COMPILER_MODE_SOURCE_GENERATED],
    requiresSourceGeneratedDesignerHost: false,
    requiresAvaloniaPreviewer: false
  });
});

test('createPreviewStartPlan does not fall back to Avalonia when source-generated mode is forced', () => {
  const actual = createPreviewStartPlan({
    requestedMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    hasSourceGeneratedDesignerHost: true,
    hasAvaloniaPreviewer: true
  });

  assert.deepEqual(actual, {
    modes: [PREVIEW_COMPILER_MODE_SOURCE_GENERATED],
    requiresSourceGeneratedDesignerHost: false,
    requiresAvaloniaPreviewer: false
  });
});

test('createPreviewStartPlan falls back to Avalonia only in auto mode when source-generated host is unavailable', () => {
  const actual = createPreviewStartPlan({
    requestedMode: PREVIEW_COMPILER_MODE_AUTO,
    preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    hasSourceGeneratedDesignerHost: false,
    hasAvaloniaPreviewer: true
  });

  assert.deepEqual(actual, {
    modes: [PREVIEW_COMPILER_MODE_AVALONIA],
    requiresSourceGeneratedDesignerHost: false,
    requiresAvaloniaPreviewer: false
  });
});

test('createPreviewStartPlan requires the bundled designer host when source-generated mode is forced', () => {
  const actual = createPreviewStartPlan({
    requestedMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
    hasSourceGeneratedDesignerHost: false,
    hasAvaloniaPreviewer: true
  });

  assert.deepEqual(actual, {
    modes: [],
    requiresSourceGeneratedDesignerHost: true,
    requiresAvaloniaPreviewer: false
  });
});

test('shouldUseNoRestoreBuild detects an existing project.assets.json file', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const projectPath = path.join(tempRoot, 'Demo.csproj');
    fs.mkdirSync(path.join(tempRoot, 'obj'), { recursive: true });
    fs.writeFileSync(projectPath, '<Project />', 'utf8');
    fs.writeFileSync(path.join(tempRoot, 'obj', 'project.assets.json'), '{}', 'utf8');

    assert.equal(shouldUseNoRestoreBuild(projectPath), true);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('projectReferencesProject follows project references transitively', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const appProjectPath = path.join(tempRoot, 'App', 'App.csproj');
    const shellProjectPath = path.join(tempRoot, 'Shell', 'Shell.csproj');
    const libraryProjectPath = path.join(tempRoot, 'Library', 'Library.csproj');
    fs.mkdirSync(path.dirname(appProjectPath), { recursive: true });
    fs.mkdirSync(path.dirname(shellProjectPath), { recursive: true });
    fs.mkdirSync(path.dirname(libraryProjectPath), { recursive: true });
    fs.writeFileSync(appProjectPath, '<Project><ItemGroup><ProjectReference Include="../Shell/Shell.csproj" /></ItemGroup></Project>', 'utf8');
    fs.writeFileSync(shellProjectPath, '<Project><ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup></Project>', 'utf8');
    fs.writeFileSync(libraryProjectPath, '<Project />', 'utf8');

    assert.equal(projectReferencesProject(appProjectPath, libraryProjectPath), true);
    assert.equal(projectReferencesProject(libraryProjectPath, appProjectPath), false);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('isInputNewerThanOutput compares file modification times', async () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const inputPath = path.join(tempRoot, 'Input.xaml');
    const outputPath = path.join(tempRoot, 'Output.dll');
    fs.writeFileSync(outputPath, '', 'utf8');
    await new Promise(resolve => setTimeout(resolve, 15));
    fs.writeFileSync(inputPath, '', 'utf8');

    assert.equal(isInputNewerThanOutput(inputPath, outputPath), true);
    assert.equal(isInputNewerThanOutput(outputPath, inputPath), false);
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('createPreviewBuildPlan skips launch builds when Avalonia preview outputs already exist', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const sourceTargetPath = path.join(tempRoot, 'Library.dll');
    const hostTargetPath = path.join(tempRoot, 'App.dll');
    fs.writeFileSync(sourceTargetPath, '', 'utf8');
    fs.writeFileSync(hostTargetPath, '', 'utf8');

    const actual = createPreviewBuildPlan({
      buildReason: 'launch',
      previewMode: PREVIEW_COMPILER_MODE_AVALONIA,
      sourceProjectPath: path.join(tempRoot, 'Library.csproj'),
      sourceTargetPath,
      hostProjectPath: path.join(tempRoot, 'App.csproj'),
      hostTargetPath,
      hostBuildIncludesSource: true
    });

    assert.deepEqual(actual, {
      buildHost: false,
      buildSource: false,
      hostBuildIncludesSource: true
    });
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('createPreviewBuildPlan rebuilds only the source project on source-generated save refresh', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const sourceTargetPath = path.join(tempRoot, 'Library.dll');
    const hostTargetPath = path.join(tempRoot, 'App.dll');
    fs.writeFileSync(sourceTargetPath, '', 'utf8');
    fs.writeFileSync(hostTargetPath, '', 'utf8');

    const actual = createPreviewBuildPlan({
      buildReason: 'save',
      previewMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
      sourceProjectPath: path.join(tempRoot, 'Library.csproj'),
      sourceTargetPath,
      hostProjectPath: path.join(tempRoot, 'App.csproj'),
      hostTargetPath,
      hostBuildIncludesSource: true
    });

    assert.deepEqual(actual, {
      buildHost: false,
      buildSource: true,
      hostBuildIncludesSource: true
    });
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});

test('createPreviewBuildPlan rebuilds source-generated outputs on launch when the document is newer than the assembly', async () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'axsg-preview-utils-'));
  try {
    const documentPath = path.join(tempRoot, 'View.axaml');
    const sourceTargetPath = path.join(tempRoot, 'App.dll');
    fs.writeFileSync(sourceTargetPath, '', 'utf8');
    await new Promise(resolve => setTimeout(resolve, 15));
    fs.writeFileSync(documentPath, '', 'utf8');

    const actual = createPreviewBuildPlan({
      buildReason: 'launch',
      previewMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
      documentFilePath: documentPath,
      sourceProjectPath: path.join(tempRoot, 'App.csproj'),
      sourceTargetPath,
      hostProjectPath: path.join(tempRoot, 'App.csproj'),
      hostTargetPath: sourceTargetPath,
      hostBuildIncludesSource: true
    });

    assert.deepEqual(actual, {
      buildHost: true,
      buildSource: false,
      hostBuildIncludesSource: true
    });
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }
});
