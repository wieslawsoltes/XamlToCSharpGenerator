const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('extension contributes shared XAML snippets for xaml and axaml', () => {
  const packageJsonPath = path.resolve(__dirname, '..', 'package.json');
  const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
  const snippets = packageJson.contributes?.snippets;

  assert.ok(Array.isArray(snippets));
  assert.equal(snippets.length, 2);
  assert.deepEqual(
    snippets.map(item => item.language).sort(),
    ['axaml', 'xaml']
  );

  for (const contribution of snippets) {
    assert.equal(contribution.path, './snippets/xaml.code-snippets');
  }
});

test('shared XAML snippets include common Avalonia authoring templates', () => {
  const snippetsPath = path.resolve(__dirname, '..', 'snippets', 'xaml.code-snippets');
  const snippets = JSON.parse(fs.readFileSync(snippetsPath, 'utf8'));

  assert.ok(snippets['Avalonia UserControl']);
  assert.ok(snippets['Avalonia Window']);
  assert.ok(snippets['Style']);
  assert.ok(snippets['DataTemplate']);
  assert.ok(snippets['ControlTheme']);
  assert.equal(snippets.Binding.body, '{Binding $0}');
  assert.equal(snippets.CompiledBinding.body, '{CompiledBinding $0}');
});
