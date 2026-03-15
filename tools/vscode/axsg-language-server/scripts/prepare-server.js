const path = require('path');
const fs = require('fs');
const cp = require('child_process');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..', '..');
const projectPath = path.join(
  repoRoot,
  'src',
  'XamlToCSharpGenerator.LanguageServer',
  'XamlToCSharpGenerator.LanguageServer.csproj'
);
const previewHostProjectPath = path.join(
  repoRoot,
  'src',
  'XamlToCSharpGenerator.PreviewerHost',
  'XamlToCSharpGenerator.PreviewerHost.csproj'
);
const outputDir = path.join(extensionRoot, 'server');
const outputAssembly = path.join(outputDir, 'XamlToCSharpGenerator.LanguageServer.dll');
const previewHostOutputDir = path.join(extensionRoot, 'preview-host');
const previewHostAssembly = path.join(previewHostOutputDir, 'XamlToCSharpGenerator.PreviewerHost.dll');

if (!fs.existsSync(projectPath)) {
  throw new Error(`Language server project not found: ${projectPath}`);
}

if (!fs.existsSync(previewHostProjectPath)) {
  throw new Error(`Preview host project not found: ${previewHostProjectPath}`);
}

fs.rmSync(outputDir, { recursive: true, force: true });
fs.rmSync(previewHostOutputDir, { recursive: true, force: true });

const args = [
  'publish',
  projectPath,
  '-c',
  'Release',
  '-f',
  'net10.0',
  '-o',
  outputDir,
  '-p:UseAppHost=false',
  '-p:DebugType=None',
  '-p:DebugSymbols=false'
];

console.log(`[axsg-language-server] Publishing bundled server from ${projectPath}`);
cp.execFileSync('dotnet', args, {
  cwd: repoRoot,
  stdio: 'inherit'
});

if (!fs.existsSync(outputAssembly)) {
  throw new Error(`Bundled server entry assembly was not produced: ${outputAssembly}`);
}

console.log(`[axsg-language-server] Bundled server ready at ${outputAssembly}`);

const previewHostArgs = [
  'publish',
  previewHostProjectPath,
  '-c',
  'Release',
  '-f',
  'net10.0',
  '-o',
  previewHostOutputDir,
  '-p:UseAppHost=false',
  '-p:DebugType=None',
  '-p:DebugSymbols=false'
];

console.log(`[axsg-language-server] Publishing preview host from ${previewHostProjectPath}`);
cp.execFileSync('dotnet', previewHostArgs, {
  cwd: repoRoot,
  stdio: 'inherit'
});

if (!fs.existsSync(previewHostAssembly)) {
  throw new Error(`Bundled preview host assembly was not produced: ${previewHostAssembly}`);
}

console.log(`[axsg-language-server] Bundled preview host ready at ${previewHostAssembly}`);
