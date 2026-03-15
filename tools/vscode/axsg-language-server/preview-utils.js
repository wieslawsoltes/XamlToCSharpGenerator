const path = require('path');
const fs = require('fs');

const PREVIEWABLE_OUTPUT_TYPES = new Set(['Exe', 'WinExe']);
const MOBILE_TARGET_FRAMEWORK_MARKERS = [
  '-android',
  '-ios',
  '-browser',
  '-wasm',
  '-tvos',
  '-maccatalyst'
];

function buildArguments(projectPath, targetFramework) {
  const args = ['build', projectPath, '-nologo', '-v:minimal'];
  if (targetFramework) {
    args.push(`-p:TargetFramework=${targetFramework}`);
  }

  return args;
}

function pickPreviewTargetFramework(targetFrameworks, preferredTargetFramework) {
  const candidates = String(targetFrameworks || '')
    .split(';')
    .map(value => value.trim())
    .filter(Boolean)
    .filter(value => !MOBILE_TARGET_FRAMEWORK_MARKERS.some(marker => value.includes(marker)));
  if (candidates.length === 0) {
    return '';
  }

  if (preferredTargetFramework && candidates.includes(preferredTargetFramework)) {
    return preferredTargetFramework;
  }

  const rankedFrameworks = ['net10.0', 'net9.0', 'net8.0', 'net7.0', 'net6.0'];
  for (const ranked of rankedFrameworks) {
    const exact = candidates.find(candidate => candidate === ranked);
    if (exact) {
      return exact;
    }
  }

  return candidates[0];
}

function normalizePreviewTargetPath(targetPath) {
  const normalized = String(targetPath || '').replace(/\\/g, '/').trim();
  if (!normalized) {
    return '/Preview.axaml';
  }

  return normalized.startsWith('/') ? normalized : `/${normalized}`;
}

function isPreviewableProjectInfo(projectInfo) {
  return Boolean(projectInfo &&
    PREVIEWABLE_OUTPUT_TYPES.has(projectInfo.outputType) &&
    projectInfo.targetPath &&
    projectInfo.previewerToolPath);
}

function resolveConfiguredProjectPath(configuredProjectPath, workspaceRoot) {
  const normalized = String(configuredProjectPath || '').trim();
  if (!normalized) {
    return '';
  }

  const resolvedPath = path.isAbsolute(normalized)
    ? normalized
    : path.join(workspaceRoot, normalized);
  if (fs.existsSync(resolvedPath) && resolvedPath.endsWith('.csproj')) {
    return path.normalize(resolvedPath);
  }

  if (fs.existsSync(resolvedPath) && fs.statSync(resolvedPath).isDirectory()) {
    const projectFiles = fs.readdirSync(resolvedPath)
      .filter(entry => entry.endsWith('.csproj'))
      .sort();
    if (projectFiles.length > 0) {
      return path.join(resolvedPath, projectFiles[0]);
    }
  }

  throw new Error(`Configured preview host project was not found: ${configuredProjectPath}`);
}

function tryParseMsbuildJson(stdout) {
  if (!stdout) {
    return null;
  }

  const trimmed = stdout.trim();
  const jsonStart = trimmed.indexOf('{');
  if (jsonStart < 0) {
    return null;
  }

  try {
    return JSON.parse(trimmed.slice(jsonStart));
  } catch {
    return null;
  }
}

function normalizeFilePath(filePath) {
  return path.normalize(path.resolve(filePath));
}

function normalizeMaybeEmptyPath(filePath) {
  if (!filePath) {
    return '';
  }

  return normalizeFilePath(filePath);
}

function samePath(left, right) {
  return normalizeFilePath(left) === normalizeFilePath(right);
}

function isUnderBuildOutput(filePath) {
  const normalized = filePath.replace(/\\/g, '/');
  return normalized.includes('/bin/') || normalized.includes('/obj/');
}

module.exports = {
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
};
