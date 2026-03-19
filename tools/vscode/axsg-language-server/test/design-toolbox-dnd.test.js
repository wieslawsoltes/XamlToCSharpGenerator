const test = require('node:test');
const assert = require('node:assert/strict');
const {
  DESIGN_TOOLBOX_TEXT_PREFIX,
  normalizeToolboxItem,
  tryParseSerializedToolboxItem,
  tryParseTextToolboxItem
} = require('../design-toolbox-dnd');

test('normalizeToolboxItem strips toolbox categories and keeps insertable fields', () => {
  assert.equal(normalizeToolboxItem({ name: 'Controls', items: [] }), null);
  assert.deepEqual(normalizeToolboxItem({
    name: 'Button',
    displayName: 'Button',
    category: 'Common',
    xamlSnippet: '<Button Content="Button" />',
    isProjectControl: true,
    tags: ['input', '', null]
  }), {
    name: 'Button',
    displayName: 'Button',
    category: 'Common',
    xamlSnippet: '<Button Content="Button" />',
    isProjectControl: true,
    tags: ['input']
  });
});

test('toolbox drag payload parser accepts custom and text/plain payloads', () => {
  const serialized = '{"name":"TextBox","displayName":"TextBox","category":"Input","xamlSnippet":"<TextBox Text=\\"\\" />","isProjectControl":false,"tags":["input"]}';

  assert.deepEqual(tryParseSerializedToolboxItem(serialized), {
    name: 'TextBox',
    displayName: 'TextBox',
    category: 'Input',
    xamlSnippet: '<TextBox Text="" />',
    isProjectControl: false,
    tags: ['input']
  });

  assert.deepEqual(tryParseTextToolboxItem(`${DESIGN_TOOLBOX_TEXT_PREFIX}${serialized}`), {
    name: 'TextBox',
    displayName: 'TextBox',
    category: 'Input',
    xamlSnippet: '<TextBox Text="" />',
    isProjectControl: false,
    tags: ['input']
  });
});
