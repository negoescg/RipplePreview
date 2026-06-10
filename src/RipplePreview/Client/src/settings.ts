import type { RippleSettings } from './api.js';

const emptyEditor = () => ({
	enabled: false,
	contentTypes: [],
	fullAreaPreviewContentTypes: [],
	stackedAreaPreviewContentTypes: [],
});

let settings: RippleSettings = {
	designWidth: 1440,
	blockGrid: emptyEditor(),
	blockList: emptyEditor(),
	richText: emptyEditor(),
	singleBlock: emptyEditor(),
};

export function setRippleSettings(value: RippleSettings) {
	settings = value;
}

export function getRippleSettings(): RippleSettings {
	return settings;
}
