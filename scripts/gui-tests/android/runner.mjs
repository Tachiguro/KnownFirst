import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { remote } from 'webdriverio';
import {
  createSummary, evaluateRuntimeBuildIdentity, finalizeOwnedResources, recordScreenshot,
  resolveAndroidRunDirectory
} from './lib/evidence.mjs';
import { runSettingsReleaseNotesNavigation, scenarioId } from './scenarios/settings-release-notes-navigation.mjs';

const allowedPackage = 'com.tachiguro.knownfirst.guitest';

function requireArgument(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required for an authorized runtime run.`);
  }
  return value;
}

async function main() {
  const runDirectory = resolveAndroidRunDirectory(requireArgument('KNOWNFIRST_ANDROID_GUI_RUN_DIRECTORY'));
  const deviceId = requireArgument('KNOWNFIRST_ANDROID_GUI_DEVICE_ID');
  const chromedriverExecutable = requireArgument('KNOWNFIRST_ANDROID_GUI_CHROMEDRIVER');
  const appiumPort = Number(requireArgument('KNOWNFIRST_ANDROID_GUI_APPIUM_PORT'));
  const expectedCommit = requireArgument('KNOWNFIRST_ANDROID_GUI_EXPECTED_COMMIT');
  await mkdir(join(runDirectory, 'screenshots'), { recursive: true });

  const capabilities = {
    platformName: 'Android',
    'appium:automationName': 'UiAutomator2',
    'appium:udid': deviceId,
    'appium:appPackage': allowedPackage,
    'appium:appWaitActivity': '*',
    'appium:noReset': true,
    'appium:fullReset': false,
    'appium:forceAppLaunch': true,
    'appium:chromedriverExecutable': chromedriverExecutable,
    'appium:autoWebview': false
  };

  let browser;
  const assertions = [];
  let summary;
  let scenarioState = {};
  let device = {};
  let availableContexts = [];
  let webviewContext = null;
  let buildIdentity = evaluateRuntimeBuildIdentity({ expectedCommit, observed: {} });
  try {
    if (!/^[0-9a-f]{40}$/i.test(expectedCommit)) {
      throw new Error('Expected commit must be exactly 40 hexadecimal characters.');
    }

    browser = await remote({ hostname: '127.0.0.1', port: appiumPort, path: '/', capabilities });
    await writeFile(join(runDirectory, 'capabilities.json'), JSON.stringify(capabilities, null, 2));
    device = await browser.execute('mobile: getDeviceInfo');
    await writeFile(join(runDirectory, 'device.json'), JSON.stringify(device, null, 2));
    availableContexts = await browser.getContexts();
    webviewContext = availableContexts.find((context) =>
      context.startsWith('WEBVIEW_com.tachiguro.knownfirst.guitest')) ?? null;
    if (!webviewContext) {
      throw new Error('Expected the dedicated KnownFirst GUI-test WebView context for build identity, but none was available.');
    }

    await browser.switchContext(webviewContext);
    const indicator = await browser.$('#gui-test-profile-indicator');
    await indicator.waitForDisplayed();
    let observedIdentity;
    try {
      observedIdentity = {
        commit: await indicator.getAttribute('data-build-commit'),
        dirty: await indicator.getAttribute('data-build-dirty'),
        version: await indicator.getAttribute('data-build-version'),
        buildNumber: await indicator.getAttribute('data-build-number'),
        configuration: await indicator.getAttribute('data-build-configuration'),
        packageId: await indicator.getAttribute('data-build-package-id')
      };
    } catch (error) {
      throw new Error(`Runtime build identity observation failed: ${error.message}`);
    }
    buildIdentity = evaluateRuntimeBuildIdentity({ expectedCommit, observed: observedIdentity });
    if (!buildIdentity.matched) {
      throw new Error(`Runtime build identity verification failed: ${buildIdentity.failureReason}`);
    }

    const recordAssertion = async (name, passed) => {
      assertions.push({ name, passed: Boolean(passed) });
      if (!passed) throw new Error(`Assertion failed: ${name}`);
    };
    const saveScreenshot = async (name) => browser.saveScreenshot(join(runDirectory, 'screenshots', name));

    scenarioState = await runSettingsReleaseNotesNavigation({ browser, recordAssertion, saveScreenshot });
    summary = createSummary({
      scenarioId,
      matrixMapping: null,
      result: 'Passed',
      failedStep: null,
      git: { commit: expectedCommit, branch: 'runtime-observed' },
      buildIdentity,
      packageId: allowedPackage,
      configuration: 'Debug',
      toolVersions: { node: process.version },
      device,
      physicalOrEmulator: 'runtime-observed',
      orientation: 'runtime-observed',
      screenshotPixels: {},
      density: null,
      dpViewport: {},
      language: 'en',
      theme: 'light',
      contexts: { native: 'NATIVE_APP', webview: webviewContext, available: availableContexts },
      profileId: scenarioState.profileId,
      safetyBefore: { passed: true, providerInvocations: scenarioState.providerInvocations },
      safetyAfter: { passed: true, providerInvocations: scenarioState.providerInvocations },
      timestamps: { startedAtUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() },
      assertionCounts: { passed: assertions.length, failed: 0 },
      remainingUnproven: ['Clean matching assembly Git metadata does not establish APK-byte reproducibility.']
    });
    summary = recordScreenshot(summary, { name: 'release-notes.png', bytes: await browser.takeScreenshot() });
  } catch (error) {
    summary = createSummary({
      scenarioId,
      matrixMapping: null,
      result: 'Failed',
      failedStep: error.message,
      git: { commit: expectedCommit, branch: 'runtime-observed' },
      buildIdentity, packageId: allowedPackage, configuration: 'Debug', toolVersions: {}, device,
      physicalOrEmulator: 'runtime-observed', orientation: 'runtime-observed', screenshotPixels: {}, density: null,
      dpViewport: {}, language: 'en', theme: 'light',
      contexts: { native: 'NATIVE_APP', webview: webviewContext, available: availableContexts },
      profileId: 'runtime-observed',
      safetyBefore: { passed: false }, safetyAfter: { passed: false },
      timestamps: { startedAtUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() },
      assertionCounts: { passed: assertions.filter((assertion) => assertion.passed).length, failed: 1 },
      remainingUnproven: [error.message]
    });
  } finally {
    const cleanup = await finalizeOwnedResources({
      scenarioSucceeded: summary?.result === 'Passed', safetyAfter: summary?.safetyAfter, session: browser,
      ownedServer: null
    });
    const completedSummary = { ...summary, cleanup };
    await writeFile(join(runDirectory, 'summary.json'), JSON.stringify(completedSummary, null, 2));
    await writeFile(join(runDirectory, 'safety-before.json'), JSON.stringify(completedSummary.safetyBefore, null, 2));
    await writeFile(join(runDirectory, 'safety-after.json'), JSON.stringify(completedSummary.safetyAfter, null, 2));
    await writeFile(join(runDirectory, 'steps.jsonl'), assertions.map((assertion) => JSON.stringify(assertion)).join('\n'));
  }

  if (summary.result !== 'Passed') process.exitCode = 1;
}

main().catch((error) => {
  process.stderr.write(`${error.stack ?? error.message}\n`);
  process.exitCode = 1;
});
