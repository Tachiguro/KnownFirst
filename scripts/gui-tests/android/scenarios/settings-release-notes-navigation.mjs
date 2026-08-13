export const scenarioId = 'P16A-SettingsReleaseNotesNavigation';

export async function runSettingsReleaseNotesNavigation({ browser, recordAssertion, captureScreenshot }) {
  const contexts = await browser.getContexts();
  const webview = contexts.find((context) => context.startsWith('WEBVIEW_com.tachiguro.knownfirst.guitest'));
  if (!webview) {
    throw new Error('Expected the dedicated KnownFirst GUI-test WebView context, but none was available.');
  }

  await browser.switchContext(webview);
  const indicator = await browser.$('#gui-test-profile-indicator');
  await indicator.waitForDisplayed();
  const profileId = await indicator.getAttribute('data-gui-test-profile-id');
  const provider = await indicator.getAttribute('data-gui-test-provider');
  const providerInvocations = await indicator.getAttribute('data-gui-test-provider-invocations');
  await recordAssertion('GUI-test profile marker is active', Boolean(profileId));
  await recordAssertion('Offline GUI-test provider is active', provider === 'offline');
  await recordAssertion('Offline GUI-test provider has no unexpected invocations', providerInvocations === '0');

  for (const selector of [
    '#stat-document-count', '#stat-unreviewed-word-count', '#stat-known-word-count',
    '#stat-unknown-word-count', '#stat-prepared-word-count'
  ]) {
    const statistic = await browser.$(selector);
    await statistic.waitForDisplayed();
    await recordAssertion(`${selector} is deterministic empty state`, (await statistic.getText()).trim() === '0');
  }

  await (await browser.$('#nav-settings')).click();
  await (await browser.$('#settings-release-notes-link')).waitForDisplayed();
  await (await browser.$('#settings-release-notes-link')).click();
  await (await browser.$('#release-notes-page')).waitForDisplayed();
  await recordAssertion('Release Notes route is active', (await browser.getUrl()).endsWith('/release-notes'));
  await recordAssertion('Newest Beta 13 release note is visible', (await browser.$('#release-note-1.0.0-beta.13')).isDisplayed());

  await browser.switchContext('NATIVE_APP');
  const screenshotEvidence = await captureScreenshot('release-notes.png');
  await browser.switchContext(webview);
  await (await browser.$('#nav-home')).click();
  await (await browser.$('#gui-test-profile-indicator')).waitForDisplayed();
  await recordAssertion('Profile is stable for the process',
    profileId === await (await browser.$('#gui-test-profile-indicator')).getAttribute('data-gui-test-profile-id'));
  await recordAssertion('Offline provider remains unused after navigation',
    providerInvocations === await (await browser.$('#gui-test-profile-indicator')).getAttribute('data-gui-test-provider-invocations'));

  return { profileId, providerInvocations, screenshotEvidence };
}
