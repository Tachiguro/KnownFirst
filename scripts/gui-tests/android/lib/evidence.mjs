import { createHash } from 'node:crypto';
import { realpathSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const expectedPackageId = 'com.tachiguro.knownfirst.guitest';
const expectedConfiguration = 'Debug';
const fullCommitPattern = /^[0-9a-f]{40}$/i;
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const authoritativeRunsRoot = join(repositoryRoot, 'artifacts', 'gui-tests', 'android', 'runs');

const requiredSummaryFields = [
  'scenarioId', 'matrixMapping', 'result', 'failedStep', 'git', 'buildIdentity', 'packageId',
  'configuration', 'toolVersions', 'device', 'physicalOrEmulator', 'orientation',
  'screenshotPixels', 'density', 'dpViewport', 'language', 'theme', 'contexts', 'profileId',
  'safetyBefore', 'safetyAfter', 'timestamps', 'assertionCounts', 'remainingUnproven'
];

export function createSummary(input) {
  for (const field of requiredSummaryFields) {
    if (!(field in input)) {
      throw new Error(`Missing required summary field: ${field}`);
    }
  }

  if (input.matrixMapping !== null && input.matrixMapping !== 'S36') {
    throw new Error(`Unsupported matrixMapping: ${input.matrixMapping}. Only 'S36' or null is permitted.`);
  }

  if (input.result === 'Passed') {
    const verifiedIdentity = evaluateRuntimeBuildIdentity({
      expectedCommit: input.buildIdentity?.expected?.commit,
      observed: input.buildIdentity?.observed
    });
    if (
      input.buildIdentity?.matched !== true ||
      input.buildIdentity?.failureReason !== null ||
      verifiedIdentity.matched !== true
    ) {
      throw new Error('Passed evidence requires a verified runtime build identity agreement.');
    }
  }

  return {
    ...input,
    screenshots: [],
    buildPerformed: false,
    installationPerformed: false,
    dataResetPerformed: false,
    liveNetworkUsed: false
  };
}

export function evaluateRuntimeBuildIdentity({ expectedCommit, observed }) {
  const expected = {
    commit: expectedCommit,
    packageId: expectedPackageId,
    configuration: expectedConfiguration
  };
  const retainedObserved = observed && typeof observed === 'object' ? { ...observed } : {};
  const failed = (failureReason) => ({ expected, observed: retainedObserved, matched: false, failureReason });

  if (typeof expectedCommit !== 'string' || !fullCommitPattern.test(expectedCommit)) {
    return failed('Expected commit must be exactly 40 hexadecimal characters.');
  }
  if (typeof retainedObserved.commit !== 'string' || !fullCommitPattern.test(retainedObserved.commit)) {
    return failed('Observed commit is missing or must be exactly 40 hexadecimal characters.');
  }
  if (retainedObserved.commit.toLowerCase() !== expectedCommit.toLowerCase()) {
    return failed('Observed commit does not match the expected commit.');
  }

  const clean = retainedObserved.dirty === false ||
    (typeof retainedObserved.dirty === 'string' && retainedObserved.dirty.trim().toLowerCase() === 'false');
  if (!clean) {
    return failed('Observed build identity is dirty or has an invalid dirty state.');
  }
  if (typeof retainedObserved.version !== 'string' || retainedObserved.version.trim().length === 0) {
    return failed('Observed version is missing.');
  }
  if (typeof retainedObserved.buildNumber !== 'string' || retainedObserved.buildNumber.trim().length === 0) {
    return failed('Observed build number is missing.');
  }
  if (retainedObserved.packageId !== expectedPackageId) {
    return failed('Observed package ID does not match the required GUI-test package.');
  }
  if (retainedObserved.configuration !== expectedConfiguration) {
    return failed('Observed configuration does not match Debug.');
  }

  return { expected, observed: retainedObserved, matched: true, failureReason: null };
}

export function resolveAndroidRunDirectory(candidate) {
  if (typeof candidate !== 'string' || candidate.trim().length === 0) {
    throw new Error('The Android GUI run directory is required.');
  }

  const canonicalRoot = realpathSync(authoritativeRunsRoot);
  const canonicalCandidate = realpathSync(candidate);
  const relativeCandidate = relative(canonicalRoot, canonicalCandidate);
  if (
    relativeCandidate.length === 0 ||
    relativeCandidate === '..' ||
    relativeCandidate.startsWith(`..${sep}`) ||
    isAbsolute(relativeCandidate)
  ) {
    throw new Error('The Android GUI run directory must be a strict descendant of the repository runs root and may not resolve outside it.');
  }

  return canonicalCandidate;
}

export function recordScreenshot(summary, { name, bytes }) {
  if (!name || !bytes) {
    throw new Error('Screenshot name and bytes are required.');
  }

  return {
    ...summary,
    screenshots: [
      ...summary.screenshots,
      {
        name,
        sha256: createHash('sha256').update(bytes).digest('hex')
      }
    ]
  };
}

export async function captureScreenshotEvidence({ name, capture, write }) {
  if (!name || typeof capture !== 'function' || typeof write !== 'function') {
    throw new Error('Screenshot name, capture, and writer are required.');
  }

  const base64Png = await capture();
  if (typeof base64Png !== 'string' || base64Png.length === 0) {
    throw new Error('Screenshot capture must return a non-empty base64 PNG string.');
  }

  const bytes = Buffer.from(base64Png, 'base64');
  if (bytes.length === 0) {
    throw new Error('Screenshot capture decoded to an empty PNG.');
  }

  await write(name, bytes);
  return { name, bytes };
}

export async function finalizeOwnedResources({ scenarioSucceeded, safetyAfter, session, ownedServer }) {
  let sessionClosed = false;
  let serverTerminated = false;

  try {
    if (session) {
      await session.quit();
      sessionClosed = true;
    }
  } finally {
    if (ownedServer) {
      await ownedServer.terminate();
      serverTerminated = true;
    }
  }

  return {
    succeeded: Boolean(scenarioSucceeded && safetyAfter?.passed),
    sessionClosed,
    serverTerminated
  };
}
