const path = require('path');
const fs = require('fs');
const vscode = require('vscode');
const lc = require('vscode-languageclient/node');
const { AvaloniaPreviewController } = require('./preview-support');
const { DesignSessionController } = require('./design-support');

let client;
let clientStartPromise;
let extensionContext;
let resolvedServerStartup;
let resolvedClientOptions;
let outputChannel;
let statusBarItem;
const AXSG_METADATA_SCHEME = 'axsg-metadata';
const AXSG_SOURCELINK_SCHEME = 'axsg-sourcelink';
const AXSG_INLINE_CSHARP_SCHEME = 'virtualCSharp-axsg-inline';
const metadataDocumentCache = new Map();
const metadataUriSubscriptions = new Map();
let metadataChangeEmitter;
const sourceLinkDocumentCache = new Map();
const sourceLinkUriSubscriptions = new Map();
let sourceLinkChangeEmitter;
const inlineCSharpProjectionCache = new Map();
const inlineCSharpProjectionFetches = new Map();
const inlineCSharpProjectionUriCache = new Map();
const inlineCSharpPresenceCache = new Map();
let inlineCSharpProjectionChangeEmitter;
const AXSG_REFACTOR_RENAME_KIND = new vscode.CodeActionKind('refactor.rename');
const VIRTUAL_LOADING_DOCUMENT_MIN_LINES = 256;
const VIRTUAL_LOADING_DOCUMENT_MIN_COLUMNS = 256;
let suppressCSharpRenameProvider = false;
let previewController;
let designController;
let runtimeGeneration = 0;
let shutdownPromise;

function decodeQueryValue(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function encodeQueryValue(value) {
  return encodeURIComponent(String(value ?? ''));
}

function renderMetadataDocument(uri) {
  const query = new URLSearchParams(uri.query || '');
  const documentId = query.get('id');
  if (documentId) {
    const decodedDocumentId = decodeQueryValue(documentId);
    trackMetadataUri(decodedDocumentId, uri);

    const cached = metadataDocumentCache.get(decodedDocumentId);
    if (cached && cached.state !== 'loading') {
      return cached.text;
    }

    if (!cached) {
      const loadingText = createMetadataLoadingDocument(query);
      metadataDocumentCache.set(decodedDocumentId, { state: 'loading', text: loadingText });
      void fetchAndCacheMetadataDocument(decodedDocumentId, uri);
      return loadingText;
    }

    return cached.text;
  }

  return renderMetadataProjectionFallback(query);
}

function getOutputChannel() {
  outputChannel = outputChannel ?? vscode.window.createOutputChannel('AXSG Language Server');
  return outputChannel;
}

function beginRuntimeGeneration() {
  runtimeGeneration += 1;
  return runtimeGeneration;
}

function isRuntimeGenerationCurrent(generation) {
  return generation === runtimeGeneration;
}

function clearInMemoryCaches() {
  metadataDocumentCache.clear();
  metadataUriSubscriptions.clear();
  sourceLinkDocumentCache.clear();
  sourceLinkUriSubscriptions.clear();
  inlineCSharpProjectionCache.clear();
  inlineCSharpProjectionFetches.clear();
  inlineCSharpProjectionUriCache.clear();
  inlineCSharpPresenceCache.clear();
  suppressCSharpRenameProvider = false;
}

async function shutdownExtensionRuntime() {
  if (shutdownPromise) {
    return shutdownPromise;
  }

  beginRuntimeGeneration();

  const existingPreviewController = previewController;
  const existingDesignController = designController;
  const existingClient = client;
  const existingStatusBarItem = statusBarItem;
  const existingSourceLinkChangeEmitter = sourceLinkChangeEmitter;
  const existingMetadataChangeEmitter = metadataChangeEmitter;
  const existingInlineCSharpProjectionChangeEmitter = inlineCSharpProjectionChangeEmitter;
  const existingOutputChannel = outputChannel;

  previewController = undefined;
  designController = undefined;
  client = undefined;
  clientStartPromise = undefined;
  statusBarItem = undefined;
  sourceLinkChangeEmitter = undefined;
  metadataChangeEmitter = undefined;
  inlineCSharpProjectionChangeEmitter = undefined;
  resolvedClientOptions = undefined;
  resolvedServerStartup = undefined;
  extensionContext = undefined;
  clearInMemoryCaches();

  shutdownPromise = (async () => {
    if (existingPreviewController) {
      try {
        await existingPreviewController.dispose?.();
      } catch {
        // Best effort shutdown.
      }
    }

    try {
      existingDesignController?.dispose?.();
    } catch {
      // Best effort shutdown.
    }

    if (existingClient) {
      try {
        await existingClient.stop();
      } catch {
        // Best effort shutdown.
      }
    }

    try {
      existingStatusBarItem?.dispose();
    } catch {
      // Best effort shutdown.
    }

    try {
      existingSourceLinkChangeEmitter?.dispose();
    } catch {
      // Best effort shutdown.
    }

    try {
      existingMetadataChangeEmitter?.dispose();
    } catch {
      // Best effort shutdown.
    }

    try {
      existingInlineCSharpProjectionChangeEmitter?.dispose();
    } catch {
      // Best effort shutdown.
    }

    try {
      existingOutputChannel?.dispose();
    } catch {
      // Best effort shutdown.
    }

    outputChannel = undefined;
  })();

  try {
    await shutdownPromise;
  } finally {
    shutdownPromise = undefined;
  }
}

function renderMetadataProjectionFallback(query) {
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

function createMetadataLoadingDocument(query) {
  const baseDocument = renderMetadataProjectionFallback(query);
  return padVirtualLoadingDocument(baseDocument);
}

function updateMetadataCacheAndNotify(documentId, state, text) {
  metadataDocumentCache.set(documentId, { state, text });

  const subscribers = metadataUriSubscriptions.get(documentId);
  if (!subscribers || !metadataChangeEmitter) {
    return;
  }

  for (const uriString of subscribers) {
    try {
      metadataChangeEmitter.fire(vscode.Uri.parse(uriString));
    } catch {
      // Ignore malformed URI entries.
    }
  }
}

function trackMetadataUri(documentId, uri) {
  let subscribers = metadataUriSubscriptions.get(documentId);
  if (!subscribers) {
    subscribers = new Set();
    metadataUriSubscriptions.set(documentId, subscribers);
  }

  subscribers.add(uri.toString());
}

async function fetchAndCacheMetadataDocument(documentId, uri) {
  const generation = runtimeGeneration;
  const cached = metadataDocumentCache.get(documentId);
  if (cached && cached.state !== 'loading') {
    return;
  }

  const activeClient = await tryEnsureClientStarted();
  if (!isRuntimeGenerationCurrent(generation)) {
    return;
  }

  if (!activeClient) {
    updateMetadataCacheAndNotify(documentId, 'error', renderMetadataProjectionFallback(new URLSearchParams(uri.query || '')));
    return;
  }

  try {
    const response = await activeClient.sendRequest('axsg/metadataDocument', { id: documentId });
    if (!isRuntimeGenerationCurrent(generation)) {
      return;
    }

    if (!response || typeof response.text !== 'string' || response.text.length === 0) {
      updateMetadataCacheAndNotify(documentId, 'error', padVirtualLoadingDocument(renderMetadataProjectionFallback(new URLSearchParams(uri.query || ''))));
      return;
    }

    updateMetadataCacheAndNotify(documentId, 'ready', response.text);
  } catch {
    if (!isRuntimeGenerationCurrent(generation)) {
      return;
    }

    updateMetadataCacheAndNotify(documentId, 'error', padVirtualLoadingDocument(renderMetadataProjectionFallback(new URLSearchParams(uri.query || ''))));
  }
}

function createSourceLinkLoadingDocument(sourceUrl) {
  return padVirtualLoadingDocument(`// AXSG source-link projection\n// Loading source from ${sourceUrl}...\n`);
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
  const generation = runtimeGeneration;
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
      if (!isRuntimeGenerationCurrent(generation)) {
        return;
      }

      const failure = padVirtualLoadingDocument(`// AXSG source-link projection\n// Failed to load source from ${sourceUrl}.\n// HTTP ${response.status} ${response.statusText}\n`);
      updateSourceLinkCacheAndNotify(sourceUrl, 'error', failure);
      return;
    }

    const text = await response.text();
    if (!isRuntimeGenerationCurrent(generation)) {
      return;
    }

    updateSourceLinkCacheAndNotify(sourceUrl, 'ready', text);
  } catch (error) {
    if (!isRuntimeGenerationCurrent(generation)) {
      return;
    }

    const message = error instanceof Error ? error.message : String(error);
    const failure = padVirtualLoadingDocument(`// AXSG source-link projection\n// Failed to load source from ${sourceUrl}.\n// ${message}\n`);
    updateSourceLinkCacheAndNotify(sourceUrl, 'error', failure);
  }
}

