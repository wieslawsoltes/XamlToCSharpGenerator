const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('bundled designer host includes Roslyn CSharp runtime assembly', () => {
  const designerHostDir = path.resolve(__dirname, '..', 'designer-host');
  const csharpAssemblyPath = path.join(designerHostDir, 'Microsoft.CodeAnalysis.CSharp.dll');

  assert.equal(fs.existsSync(designerHostDir), true);
  assert.equal(fs.existsSync(csharpAssemblyPath), true);
});
