const http = require('http');
const https = require('https');

const DEFAULT_REQUEST_TIMEOUT_MS = 30000;

async function fetchPreviewPageHtmlAsync(previewUrl, attemptCount = 10, delayMs = 150) {
  let lastError;
  for (let attemptIndex = 0; attemptIndex < attemptCount; attemptIndex += 1) {
    try {
      return await fetchTextFromUrlAsync(previewUrl);
    } catch (error) {
      lastError = error;
      if (attemptIndex < attemptCount - 1) {
        await delayAsync(delayMs);
      }
    }
  }

  throw lastError || new Error(`Failed to fetch ${previewUrl}.`);
}

async function fetchTextFromUrlAsync(urlText, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const parsedUrl = new URL(urlText);
    const client = parsedUrl.protocol === 'https:' ? https : http;
    const request = client.get(parsedUrl, response => {
      if (response.statusCode && response.statusCode >= 400) {
        settled = true;
        response.resume();
        reject(new Error(`HTTP ${response.statusCode} while fetching ${urlText}.`));
        return;
      }

      const chunks = [];
      response.setEncoding('utf8');
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        if (settled) {
          return;
        }

        settled = true;
        resolve(chunks.join(''));
      });
      response.on('error', error => {
        if (settled) {
          return;
        }

        settled = true;
        reject(error);
      });
    });

    request.setTimeout(timeoutMs, () => {
      if (settled) {
        return;
      }

      const timeoutError = new Error(`Timed out fetching ${urlText}.`);
      settled = true;
      request.destroy(timeoutError);
      reject(timeoutError);
    });
    request.on('error', error => {
      if (settled) {
        return;
      }

      settled = true;
      reject(error);
    });
  });
}

function delayAsync(delayMs) {
  return new Promise(resolve => setTimeout(resolve, delayMs));
}

module.exports = {
  DEFAULT_REQUEST_TIMEOUT_MS,
  delayAsync,
  fetchPreviewPageHtmlAsync,
  fetchTextFromUrlAsync
};
