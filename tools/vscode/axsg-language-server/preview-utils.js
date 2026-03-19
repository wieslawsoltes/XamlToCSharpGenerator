const path = require('path');
const fs = require('fs');

const PREVIEWABLE_OUTPUT_TYPES = new Set(['Exe', 'WinExe']);
const PREVIEW_COMPILER_MODE_AUTO = 'auto';
const PREVIEW_COMPILER_MODE_AVALONIA = 'avalonia';
const PREVIEW_COMPILER_MODE_SOURCE_GENERATED = 'sourceGenerated';
const VALID_PREVIEW_COMPILER_MODES = new Set([
  PREVIEW_COMPILER_MODE_AUTO,
  PREVIEW_COMPILER_MODE_AVALONIA,
  PREVIEW_COMPILER_MODE_SOURCE_GENERATED
]);
const SOURCE_GENERATED_RUNTIME_ASSEMBLY_NAME = 'XamlToCSharpGenerator.Runtime.Avalonia.dll';
const SOURCE_GENERATED_RUNTIME_LIBRARY_NAME = 'XamlToCSharpGenerator.Runtime.Avalonia';
const SOURCE_GENERATED_LIVE_PREVIEW_MARKER = 'SourceGenPreviewMarkupRuntime';
const PREVIEW_ASSEMBLY_SIDECAR_EXTENSIONS = ['.pdb', '.deps.json', '.runtimeconfig.json'];
const MOBILE_TARGET_FRAMEWORK_MARKERS = [
  '-android',
  '-ios',
  '-browser',
  '-wasm',
  '-tvos',
  '-maccatalyst'
];
const DEFAULT_PREVIEW_SCALE = 1;

function buildArguments(projectPath, targetFramework, options = {}) {
  const args = ['build', projectPath, '-nologo', '-v:minimal'];
  if (options.skipRestore) {
    args.push('--no-restore');
  }

  if (targetFramework) {
    args.push(`-p:TargetFramework=${targetFramework}`);
  }

  return args;
}

function appendCommandOutputText(existingText, nextText) {
  const normalizedNextText = String(nextText || '').trim();
  if (!normalizedNextText) {
    return existingText;
  }

  return existingText
    ? `${existingText}\n${normalizedNextText}`
    : normalizedNextText;
}

