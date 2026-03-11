#!/usr/bin/env node
import { createHash } from "node:crypto";

const semverPattern =
  /^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;

const knownStageRanks = new Map([
  ["dev", 1],
  ["nightly", 1],
  ["ci", 1],
  ["preview", 2],
  ["pre", 2],
  ["alpha", 2],
  ["a", 2],
  ["beta", 3],
  ["b", 3],
  ["rc", 4],
]);

const patchStride = 5_000_000;
const stageStride = 100_000;
const sequenceStride = 100;
const maxSequence = 999;

function getTwoDigitFingerprint(value) {
  const hash = createHash("sha256").update(value, "utf8").digest();
  return ((hash[0] << 8) | hash[1]) % 100;
}

function resolveStageIdentifier(identifiers) {
  return identifiers.find((identifier) => /[A-Za-z]/.test(identifier))?.toLowerCase() ?? "prerelease";
}

function resolveStageRank(stageIdentifier) {
  const known = knownStageRanks.get(stageIdentifier);
  if (known !== undefined) {
    return known;
  }

  return 10 + (getTwoDigitFingerprint(stageIdentifier) % 40);
}

function resolveSequence(identifiers) {
  for (const identifier of identifiers) {
    if (/^\d+$/.test(identifier)) {
      return Math.min(Number.parseInt(identifier, 10), maxSequence);
    }

    const prefixedNumberMatch = /^(\d+)/.exec(identifier);
    if (prefixedNumberMatch !== null) {
      return Math.min(Number.parseInt(prefixedNumberMatch[1], 10), maxSequence);
    }
  }

  return 0;
}

function resolveVsCodeExtensionVersion(version) {
  const match = semverPattern.exec(version);
  if (match?.groups === undefined) {
    throw new Error(`Unsupported semantic version: ${version}`);
  }

  const major = Number.parseInt(match.groups.major, 10);
  const minor = Number.parseInt(match.groups.minor, 10);
  const patch = Number.parseInt(match.groups.patch, 10);
  const prerelease = match.groups.prerelease;

  if (prerelease === undefined) {
    return `${major}.${minor * 2}.${patch}`;
  }

  const identifiers = prerelease.split(".");
  const simpleChannelMatch = /^(alpha|a|beta|b|rc)\.(\d+)$/i.exec(prerelease);
  if (simpleChannelMatch !== null) {
    const channel = simpleChannelMatch[1].toLowerCase();
    const sequence = Number.parseInt(simpleChannelMatch[2], 10);
    const channelOffset =
      channel === "alpha" || channel === "a"
        ? 0
        : channel === "beta" || channel === "b"
          ? 100
          : 200;
    return `${major}.${minor * 2 + 1}.${patch * 1000 + channelOffset + sequence}`;
  }

  const stageIdentifier = resolveStageIdentifier(identifiers);
  const stageRank = resolveStageRank(stageIdentifier);
  const sequence = resolveSequence(identifiers);
  const simpleStageSequence =
    identifiers.length === 2 &&
    identifiers[0].toLowerCase() === stageIdentifier &&
    /^\d+$/.test(identifiers[1]);
  const discriminator = simpleStageSequence ? 0 : getTwoDigitFingerprint(prerelease);
  const resolvedPatch =
    patch * patchStride +
    stageRank * stageStride +
    sequence * sequenceStride +
    discriminator;

  return `${major}.${minor * 2 + 1}.${resolvedPatch}`;
}

function main(argv) {
  if (argv.length !== 3) {
    console.error("Usage: resolve-vscode-extension-version.mjs <version>");
    return 1;
  }

  try {
    console.log(resolveVsCodeExtensionVersion(argv[2]));
    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

process.exitCode = main(process.argv);
