const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');

const {
  fetchTextFromUrlAsync
} = require('../preview-fetch');

test('fetchTextFromUrlAsync rejects when the response times out', async () => {
  const sockets = new Set();
  const server = http.createServer((request, response) => {
    sockets.add(response.socket);
  });

  try {
    await new Promise((resolve, reject) => {
      server.listen(0, '127.0.0.1', error => {
        if (error) {
          reject(error);
          return;
        }

        resolve();
      });
    });

    const address = server.address();
    assert.ok(address && typeof address === 'object');

    await assert.rejects(
      fetchTextFromUrlAsync(`http://127.0.0.1:${address.port}/`, 50),
      /Timed out fetching/);
  } finally {
    for (const socket of sockets) {
      socket.destroy();
    }

    await new Promise(resolve => server.close(resolve));
  }
});
