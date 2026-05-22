# Frequently asked questions

Short answers to recurring questions about running Equibles. For step-by-step instructions, see the [how-to guides](README.md#how-to-guides).

## How do I disable the "update available" banner on the web portal?

The web portal checks GitHub Releases on a schedule and shows a banner when a newer version is published. To turn the check off, set `CHECK_FOR_UPDATES=false` in your `.env` file (or environment) and restart the `web` service. The banner stays hidden until you flip the setting back to `true`. To actually upgrade when an update is available, see [Upgrade to the latest release](how-to-upgrade.md).
