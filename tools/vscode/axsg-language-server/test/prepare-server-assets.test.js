const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('bundled designer host includes Roslyn CSharp runtime assembly', () => {
  const designerHostDir = path.resolve(__dirname, '..', 'designer-host');
  const csharpAssemblyPath = path.join(designerHostDir, 'Microsoft.CodeAnalysis.CSharp.dll');
  const xamlLoaderAssemblyPath = path.join(designerHostDir, 'Avalonia.Markup.Xaml.Loader.dll');
  const runtimeConfigPath = path.join(designerHostDir, 'XamlToCSharpGenerator.Previewer.DesignerHost.runtimeconfig.json');
  const depsFilePath = path.join(designerHostDir, 'XamlToCSharpGenerator.Previewer.DesignerHost.deps.json');

  assert.equal(fs.existsSync(designerHostDir), true);
  assert.equal(fs.existsSync(csharpAssemblyPath), true);
  assert.equal(fs.existsSync(xamlLoaderAssemblyPath), true);
  assert.equal(fs.existsSync(runtimeConfigPath), true);
  assert.equal(fs.existsSync(depsFilePath), true);
});
