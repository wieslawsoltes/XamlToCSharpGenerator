const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');

const {
  buildArguments,
  isPreviewableProjectInfo,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  resolveConfiguredProjectPath,
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

test('buildArguments adds the target framework when provided', () => {
  assert.deepEqual(buildArguments('/tmp/Demo.csproj', 'net10.0'), [
    'build',
    '/tmp/Demo.csproj',
    '-nologo',
    '-v:minimal',
    '-p:TargetFramework=net10.0'
  ]);
});
