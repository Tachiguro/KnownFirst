import { createHash } from 'node:crypto';

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

  if (input.matrixMapping !== null) {
    throw new Error('P16-A is a pre-matrix scenario and must retain a null matrixMapping.');
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
