const DESIGN_TOOLBOX_ITEM_MIME = 'application/vnd.axsg.toolbox-item+json';
const DESIGN_TOOLBOX_TEXT_MIME = 'text/plain';
const DESIGN_TOOLBOX_TEXT_PREFIX = 'AXSG_TOOLBOX_ITEM:';

function normalizeToolboxItem(candidate) {
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate.items)) {
    return null;
  }

  const name = typeof candidate.name === 'string' && candidate.name.trim().length > 0
    ? candidate.name.trim()
    : '';
  const displayName = typeof candidate.displayName === 'string' && candidate.displayName.trim().length > 0
    ? candidate.displayName.trim()
    : name;
  const xamlSnippet = typeof candidate.xamlSnippet === 'string' && candidate.xamlSnippet.trim().length > 0
    ? candidate.xamlSnippet
    : '';
  if (!name && !displayName) {
    return null;
  }

  return {
    name: name || displayName,
    displayName: displayName || name,
    category: typeof candidate.category === 'string' ? candidate.category : '',
    xamlSnippet,
    isProjectControl: !!candidate.isProjectControl,
    tags: Array.isArray(candidate.tags)
      ? candidate.tags.filter(tag => typeof tag === 'string' && tag.trim().length > 0)
      : []
  };
}

function serializeToolboxItem(candidate) {
  const toolboxItem = normalizeToolboxItem(candidate);
  return toolboxItem
    ? JSON.stringify(toolboxItem)
    : '';
}

function serializeToolboxItemAsText(candidate) {
  const serialized = serializeToolboxItem(candidate);
  return serialized
    ? `${DESIGN_TOOLBOX_TEXT_PREFIX}${serialized}`
    : '';
}

function tryParseSerializedToolboxItem(value) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    return null;
  }

  try {
    const parsed = JSON.parse(value);
    return normalizeToolboxItem(parsed);
  } catch {
    return null;
  }
}

function tryParseTextToolboxItem(value) {
  if (typeof value !== 'string' || !value.startsWith(DESIGN_TOOLBOX_TEXT_PREFIX)) {
    return null;
  }

  return tryParseSerializedToolboxItem(value.slice(DESIGN_TOOLBOX_TEXT_PREFIX.length));
}

async function readToolboxItemFromDataTransfer(dataTransfer) {
  if (!dataTransfer || typeof dataTransfer.get !== 'function') {
    return null;
  }

  const customItem = dataTransfer.get(DESIGN_TOOLBOX_ITEM_MIME);
  if (customItem && typeof customItem.asString === 'function') {
    const customText = await customItem.asString();
    const toolboxItem = tryParseSerializedToolboxItem(customText);
    if (toolboxItem) {
      return toolboxItem;
    }
  }

  const textItem = dataTransfer.get(DESIGN_TOOLBOX_TEXT_MIME);
  if (textItem && typeof textItem.asString === 'function') {
    const text = await textItem.asString();
    return tryParseTextToolboxItem(text);
  }

  return null;
}

module.exports = {
  DESIGN_TOOLBOX_ITEM_MIME,
  DESIGN_TOOLBOX_TEXT_MIME,
  DESIGN_TOOLBOX_TEXT_PREFIX,
  normalizeToolboxItem,
  serializeToolboxItem,
  serializeToolboxItemAsText,
  tryParseSerializedToolboxItem,
  tryParseTextToolboxItem,
  readToolboxItemFromDataTransfer
};