function createCommandFailureMessage(command, args, stdoutText, stderrText, exitCode) {
  let diagnostics = '';
  diagnostics = appendCommandOutputText(diagnostics, stderrText);
  diagnostics = appendCommandOutputText(diagnostics, stdoutText);
  if (diagnostics) {
    return diagnostics;
  }

  return `Command '${command} ${args.join(' ')}' failed with exit code ${exitCode}.`;
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

function normalizePreviewCompilerMode(mode) {
  const normalized = String(mode || '').trim();
  if (VALID_PREVIEW_COMPILER_MODES.has(normalized)) {
    return normalized;
  }

  return PREVIEW_COMPILER_MODE_AUTO;
}

function hasPendingPreviewText(value) {
  return value !== null && value !== undefined;
}

function isExecutableProjectInfo(projectInfo) {
  return Boolean(projectInfo &&
    PREVIEWABLE_OUTPUT_TYPES.has(projectInfo.outputType) &&
    projectInfo.targetPath);
}

function isPreviewableProjectInfo(projectInfo) {
  return Boolean(projectInfo &&
    isExecutableProjectInfo(projectInfo) &&
    projectInfo.previewerToolPath);
}

function isUsablePreviewHostProjectInfo(projectInfo, sourceProjectInfo, configuredMode) {
  return isExecutableProjectInfo(projectInfo);
}

function isResolvablePreviewHostProjectInfo(projectInfo, sourceProjectInfo, configuredMode, allowAutoExecutableFallback = false) {
  if (isUsablePreviewHostProjectInfo(projectInfo, sourceProjectInfo, configuredMode)) {
    return true;
  }

  return Boolean(
    allowAutoExecutableFallback &&
    normalizePreviewCompilerMode(configuredMode) === PREVIEW_COMPILER_MODE_AUTO &&
    isExecutableProjectInfo(projectInfo));
}

function supportsSourceGeneratedPreview(projectInfo) {
  const targetPath = normalizeMaybeEmptyPath(projectInfo && projectInfo.targetPath);
  if (!targetPath) {
    return false;
  }

  const outputDirectory = path.dirname(targetPath);
  if (fs.existsSync(path.join(outputDirectory, SOURCE_GENERATED_RUNTIME_ASSEMBLY_NAME))) {
    return true;
  }

  const depsPath = path.join(
    outputDirectory,
    `${path.basename(targetPath, path.extname(targetPath))}.deps.json`);
  if (!fs.existsSync(depsPath)) {
    return false;
  }

  try {
    const parsed = JSON.parse(fs.readFileSync(depsPath, 'utf8'));
    const libraries = parsed && parsed.libraries && typeof parsed.libraries === 'object'
      ? Object.keys(parsed.libraries)
      : [];
    return libraries.some(library =>
      library === SOURCE_GENERATED_RUNTIME_LIBRARY_NAME ||
      library.startsWith(`${SOURCE_GENERATED_RUNTIME_LIBRARY_NAME}/`));
  } catch {
    return false;
  }
}

function supportsSourceGeneratedLivePreview(projectInfo) {
  const targetPath = normalizeMaybeEmptyPath(projectInfo && projectInfo.targetPath);
  if (!targetPath) {
    return false;
  }

  const runtimeAssemblyPath = path.join(path.dirname(targetPath), SOURCE_GENERATED_RUNTIME_ASSEMBLY_NAME);
  if (!fs.existsSync(runtimeAssemblyPath)) {
    return false;
  }

  try {
    const assemblyBytes = fs.readFileSync(runtimeAssemblyPath);
    return assemblyBytes.includes(Buffer.from(SOURCE_GENERATED_LIVE_PREVIEW_MARKER, 'utf8'));
  } catch {
    return false;
  }
}

function resolvePreviewCompilerMode(configuredMode, sourceProjectInfo) {
  const requestedMode = normalizePreviewCompilerMode(configuredMode);
  const sourceGeneratedSupported = supportsSourceGeneratedPreview(sourceProjectInfo);
  const sourceGeneratedLivePreviewSupported = supportsSourceGeneratedLivePreview(sourceProjectInfo);

  if (requestedMode === PREVIEW_COMPILER_MODE_AVALONIA) {
    return {
      requestedMode,
      preferredMode: PREVIEW_COMPILER_MODE_AVALONIA,
      sourceGeneratedSupported,
      sourceGeneratedLivePreviewSupported
    };
  }

  if (requestedMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
    return {
      requestedMode,
      preferredMode: PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
      sourceGeneratedSupported,
      sourceGeneratedLivePreviewSupported
    };
  }

  return {
    requestedMode,
    preferredMode: sourceGeneratedLivePreviewSupported
      ? PREVIEW_COMPILER_MODE_SOURCE_GENERATED
      : PREVIEW_COMPILER_MODE_AVALONIA,
    sourceGeneratedSupported,
    sourceGeneratedLivePreviewSupported
  };
}

function resolvePreviewBuildMode(configuredMode, sourceProjectInfo) {
  const requestedMode = normalizePreviewCompilerMode(configuredMode);
  if (requestedMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
    return PREVIEW_COMPILER_MODE_SOURCE_GENERATED;
  }

  if (requestedMode === PREVIEW_COMPILER_MODE_AVALONIA) {
    return PREVIEW_COMPILER_MODE_AVALONIA;
  }

  return supportsSourceGeneratedPreview(sourceProjectInfo)
    ? PREVIEW_COMPILER_MODE_SOURCE_GENERATED
    : PREVIEW_COMPILER_MODE_AVALONIA;
}

function resolveEffectivePreviewMode(previewPlan) {
  if (!previewPlan || typeof previewPlan !== 'object') {
    return PREVIEW_COMPILER_MODE_AVALONIA;
  }

  return previewPlan.resolvedMode || previewPlan.preferredMode || PREVIEW_COMPILER_MODE_AVALONIA;
}

function resolvePreviewDocumentText(documentText, persistedText, isDirty, previewMode) {
  return documentText;
}

function shouldUseInlineLoopbackPreviewClient(remoteName, uiKind) {
  return !String(remoteName || '').trim() && Number(uiKind) === 1;
}

function extractPreviewSecurityCookie(previewHtml) {
  const match = String(previewHtml || '')
    .match(/avaloniaPreviewerSecurityCookie"\]\s*=\s*"([^"]+)"/);
  return match ? match[1] : '';
}

function resolveLoopbackPreviewWebviewTarget(previewUrl) {
  const normalized = String(previewUrl || '').trim();
  if (!normalized) {
    return null;
  }

  let parsedUrl;
  try {
    parsedUrl = new URL(normalized);
  } catch {
    return null;
  }

  if (parsedUrl.protocol !== 'http:' && parsedUrl.protocol !== 'https:') {
    return null;
  }

  const hostname = parsedUrl.hostname.toLowerCase();
  if (hostname !== '127.0.0.1' && hostname !== 'localhost' && hostname !== '::1' && hostname !== '[::1]') {
    return null;
  }

  const port = Number.parseInt(parsedUrl.port, 10);
  if (!Number.isInteger(port) || port <= 0) {
    return null;
  }

  return {
    previewUrl: parsedUrl.toString(),
    webSocketUrl: `${parsedUrl.protocol === 'https:' ? 'wss:' : 'ws:'}//localhost:${port}/ws`,
    port
  };
}

function normalizePreviewViewportMetrics(width, height, scale) {
  const normalizedWidth = Number(width);
  const normalizedHeight = Number(height);
  if (!Number.isFinite(normalizedWidth) ||
      !Number.isFinite(normalizedHeight) ||
      normalizedWidth <= 0 ||
      normalizedHeight <= 0) {
    return null;
  }

  return {
    width: Math.max(1, Math.round(normalizedWidth)),
    height: Math.max(1, Math.round(normalizedHeight)),
    scale: normalizePreviewScale(scale)
  };
}

function getPreviewViewportMetricsKey(previewMetrics) {
  if (!previewMetrics ||
      !Number.isFinite(previewMetrics.width) ||
      !Number.isFinite(previewMetrics.height) ||
      !Number.isFinite(previewMetrics.scale) ||
      previewMetrics.scale <= 0) {
    return '';
  }

  return `${previewMetrics.width}x${previewMetrics.height}@${previewMetrics.scale.toFixed(3)}`;
}

function normalizePreviewScale(scale) {
  const normalizedScale = Number(scale);
  if (!Number.isFinite(normalizedScale) || normalizedScale <= 0) {
    return DEFAULT_PREVIEW_SCALE;
  }

  return Math.round(normalizedScale * 1000) / 1000;
}

function resolveAvaloniaPreviewerToolPaths(hasBundledDesignerHost, bundledDesignerHostPath, projectPreviewerToolPath) {
  const orderedPaths = [];
  const seenPaths = new Set();

  function tryAdd(candidatePath) {
    const normalizedPath = normalizeMaybeEmptyPath(candidatePath);
    if (!normalizedPath || seenPaths.has(normalizedPath)) {
      return;
    }

    seenPaths.add(normalizedPath);
    orderedPaths.push(normalizedPath);
  }

  if (hasBundledDesignerHost) {
    tryAdd(bundledDesignerHostPath);
  }

  tryAdd(projectPreviewerToolPath);
  return orderedPaths;
}

function resolvePreviewDesignAssemblyPath(sourceTargetPath, hostTargetPath, hostReferencesSource) {
  const normalizedSourceTargetPath = normalizeMaybeEmptyPath(sourceTargetPath);
  if (!normalizedSourceTargetPath) {
    return '';
  }

  const normalizedHostTargetPath = normalizeMaybeEmptyPath(hostTargetPath);
  if (!hostReferencesSource || !normalizedHostTargetPath || samePath(normalizedSourceTargetPath, normalizedHostTargetPath)) {
    return normalizedSourceTargetPath;
  }

  return normalizeFilePath(path.join(
    path.dirname(normalizedHostTargetPath),
    path.basename(normalizedSourceTargetPath)));
}

function enumeratePreviewAssemblyArtifacts(sourceAssemblyPath, previewAssemblyPath) {
  const normalizedSourceAssemblyPath = normalizeMaybeEmptyPath(sourceAssemblyPath);
  const normalizedPreviewAssemblyPath = normalizeMaybeEmptyPath(previewAssemblyPath);
  if (!normalizedSourceAssemblyPath || !normalizedPreviewAssemblyPath || !fs.existsSync(normalizedSourceAssemblyPath)) {
    return [];
  }

  const artifacts = [{
    sourcePath: normalizedSourceAssemblyPath,
    targetPath: normalizedPreviewAssemblyPath
  }];

  for (const extension of PREVIEW_ASSEMBLY_SIDECAR_EXTENSIONS) {
    const sourceSidecarPath = replaceFileExtension(normalizedSourceAssemblyPath, extension);
    if (!fs.existsSync(sourceSidecarPath)) {
      continue;
    }

    artifacts.push({
      sourcePath: sourceSidecarPath,
      targetPath: replaceFileExtension(normalizedPreviewAssemblyPath, extension)
    });
  }

  return artifacts;
}

function createPreviewStartPlan(options) {
  const requestedMode = options && options.requestedMode
    ? options.requestedMode
    : PREVIEW_COMPILER_MODE_AUTO;
  const preferredMode = options && options.preferredMode
    ? options.preferredMode
    : PREVIEW_COMPILER_MODE_AVALONIA;
  const hasBundledDesignerHost = Boolean(options && options.hasBundledDesignerHost);
  const hasAvaloniaPreviewer = Boolean(options && options.hasAvaloniaPreviewer);
  const hasAvaloniaHost = hasBundledDesignerHost || hasAvaloniaPreviewer;
  const modes = [];

  if (preferredMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
    if (hasBundledDesignerHost) {
      modes.push(PREVIEW_COMPILER_MODE_SOURCE_GENERATED);
    } else if (requestedMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
      return {
        modes,
        requiresBundledDesignerHost: true,
        requiresAvaloniaPreviewer: false
      };
    }

    if (requestedMode !== PREVIEW_COMPILER_MODE_AUTO) {
      return {
        modes,
        requiresBundledDesignerHost: false,
        requiresAvaloniaPreviewer: false
      };
    }

    if (hasAvaloniaHost) {
      modes.push(PREVIEW_COMPILER_MODE_AVALONIA);
    }

    return {
      modes,
      requiresBundledDesignerHost: false,
      requiresAvaloniaPreviewer: false
    };
  }

  if (!hasAvaloniaHost) {
    return {
      modes,
      requiresBundledDesignerHost: false,
      requiresAvaloniaPreviewer: true
    };
  }

  return {
    modes: [PREVIEW_COMPILER_MODE_AVALONIA],
    requiresBundledDesignerHost: false,
    requiresAvaloniaPreviewer: false
  };
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

function normalizePathForComparison(filePath) {
  const normalizedPath = normalizeMaybeEmptyPath(filePath);
  if (!normalizedPath) {
    return '';
  }

  return process.platform === 'win32'
    ? normalizedPath.toLowerCase()
    : normalizedPath;
}

function samePath(left, right) {
  return normalizePathForComparison(left) === normalizePathForComparison(right);
}

function getFileModifiedTimeMs(filePath) {
  try {
    return fs.statSync(filePath).mtimeMs;
  } catch {
    return 0;
  }
}

function isInputNewerThanOutput(inputPath, outputPath) {
  if (!inputPath || !outputPath) {
    return false;
  }

  const outputTime = getFileModifiedTimeMs(outputPath);
  if (outputTime <= 0) {
    return true;
  }

  return getFileModifiedTimeMs(inputPath) > outputTime;
}

function getProjectAssetsPath(projectPath) {
  return path.join(path.dirname(projectPath), 'obj', 'project.assets.json');
}

function shouldUseNoRestoreBuild(projectPath) {
  return fs.existsSync(getProjectAssetsPath(projectPath));
}

function projectReferencesProject(projectPath, referencedProjectPath, visited = new Set()) {
  const normalizedProjectPath = normalizeMaybeEmptyPath(projectPath);
  const normalizedReferencedPath = normalizeMaybeEmptyPath(referencedProjectPath);
  if (!normalizedProjectPath || !normalizedReferencedPath || visited.has(normalizedProjectPath)) {
    return false;
  }

  visited.add(normalizedProjectPath);

  let contents;
  try {
    contents = fs.readFileSync(normalizedProjectPath, 'utf8');
  } catch {
    return false;
  }

  const projectReferencePattern = /<ProjectReference\b[^>]*Include\s*=\s*"([^"]+)"/gi;
  let match;
  while ((match = projectReferencePattern.exec(contents)) !== null) {
    const includePath = String(match[1] || '').trim();
    if (!includePath) {
      continue;
    }

    const resolvedReference = normalizeFilePath(path.resolve(path.dirname(normalizedProjectPath), includePath));
    if (samePath(resolvedReference, normalizedReferencedPath)) {
      return true;
    }

    if (projectReferencesProject(resolvedReference, normalizedReferencedPath, visited)) {
      return true;
    }
  }

  return false;
}

function createPreviewBuildPlan(options) {
  const previewMode = options.previewMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED
    ? PREVIEW_COMPILER_MODE_SOURCE_GENERATED
    : PREVIEW_COMPILER_MODE_AVALONIA;
  const buildReason = String(options.buildReason || 'launch');
  const sourceProjectPath = normalizeMaybeEmptyPath(options.sourceProjectPath);
  const hostProjectPath = normalizeMaybeEmptyPath(options.hostProjectPath);
  const sourceTargetPath = normalizeMaybeEmptyPath(options.sourceTargetPath);
  const hostTargetPath = normalizeMaybeEmptyPath(options.hostTargetPath);
  const sameProject = sourceProjectPath && hostProjectPath && samePath(sourceProjectPath, hostProjectPath);
  const hostBuildIncludesSource = Boolean(options.hostBuildIncludesSource || sameProject);
  const requiresSourceGeneratedCapabilityRefresh = Boolean(options.requiresSourceGeneratedCapabilityRefresh);
  const sourceOutputMissing = !sourceTargetPath || !fs.existsSync(sourceTargetPath);
  const hostOutputMissing = !hostTargetPath || !fs.existsSync(hostTargetPath);
  const documentChangedSinceBuild = isInputNewerThanOutput(options.documentFilePath, sourceTargetPath);
  const sourceBuildRequired = sourceOutputMissing || documentChangedSinceBuild;

  if (previewMode === PREVIEW_COMPILER_MODE_SOURCE_GENERATED) {
    if (buildReason === 'save') {
      if (sameProject) {
        return {
          buildHost: true,
          buildSource: false,
          hostBuildIncludesSource: true
        };
      }

      if (hostOutputMissing) {
        return {
          buildHost: true,
          buildSource: !hostBuildIncludesSource,
          hostBuildIncludesSource
        };
      }

      return {
        buildHost: false,
        buildSource: true,
        hostBuildIncludesSource
      };
    }

    const buildSource = sourceBuildRequired || requiresSourceGeneratedCapabilityRefresh;
    const buildHost = hostOutputMissing || (sameProject && buildSource);
    return {
      buildHost,
      buildSource: buildSource && !(buildHost && hostBuildIncludesSource),
      hostBuildIncludesSource
    };
  }

  if (sameProject) {
    return {
      buildHost: hostOutputMissing || sourceBuildRequired,
      buildSource: false,
      hostBuildIncludesSource: true
    };
  }

  const buildHost = hostOutputMissing || (hostBuildIncludesSource && sourceBuildRequired);
  return {
    buildHost,
    buildSource: sourceBuildRequired && !(buildHost && hostBuildIncludesSource),
    hostBuildIncludesSource
  };
}

function isUnderBuildOutput(filePath) {
  const normalized = filePath.replace(/\\/g, '/');
  return normalized.includes('/bin/') || normalized.includes('/obj/');
}

function replaceFileExtension(filePath, nextExtension) {
  const parsedPath = path.parse(filePath);
  return path.join(parsedPath.dir, parsedPath.name + nextExtension);
}

function resolvePreviewHostRuntimePaths(previewerToolPath, hostAssemblyPath, useHostAssemblyRuntime = false) {
  const runtimeBasePath = normalizeMaybeEmptyPath(useHostAssemblyRuntime
    ? hostAssemblyPath
    : previewerToolPath);
  if (!runtimeBasePath) {
    return {
      runtimeConfigPath: '',
      depsFilePath: ''
    };
  }

  return {
    runtimeConfigPath: replaceFileExtension(runtimeBasePath, '.runtimeconfig.json'),
    depsFilePath: replaceFileExtension(runtimeBasePath, '.deps.json')
  };
}

module.exports = {
  createCommandFailureMessage,
  buildArguments,
  createPreviewBuildPlan,
  enumeratePreviewAssemblyArtifacts,
  resolveAvaloniaPreviewerToolPaths,
  resolvePreviewDesignAssemblyPath,
  resolvePreviewHostRuntimePaths,
  getPreviewViewportMetricsKey,
  resolveLoopbackPreviewWebviewTarget,
  createPreviewStartPlan,
  extractPreviewSecurityCookie,
  getFileModifiedTimeMs,
  hasPendingPreviewText,
  isExecutableProjectInfo,
  isInputNewerThanOutput,
  isPreviewableProjectInfo,
  isResolvablePreviewHostProjectInfo,
  isUsablePreviewHostProjectInfo,
  isUnderBuildOutput,
  normalizeFilePath,
  normalizePreviewCompilerMode,
  normalizePreviewViewportMetrics,
  normalizeMaybeEmptyPath,
  normalizePreviewTargetPath,
  pickPreviewTargetFramework,
  PREVIEW_COMPILER_MODE_AUTO,
  PREVIEW_COMPILER_MODE_AVALONIA,
  PREVIEW_COMPILER_MODE_SOURCE_GENERATED,
  projectReferencesProject,
  resolveConfiguredProjectPath,
  resolveEffectivePreviewMode,
  resolvePreviewBuildMode,
  resolvePreviewDocumentText,
  resolvePreviewCompilerMode,
  samePath,
  shouldUseInlineLoopbackPreviewClient,
  shouldUseNoRestoreBuild,
  supportsSourceGeneratedLivePreview,
  supportsSourceGeneratedPreview,
  tryParseMsbuildJson
};