function padVirtualLoadingDocument(text) {
  const lines = String(text || '').replace(/\r\n/g, '\n').split('\n');
  const paddedLines = lines.map((line) => line.length >= VIRTUAL_LOADING_DOCUMENT_MIN_COLUMNS
    ? line
    : line + ' '.repeat(VIRTUAL_LOADING_DOCUMENT_MIN_COLUMNS - line.length));

  while (paddedLines.length < VIRTUAL_LOADING_DOCUMENT_MIN_LINES) {
    paddedLines.push(' '.repeat(VIRTUAL_LOADING_DOCUMENT_MIN_COLUMNS));
  }

  return `${paddedLines.join('\n')}\n`;
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

function createInlineCSharpLoadingDocument(uri) {
  const query = new URLSearchParams(uri.query || '');
  const sourceUri = decodeQueryValue(query.get('sourceUri') || '');
  return padVirtualLoadingDocument(`// AXSG inline C# projection\n// Loading projected C# for ${sourceUri || '<unknown source>'}...\n`);
}

function updateInlineCSharpProjectionCache(cacheEntry) {
  inlineCSharpProjectionCache.set(cacheEntry.cacheKey, cacheEntry);
  for (const projection of cacheEntry.projections) {
    inlineCSharpProjectionUriCache.set(projection.uri.toString(), {
      cacheKey: cacheEntry.cacheKey,
      sourceUri: cacheEntry.sourceUri,
      version: cacheEntry.version,
      sourceText: cacheEntry.sourceText,
      projection
    });
    if (inlineCSharpProjectionChangeEmitter) {
      inlineCSharpProjectionChangeEmitter.fire(projection.uri);
    }
  }
}

function buildInlineCSharpProjectionUri(sourceUri, version, projectionId) {
  const query = new URLSearchParams();
  query.set('sourceUri', encodeQueryValue(sourceUri));
  query.set('version', String(version));
  query.set('id', encodeQueryValue(projectionId));

  return vscode.Uri.from({
    scheme: AXSG_INLINE_CSHARP_SCHEME,
    authority: 'axsg-inline',
    path: `/${projectionId}.cs`,
    query: query.toString()
  });
}

function createInlineCSharpProjectionCacheKey(sourceUri, version) {
  return `${sourceUri}::${version}`;
}

function documentMayContainInlineCSharp(document) {
  if (!isXamlDocument(document)) {
    return false;
  }

  const cacheKey = createInlineCSharpProjectionCacheKey(document.uri.toString(), document.version ?? 0);
  const cached = inlineCSharpPresenceCache.get(cacheKey);
  if (typeof cached === 'boolean') {
    return cached;
  }

  const text = document.getText();
  const containsInlineCSharp = text.includes('CSharp');
  inlineCSharpPresenceCache.set(cacheKey, containsInlineCSharp);
  return containsInlineCSharp;
}

function parseInlineCSharpProjectionUri(uri) {
  const query = new URLSearchParams(uri.query || '');
  const sourceUri = decodeQueryValue(query.get('sourceUri') || '');
  const versionValue = Number.parseInt(query.get('version') || '0', 10);
  const projectionId = decodeQueryValue(query.get('id') || '');

  if (!sourceUri || !projectionId || !Number.isFinite(versionValue)) {
    return undefined;
  }

  return {
    sourceUri,
    version: versionValue,
    projectionId,
    cacheKey: createInlineCSharpProjectionCacheKey(sourceUri, versionValue)
  };
}

function cloneRange(range) {
  return new vscode.Range(
    range.start.line,
    range.start.character,
    range.end.line,
    range.end.character
  );
}

function comparePositions(left, right) {
  if (left.line !== right.line) {
    return left.line - right.line;
  }

  return left.character - right.character;
}

function containsPosition(range, position) {
  return comparePositions(position, range.start) >= 0 && comparePositions(position, range.end) <= 0;
}

function intersectsRanges(left, right) {
  return comparePositions(left.end, right.start) >= 0 && comparePositions(right.end, left.start) >= 0;
}

function offsetAt(text, position) {
  const normalizedText = String(text || '').replace(/\r\n/g, '\n');
  let line = 0;
  let character = 0;

  for (let index = 0; index < normalizedText.length; index++) {
    if (line === position.line && character === position.character) {
      return index;
    }

    if (normalizedText[index] === '\n') {
      line++;
      character = 0;
      if (line > position.line) {
        return index + 1;
      }
    } else {
      character++;
    }
  }

  return normalizedText.length;
}

function positionAt(text, offset) {
  const normalizedText = String(text || '').replace(/\r\n/g, '\n');
  const boundedOffset = Math.max(0, Math.min(offset, normalizedText.length));
  let line = 0;
  let character = 0;

  for (let index = 0; index < boundedOffset; index++) {
    if (normalizedText[index] === '\n') {
      line++;
      character = 0;
    } else {
      character++;
    }
  }

  return new vscode.Position(line, character);
}

function mapProjectedPositionToXamlPosition(sourceText, projection, projectedPosition) {
  const projectedCodeStart = offsetAt(projection.projectedText, projection.projectedCodeRange.start);
  const projectedCodeEnd = offsetAt(projection.projectedText, projection.projectedCodeRange.end);
  const projectedOffset = offsetAt(projection.projectedText, projectedPosition);
  if (projectedOffset < projectedCodeStart || projectedOffset > projectedCodeEnd) {
    return undefined;
  }

  const xamlCodeStart = offsetAt(sourceText, projection.xamlRange.start);
  return positionAt(sourceText, xamlCodeStart + (projectedOffset - projectedCodeStart));
}

function mapProjectedRangeToXamlRange(sourceText, projection, projectedRange) {
  const start = mapProjectedPositionToXamlPosition(sourceText, projection, projectedRange.start);
  const end = mapProjectedPositionToXamlPosition(sourceText, projection, projectedRange.end);
  if (!start || !end) {
    return undefined;
  }

  return new vscode.Range(start, end);
}

function mapXamlPositionToProjectedPosition(sourceText, projection, xamlPosition) {
  const xamlCodeStart = offsetAt(sourceText, projection.xamlRange.start);
  const xamlCodeEnd = offsetAt(sourceText, projection.xamlRange.end);
  const xamlOffset = offsetAt(sourceText, xamlPosition);
  if (xamlOffset < xamlCodeStart || xamlOffset > xamlCodeEnd) {
    return undefined;
  }

  const projectedCodeStart = offsetAt(projection.projectedText, projection.projectedCodeRange.start);
  return positionAt(projection.projectedText, projectedCodeStart + (xamlOffset - xamlCodeStart));
}

function mapXamlRangeToProjectedRange(sourceText, projection, xamlRange) {
  const start = mapXamlPositionToProjectedPosition(sourceText, projection, xamlRange.start);
  const end = mapXamlPositionToProjectedPosition(sourceText, projection, xamlRange.end);
  if (!start || !end) {
    return undefined;
  }

  return new vscode.Range(start, end);
}

async function fetchInlineCSharpProjections(document, token) {
  const generation = runtimeGeneration;
  if (!documentMayContainInlineCSharp(document)) {
    return undefined;
  }

  const activeClient = await tryEnsureClientStarted();
  if (!isRuntimeGenerationCurrent(generation)) {
    return undefined;
  }

  if (!activeClient) {
    return undefined;
  }

  const sourceUri = document.uri.toString();
  const version = document.version ?? 0;
  const cacheKey = createInlineCSharpProjectionCacheKey(sourceUri, version);
  const cached = inlineCSharpProjectionCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  const inflight = inlineCSharpProjectionFetches.get(cacheKey);
  if (inflight) {
    return inflight;
  }

  const fetchPromise = (async () => {
    const response = await activeClient.sendRequest('axsg/inlineCSharpProjections', {
      textDocument: {
        uri: sourceUri
      },
      version,
      documentText: document.getText()
    }, token);
    if (!isRuntimeGenerationCurrent(generation)) {
      return undefined;
    }

    const responseItems = Array.isArray(response) ? response : [];
    const projections = responseItems
      .filter(item => item && typeof item.id === 'string' && item.xamlRange && item.projectedCodeRange && typeof item.projectedText === 'string')
      .map(item => {
        const projection = {
          id: item.id,
          kind: typeof item.kind === 'string' ? item.kind : 'expression',
          sourceUri,
          version,
          xamlRange: toVsCodeRange(item.xamlRange),
          projectedCodeRange: toVsCodeRange(item.projectedCodeRange),
          projectedText: item.projectedText
        };

        projection.uri = buildInlineCSharpProjectionUri(sourceUri, version, projection.id);
        return projection;
      });

    const entry = {
      cacheKey,
      sourceUri,
      version,
      sourceText: document.getText(),
      projections,
      projectionMap: new Map(projections.map(projection => [projection.id, projection]))
    };

    updateInlineCSharpProjectionCache(entry);
    return entry;
  })();

  inlineCSharpProjectionFetches.set(cacheKey, fetchPromise);
  try {
    return await fetchPromise;
  } finally {
    inlineCSharpProjectionFetches.delete(cacheKey);
  }
}

async function resolveInlineCSharpProjectionFromUri(uri, token) {
  const cached = inlineCSharpProjectionUriCache.get(uri.toString());
  if (cached) {
    return cached;
  }

  const parsed = parseInlineCSharpProjectionUri(uri);
  if (!parsed) {
    return undefined;
  }

  const exactCacheEntry = inlineCSharpProjectionCache.get(parsed.cacheKey);
  if (exactCacheEntry) {
    const exactProjection = exactCacheEntry.projectionMap.get(parsed.projectionId);
    if (exactProjection) {
      return {
        cacheKey: exactCacheEntry.cacheKey,
        sourceUri: exactCacheEntry.sourceUri,
        version: exactCacheEntry.version,
        sourceText: exactCacheEntry.sourceText,
        projection: exactProjection
      };
    }
  }

  const sourceDocument = await vscode.workspace.openTextDocument(vscode.Uri.parse(parsed.sourceUri));
  if ((sourceDocument.version ?? 0) !== parsed.version) {
    return undefined;
  }

  const cacheEntry = await fetchInlineCSharpProjections(sourceDocument, token);
  if (!cacheEntry) {
    return undefined;
  }

  const projection = cacheEntry.projectionMap.get(parsed.projectionId);
  if (!projection) {
    return undefined;
  }

  return {
    cacheKey: cacheEntry.cacheKey,
    sourceUri: cacheEntry.sourceUri,
    version: cacheEntry.version,
    sourceText: cacheEntry.sourceText,
    projection
  };
}

async function openInlineCSharpProjectionDocument(projectionUri) {
  let document = await vscode.workspace.openTextDocument(projectionUri);
  if (document.languageId !== 'csharp') {
    document = await vscode.languages.setTextDocumentLanguage(document, 'csharp');
  }

  return document;
}

async function tryGetInlineCSharpProjectionAtPosition(document, position, token) {
  if (!isXamlDocument(document)) {
    return undefined;
  }

  const cacheEntry = await fetchInlineCSharpProjections(document, token);
  if (!cacheEntry || !Array.isArray(cacheEntry.projections) || cacheEntry.projections.length === 0) {
    return undefined;
  }

  for (const projection of cacheEntry.projections) {
    if (!containsPosition(projection.xamlRange, position)) {
      continue;
    }

    const projectedPosition = mapXamlPositionToProjectedPosition(cacheEntry.sourceText, projection, position);
    if (!projectedPosition) {
      continue;
    }

    const projectedDocument = await openInlineCSharpProjectionDocument(projection.uri);
    return {
      cacheEntry,
      projection,
      projectedPosition,
      projectedDocument
    };
  }

  return undefined;
}

function normalizeLocationResults(value) {
  if (!value) {
    return [];
  }

  if (Array.isArray(value)) {
    return value;
  }

  return [value];
}

function mapProjectedResultLocation(result) {
  if (!result) {
    return undefined;
  }

  if (result.targetUri && result.targetRange) {
    const targetUri = result.targetUri;
    if (targetUri.scheme === AXSG_INLINE_CSHARP_SCHEME) {
      const projectionInfo = inlineCSharpProjectionUriCache.get(targetUri.toString());
      if (!projectionInfo) {
        return undefined;
      }

      const mappedRange = mapProjectedRangeToXamlRange(
        projectionInfo.sourceText,
        projectionInfo.projection,
        result.targetSelectionRange ?? result.targetRange);
      if (!mappedRange) {
        return undefined;
      }

      return new vscode.Location(vscode.Uri.parse(projectionInfo.sourceUri), mappedRange);
    }

    return new vscode.Location(targetUri, result.targetSelectionRange ?? result.targetRange);
  }

  if (result.uri && result.range) {
    if (result.uri.scheme === AXSG_INLINE_CSHARP_SCHEME) {
      const projectionInfo = inlineCSharpProjectionUriCache.get(result.uri.toString());
      if (!projectionInfo) {
        return undefined;
      }

      const mappedRange = mapProjectedRangeToXamlRange(
        projectionInfo.sourceText,
        projectionInfo.projection,
        result.range);
      if (!mappedRange) {
        return undefined;
      }

      return new vscode.Location(vscode.Uri.parse(projectionInfo.sourceUri), mappedRange);
    }

    return result;
  }

  return undefined;
}

function dedupeLocations(locations) {
  const map = new Map();
  for (const location of locations) {
    if (!(location instanceof vscode.Location)) {
      continue;
    }

    const key = `${location.uri.toString()}::${location.range.start.line}:${location.range.start.character}:${location.range.end.line}:${location.range.end.character}`;
    if (!map.has(key)) {
      map.set(key, location);
    }
  }

  return [...map.values()];
}

function hasCompletionItems(result) {
  if (!result) {
    return false;
  }

  if (Array.isArray(result)) {
    return result.length > 0;
  }

  if (Array.isArray(result.items)) {
    return result.items.length > 0;
  }

  return false;
}

function hasLocations(result) {
  return normalizeLocationResults(result)
    .map(mapProjectedResultLocation)
    .some(location => location instanceof vscode.Location);
}

function mapProjectedCompletionRange(sourceText, projection, range) {
  if (!range) {
    return undefined;
  }

  if (range.inserting && range.replacing) {
    const inserting = mapProjectedRangeToXamlRange(sourceText, projection, range.inserting);
    const replacing = mapProjectedRangeToXamlRange(sourceText, projection, range.replacing);
    if (!inserting || !replacing) {
      return undefined;
    }

    return { inserting, replacing };
  }

  return mapProjectedRangeToXamlRange(sourceText, projection, range);
}

function mapProjectedTextEdits(sourceText, projection, edits) {
  if (!Array.isArray(edits) || edits.length === 0) {
    return undefined;
  }

  const mapped = [];
  for (const edit of edits) {
    if (!edit || !edit.range) {
      continue;
    }

    const mappedRange = mapProjectedRangeToXamlRange(sourceText, projection, edit.range);
    if (!mappedRange) {
      continue;
    }

    mapped.push(new vscode.TextEdit(mappedRange, typeof edit.newText === 'string' ? edit.newText : ''));
  }

  return mapped.length > 0 ? mapped : undefined;
}

function mapProjectedCompletionItem(sourceText, projection, item) {
  if (!item) {
    return undefined;
  }

  const mappedRange = mapProjectedCompletionRange(sourceText, projection, item.range);
  if (item.range && !mappedRange) {
    return undefined;
  }

  if (mappedRange) {
    item.range = mappedRange;
  }

  const mappedTextEdits = mapProjectedTextEdits(sourceText, projection, item.additionalTextEdits);
  if (Array.isArray(item.additionalTextEdits) && !mappedTextEdits) {
    delete item.additionalTextEdits;
  } else if (mappedTextEdits) {
    item.additionalTextEdits = mappedTextEdits;
  }

  return item;
}

function mapProjectedHover(sourceText, projection, hover) {
  if (!hover) {
    return undefined;
  }

  if (!hover.range) {
    return hover;
  }

  const mappedRange = mapProjectedRangeToXamlRange(sourceText, projection, hover.range);
  if (!mappedRange) {
    return undefined;
  }

  return new vscode.Hover(hover.contents, mappedRange);
}

async function tryExecuteCommand(command, ...args) {
  try {
    return await vscode.commands.executeCommand(command, ...args);
  } catch {
    return undefined;
  }
}

async function requestInlineCSharpCompletion(document, position, completionContext, token) {
  const projectionInfo = await tryGetInlineCSharpProjectionAtPosition(document, position, token);
  if (!projectionInfo) {
    return undefined;
  }

  const result = await tryExecuteCommand(
    'vscode.executeCompletionItemProvider',
    projectionInfo.projectedDocument.uri,
    projectionInfo.projectedPosition,
    completionContext && typeof completionContext.triggerCharacter === 'string'
      ? completionContext.triggerCharacter
      : undefined);

  if (!result) {
    return undefined;
  }

  if (Array.isArray(result)) {
    const items = result
      .map(item => mapProjectedCompletionItem(projectionInfo.cacheEntry.sourceText, projectionInfo.projection, item))
      .filter(Boolean);
    return items.length > 0 ? items : undefined;
  }

  if (Array.isArray(result.items)) {
    const items = result.items
      .map(item => mapProjectedCompletionItem(projectionInfo.cacheEntry.sourceText, projectionInfo.projection, item))
      .filter(Boolean);
    if (items.length === 0) {
      return undefined;
    }

    return new vscode.CompletionList(items, result.isIncomplete);
  }

  return undefined;
}

async function requestInlineCSharpHover(document, position, token) {
  const projectionInfo = await tryGetInlineCSharpProjectionAtPosition(document, position, token);
  if (!projectionInfo) {
    return undefined;
  }

  const hovers = await tryExecuteCommand(
    'vscode.executeHoverProvider',
    projectionInfo.projectedDocument.uri,
    projectionInfo.projectedPosition);
  if (!Array.isArray(hovers) || hovers.length === 0) {
    return undefined;
  }

  for (const hover of hovers) {
    const mappedHover = mapProjectedHover(projectionInfo.cacheEntry.sourceText, projectionInfo.projection, hover);
    if (mappedHover) {
      return mappedHover;
    }
  }

  return undefined;
}

async function requestInlineCSharpLocations(command, document, position, token, includeDeclaration) {
  const projectionInfo = await tryGetInlineCSharpProjectionAtPosition(document, position, token);
  if (!projectionInfo) {
    return [];
  }

  const locations = await tryExecuteCommand(
    command,
    projectionInfo.projectedDocument.uri,
    projectionInfo.projectedPosition,
    includeDeclaration);

  return dedupeLocations(normalizeLocationResults(locations)
    .map(mapProjectedResultLocation)
    .filter(location => location instanceof vscode.Location));
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

function createLanguageClient() {
  if (!extensionContext || !resolvedServerStartup) {
    return undefined;
  }

  resolvedClientOptions = resolvedClientOptions ?? resolveClientOptions(extensionContext);
  const clientInstance = new lc.LanguageClient(
    'axsgLanguageServer',
    'AXSG Language Server',
    resolvedServerStartup.serverOptions,
    resolvedClientOptions);
  const trace = vscode.workspace.getConfiguration('axsg').get('languageServer.trace', 'off');
  clientInstance.setTrace(trace);
  return clientInstance;
}

async function ensureClientStarted() {
  const generation = runtimeGeneration;
  if (clientStartPromise) {
    return clientStartPromise;
  }

  if (!client) {
    client = createLanguageClient();
  }

  if (!client || !resolvedServerStartup) {
    return undefined;
  }

  setStatusBarState('starting', resolvedServerStartup.details);

  const startingClient = client;
  clientStartPromise = (async () => {
    try {
      await startingClient.start();
      if (!isRuntimeGenerationCurrent(generation)) {
        try {
          await startingClient.stop();
        } catch {
          // Best effort shutdown of a stale client startup.
        }
        return undefined;
      }

      setStatusBarState('running', resolvedServerStartup.details);
      return startingClient;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (isRuntimeGenerationCurrent(generation) && resolvedServerStartup) {
        setStatusBarState('error', resolvedServerStartup.details, message);
      }
      if (client === startingClient) {
        client = undefined;
      }

      clientStartPromise = undefined;
      throw error;
    }
  })();

  return clientStartPromise;
}

async function tryEnsureClientStarted() {
  try {
    return await ensureClientStarted();
  } catch {
    return undefined;
  }
}

function resolveClientOptions(context) {
  outputChannel = getOutputChannel();
  const configuration = vscode.workspace.getConfiguration('axsg');
  return {
    documentSelector: [
      { scheme: 'file', language: 'axaml' },
      { scheme: 'file', language: 'xaml' }
    ],
    synchronize: {
      fileEvents: [
        vscode.workspace.createFileSystemWatcher('**/*.{xaml,axaml}'),
        vscode.workspace.createFileSystemWatcher('**/*.csproj')
      ]
    },
    outputChannel,
    initializationOptions: {
      extensionPath: context.extensionPath,
      inlayHints: {
        bindingTypeHintsEnabled: configuration.get('inlayHints.bindingTypeHints.enabled', true),
        typeDisplayStyle: configuration.get('inlayHints.typeDisplayStyle', 'short')
      }
    },
    middleware: {
      provideCompletionItem: async (document, position, completionContext, token, next) => {
        const fallbackResult = await next(document, position, completionContext, token);
        if (hasCompletionItems(fallbackResult)) {
          return fallbackResult;
        }

        const inlineResult = await requestInlineCSharpCompletion(document, position, completionContext, token);
        return inlineResult ?? fallbackResult;
      },
      provideHover: async (document, position, token, next) => {
        const fallbackHover = await next(document, position, token);
        if (fallbackHover) {
          return fallbackHover;
        }

        const inlineHover = await requestInlineCSharpHover(document, position, token);
        return inlineHover ?? fallbackHover;
      },
      provideDefinition: async (document, position, token, next) => {
        const fallbackLocations = dedupeLocations(
          normalizeLocationResults(await next(document, position, token))
            .map(mapProjectedResultLocation)
            .filter(location => location instanceof vscode.Location));
        if (fallbackLocations.length > 0) {
          return fallbackLocations;
        }

        const inlineLocations = await requestInlineCSharpLocations(
          'vscode.executeDefinitionProvider',
          document,
          position,
          token,
          undefined);
        return inlineLocations.length > 0 ? inlineLocations : undefined;
      },
      provideDeclaration: async (document, position, token, next) => {
        const fallbackLocations = dedupeLocations(
          normalizeLocationResults(await next(document, position, token))
            .map(mapProjectedResultLocation)
            .filter(location => location instanceof vscode.Location));
        if (fallbackLocations.length > 0) {
          return fallbackLocations;
        }

        const inlineLocations = await requestInlineCSharpLocations(
          'vscode.executeDeclarationProvider',
          document,
          position,
          token,
          undefined);
        return inlineLocations.length > 0 ? inlineLocations : undefined;
      },
      provideReferences: async (document, position, referenceContext, token, next) => {
        const fallbackLocations = dedupeLocations(
          normalizeLocationResults(await next(document, position, referenceContext, token))
            .map(mapProjectedResultLocation)
            .filter(location => location instanceof vscode.Location));
        if (fallbackLocations.length > 0) {
          return fallbackLocations;
        }

        const inlineLocations = await requestInlineCSharpLocations(
          'vscode.executeReferenceProvider',
          document,
          position,
          token,
          referenceContext && referenceContext.includeDeclaration === true);
        return inlineLocations.length > 0 ? inlineLocations : undefined;
      }
    }
  };
}

function toProtocolPosition(position) {
  return {
    line: position.line,
    character: position.character
  };
}

function toVsCodeRange(range) {
  return new vscode.Range(
    range.start.line,
    range.start.character,
    range.end.line,
    range.end.character
  );
}

function toVsCodeLocation(location) {
  if (!location || typeof location.uri !== 'string' || !location.range) {
    return undefined;
  }

  return new vscode.Location(vscode.Uri.parse(location.uri), toVsCodeRange(location.range));
}

async function requestCrossLanguageLocations(method, document, position, token) {
  const activeClient = await tryEnsureClientStarted();
  if (!activeClient) {
    return [];
  }

  const response = await activeClient.sendRequest(method, {
    textDocument: {
      uri: document.uri.toString()
    },
    position: toProtocolPosition(position),
    documentText: document.getText()
  }, token);

  if (!Array.isArray(response)) {
    return [];
  }

  return response
    .map(toVsCodeLocation)
    .filter((location) => location instanceof vscode.Location);
}

async function applyProtocolWorkspaceEdit(edit) {
  if (!edit || !edit.changes || typeof edit.changes !== 'object') {
    return false;
  }

  const workspaceEdit = new vscode.WorkspaceEdit();
  appendProtocolWorkspaceEdit(workspaceEdit, edit);

  return vscode.workspace.applyEdit(workspaceEdit);
}

function appendProtocolWorkspaceEdit(workspaceEdit, edit) {
  if (!workspaceEdit || !edit || !edit.changes || typeof edit.changes !== 'object') {
    return 0;
  }

  let count = 0;
  for (const [uri, edits] of Object.entries(edit.changes)) {
    if (!Array.isArray(edits)) {
      continue;
    }

    const documentUri = vscode.Uri.parse(uri);
    for (const editItem of edits) {
      if (!editItem || !editItem.range) {
        continue;
      }

      workspaceEdit.replace(
        documentUri,
        toVsCodeRange(editItem.range),
        typeof editItem.newText === 'string' ? editItem.newText : ''
      );
      count++;
    }
  }

  return count;
}

function isXamlDocument(document) {
  return document?.languageId === 'xaml' || document?.languageId === 'axaml';
}

function isCSharpDocument(document) {
  return document?.languageId === 'csharp';
}

function tryParseCommandPositionArgument(value) {
  if (!value || typeof value !== 'object') {
    return undefined;
  }

  const candidate = value.position;
  if (!candidate || typeof candidate !== 'object') {
    return undefined;
  }

  if (!Number.isInteger(candidate.line) || !Number.isInteger(candidate.character)) {
    return undefined;
  }

  return new vscode.Position(candidate.line, candidate.character);
}

async function resolveEditorForRenameArgument(argument) {
  if (!argument || typeof argument !== 'object' || typeof argument.uri !== 'string' || argument.uri.length === 0) {
    return vscode.window.activeTextEditor;
  }

  const targetUri = vscode.Uri.parse(argument.uri);
  const activeEditor = vscode.window.activeTextEditor;
  if (activeEditor && activeEditor.document.uri.toString() === targetUri.toString()) {
    return activeEditor;
  }

  const visibleEditor = vscode.window.visibleTextEditors.find(editor =>
    editor.document.uri.toString() === targetUri.toString());
  if (visibleEditor) {
    return visibleEditor;
  }

  const document = await vscode.workspace.openTextDocument(targetUri);
  return vscode.window.showTextDocument(document, { preview: false, preserveFocus: false });
}

async function executeCrossLanguageRenameCommand(argument) {
  const activeClient = await tryEnsureClientStarted();
  if (!activeClient) {
    return;
  }

  const editor = await resolveEditorForRenameArgument(argument);
  if (!editor) {
    return;
  }

  const position = tryParseCommandPositionArgument(argument) ?? argument ?? editor.selection.active;
  const document = editor.document;
  if (isCSharpDocument(document)) {
    await executeCSharpRename(editor, position);
    return;
  }

  if (isXamlDocument(document)) {
    await executeAxsgRename(editor, position);
  }
}

async function executeAxsgRename(editor, position) {
  const activeClient = await tryEnsureClientStarted();
  if (!activeClient) {
    return;
  }

  const document = editor.document;
  const params = {
    textDocument: {
      uri: document.uri.toString()
    },
    position: toProtocolPosition(position),
    documentText: document.getText()
  };

  const prepareResult = await activeClient.sendRequest('axsg/refactor/prepareRename', params);
  if (!prepareResult || !prepareResult.range) {
    void vscode.window.showInformationMessage('AXSG rename is not available at the current position.');
    return;
  }

  const newName = await vscode.window.showInputBox({
    title: 'AXSG Rename Symbol Across C# and XAML',
    value: prepareResult.placeholder || '',
    prompt: 'Enter the new symbol name.'
  });
  if (typeof newName !== 'string' || newName.length === 0 || newName === prepareResult.placeholder) {
    return;
  }

  const renameResult = await activeClient.sendRequest('axsg/refactor/rename', {
    ...params,
    newName
  });
  const applied = await applyProtocolWorkspaceEdit(renameResult);
  if (!applied) {
    void vscode.window.showWarningMessage('AXSG could not apply the computed rename edits.');
  }
}

async function executeCSharpRename(editor, position) {
  const document = editor.document;

  let prepareResult;
  try {
    prepareResult = await executeNativeCSharpPrepareRename(document, position);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    void vscode.window.showWarningMessage(`AXSG could not prepare the C# rename: ${message}`);
    return;
  }

  if (!prepareResult) {
    void vscode.window.showInformationMessage('Rename is not available at the current C# position.');
    return;
  }

  const placeholder = typeof prepareResult.placeholder === 'string'
    ? prepareResult.placeholder
    : document.getText(prepareResult.range);
  const newName = await vscode.window.showInputBox({
    title: 'AXSG Rename Symbol Across C# and XAML',
    value: placeholder,
    prompt: 'Enter the new symbol name.'
  });
  if (typeof newName !== 'string' || newName.length === 0 || newName === placeholder) {
    return;
  }

  const nativeRenameEdit = await buildCombinedCSharpRenameEdit(document, position, newName, undefined, true);
  if (!(nativeRenameEdit instanceof vscode.WorkspaceEdit)) {
    void vscode.window.showWarningMessage('AXSG could not retrieve the C# rename edit from VS Code.');
    return;
  }

  const applied = await vscode.workspace.applyEdit(nativeRenameEdit);
  if (!applied) {
    void vscode.window.showWarningMessage('AXSG could not apply the combined C# and XAML rename edits.');
  }
}

async function executeNativeCSharpPrepareRename(document, position) {
  suppressCSharpRenameProvider = true;
  try {
    return await vscode.commands.executeCommand('_executePrepareRename', document.uri, position);
  } finally {
    suppressCSharpRenameProvider = false;
  }
}

async function executeNativeCSharpRename(document, position, newName) {
  suppressCSharpRenameProvider = true;
  try {
    return await vscode.commands.executeCommand(
      '_executeDocumentRenameProvider',
      document.uri,
      position,
      newName);
  } finally {
    suppressCSharpRenameProvider = false;
  }
}

async function buildCombinedCSharpRenameEdit(document, position, newName, token, showWarnings) {
  let nativeRenameEdit;
  try {
    nativeRenameEdit = await executeNativeCSharpRename(document, position, newName);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (showWarnings) {
      void vscode.window.showWarningMessage(`AXSG could not compute the C# rename edit: ${message}`);
    }

    throw error;
  }

  if (!(nativeRenameEdit instanceof vscode.WorkspaceEdit)) {
    return undefined;
  }

  const activeClient = await tryEnsureClientStarted();
  if (!activeClient) {
    return nativeRenameEdit;
  }

  try {
    const xamlPropagationEdit = await activeClient.sendRequest('axsg/csharp/renamePropagation', {
      textDocument: {
        uri: document.uri.toString()
      },
      position: toProtocolPosition(position),
      documentText: document.getText(),
      newName
    }, token);

    appendProtocolWorkspaceEdit(nativeRenameEdit, xamlPropagationEdit);
  } catch (error) {
    if (showWarnings) {
      const message = error instanceof Error ? error.message : String(error);
      void vscode.window.showWarningMessage(`AXSG could not compute XAML propagation edits: ${message}`);
    }
  }

  return nativeRenameEdit;
}

function setStatusBarState(state, details, errorMessage) {
  if (!statusBarItem) {
    return;
  }

  if (state === 'starting') {
    statusBarItem.text = '$(sync~spin) AXSG';
  } else if (state === 'running') {
    statusBarItem.text = '$(info) AXSG';
  } else if (state === 'idle') {
    statusBarItem.text = '$(debug-disconnect) AXSG';
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
  await shutdownExtensionRuntime();
  beginRuntimeGeneration();
  extensionContext = context;
  resolvedServerStartup = resolveServerOptions(context);
  metadataChangeEmitter = new vscode.EventEmitter();
  context.subscriptions.push(metadataChangeEmitter);
  sourceLinkChangeEmitter = new vscode.EventEmitter();
  context.subscriptions.push(sourceLinkChangeEmitter);
  inlineCSharpProjectionChangeEmitter = new vscode.EventEmitter();
  context.subscriptions.push(inlineCSharpProjectionChangeEmitter);
  const sourceLinkProvider = {
    onDidChange: sourceLinkChangeEmitter.event,
    provideTextDocumentContent(uri) {
      return renderSourceLinkDocument(uri);
    }
  };
  const inlineCSharpProjectionProvider = {
    onDidChange: inlineCSharpProjectionChangeEmitter.event,
    async provideTextDocumentContent(uri) {
      const projectionInfo = await resolveInlineCSharpProjectionFromUri(uri);
      if (!projectionInfo) {
        return createInlineCSharpLoadingDocument(uri);
      }

      return projectionInfo.projection.projectedText;
    }
  };
  const metadataProviderWithUpdates = {
    onDidChange: metadataChangeEmitter.event,
    provideTextDocumentContent(uri) {
      return renderMetadataDocument(uri);
    }
  };
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(
    AXSG_METADATA_SCHEME,
    metadataProviderWithUpdates));
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(
    AXSG_SOURCELINK_SCHEME,
    sourceLinkProvider));
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(
    AXSG_INLINE_CSHARP_SCHEME,
    inlineCSharpProjectionProvider));
  context.subscriptions.push(vscode.commands.registerCommand(
    'axsg.refactor.renameSymbol',
    async argument => {
      await executeCrossLanguageRenameCommand(argument);
    }));
  context.subscriptions.push(vscode.commands.registerCommand('axsg.design.focus', async () => {
    await vscode.commands.executeCommand('workbench.view.extension.axsgDesign');
  }));
  previewController = new AvaloniaPreviewController({
    context,
    ensureClientStarted: tryEnsureClientStarted,
    getOutputChannel,
    isXamlDocument,
    workspaceRoot: resolveWorkspaceRoot()
  });
  previewController.register(context);
  designController = new DesignSessionController({
    context,
    previewController,
    getOutputChannel,
    isXamlDocument
  });
  designController.register(context);
  context.subscriptions.push(vscode.languages.registerCodeActionsProvider(
    [
      { scheme: 'file', language: 'csharp' }
    ],
    {
      provideCodeActions(document, range) {
        const position = range.start;
        const action = new vscode.CodeAction(
          'AXSG: Rename Symbol Across C# and XAML',
          AXSG_REFACTOR_RENAME_KIND
        );
        action.isPreferred = true;
        action.command = {
          command: 'axsg.refactor.renameSymbol',
          title: 'AXSG: Rename Symbol Across C# and XAML',
          arguments: [position]
        };
        return [action];
      }
    },
    {
      providedCodeActionKinds: [AXSG_REFACTOR_RENAME_KIND]
    }));
  context.subscriptions.push(vscode.languages.registerReferenceProvider(
    [
      { scheme: 'file', language: 'csharp' }
    ],
    {
      async provideReferences(document, position, context, token) {
        return requestCrossLanguageLocations('axsg/csharp/references', document, position, token);
      }
    }));
  context.subscriptions.push(vscode.languages.registerDefinitionProvider(
    [
      { scheme: 'file', language: 'csharp' }
    ],
    {
      async provideDefinition(document, position, token) {
        return requestCrossLanguageLocations('axsg/csharp/declarations', document, position, token);
      }
    }));
  context.subscriptions.push(vscode.languages.registerDeclarationProvider(
    [
      { scheme: 'file', language: 'csharp' }
    ],
    {
      async provideDeclaration(document, position, token) {
        return requestCrossLanguageLocations('axsg/csharp/declarations', document, position, token);
      }
    }));
  context.subscriptions.push(vscode.languages.registerRenameProvider(
    [
      { scheme: 'file', language: 'csharp' }
    ],
    {
      async prepareRename(document, position) {
        if (suppressCSharpRenameProvider) {
          return undefined;
        }

        return executeNativeCSharpPrepareRename(document, position);
      },
      async provideRenameEdits(document, position, newName, token) {
        if (suppressCSharpRenameProvider) {
          return undefined;
        }

        return buildCombinedCSharpRenameEdit(document, position, newName, token, false);
      }
    }));
  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(async document => {
    if (isXamlDocument(document)) {
      void tryEnsureClientStarted();
    }

    if (document.uri.scheme !== AXSG_METADATA_SCHEME &&
        document.uri.scheme !== AXSG_SOURCELINK_SCHEME &&
        document.uri.scheme !== AXSG_INLINE_CSHARP_SCHEME) {
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
  setStatusBarState('idle', resolvedServerStartup.details);

  context.subscriptions.push(vscode.commands.registerCommand('axsg.languageServer.showInfo', async () => {
    const info = `AXSG Language Server (${resolvedServerStartup.details.effectiveMode})`;
    const selection = await vscode.window.showInformationMessage(
      info,
      'Open Output');
    if (selection === 'Open Output' && outputChannel) {
      outputChannel.show(true);
    }
  }));

  context.subscriptions.push(new vscode.Disposable(() => {
    void shutdownExtensionRuntime();
  }));

  if (vscode.window.visibleTextEditors.some(editor => isXamlDocument(editor.document))) {
    void tryEnsureClientStarted();
  }
}

async function deactivate() {
  await shutdownExtensionRuntime();
}

module.exports = {
  activate,
  deactivate
};
