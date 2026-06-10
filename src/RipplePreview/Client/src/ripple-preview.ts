import type { UmbEntryPointOnInit } from '@umbraco-cms/backoffice/extension-api';
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { getSettings, setAuthContext } from './api.js';
import { setRippleSettings } from './settings.js';
import { isPreviewMode, onPreviewModeChange, togglePreviewMode } from './preview-mode.js';
import { RippleGridViewElement } from './elements/ripple-grid-view.element.js';
import { RippleListViewElement } from './elements/ripple-list-view.element.js';
import { RippleRteViewElement } from './elements/ripple-rte-view.element.js';
import { RippleSingleViewElement } from './elements/ripple-single-view.element.js';
import { RippleHeaderToggleElement } from './elements/ripple-header-toggle.element.js';
import { RippleTogglePreviewPropertyAction } from './ripple-toggle.property-action.js';
import './elements/ripple-zoom-overlay.element.js';

declare const __RIPPLE_VERSION__: string;

let initialized = false;
let customViewsRegistered = false;
const customViewManifests: Array<UmbExtensionManifest> = [];

/**
 * Live-switches between Ripple previews and Umbraco's native block entries by
 * registering/unregistering the custom view manifests — extension slots react instantly.
 */
function applyPreviewMode(enabled: boolean) {
	if (!customViewManifests.length) return;

	if (enabled && !customViewsRegistered) {
		// Fresh manifest objects on every registration: the registry may decorate
		// registered manifests, which would make re-registering the originals unreliable.
		umbExtensionsRegistry.registerMany(customViewManifests.map((manifest) => ({ ...manifest })));
		customViewsRegistered = true;
	} else if (!enabled && customViewsRegistered) {
		for (const manifest of customViewManifests) {
			umbExtensionsRegistry.unregister(manifest.alias);
		}
		customViewsRegistered = false;
	}
}

export const onInit: UmbEntryPointOnInit = (host) => {
	console.info(`[RipplePreview] client ${__RIPPLE_VERSION__} loaded`);

	host.consumeContext(UMB_AUTH_CONTEXT, async (auth) => {
		if (!auth || initialized) return;
		setAuthContext(auth);

		try {
			// Wait until a token is actually available before calling our API.
			const token = await auth.getLatestToken();
			if (!token) return;

			const settings = await getSettings();
			if (!settings) return;
			initialized = true;
			setRippleSettings(settings);

			if (settings.blockGrid?.enabled) {
				customViewManifests.push({
					type: 'blockEditorCustomView',
					alias: 'RipplePreview.CustomView.BlockGrid',
					name: 'Ripple Preview Block Grid View',
					element: RippleGridViewElement,
					forBlockEditor: 'block-grid',
					...(settings.blockGrid.contentTypes?.length
						? { forContentTypeAlias: settings.blockGrid.contentTypes }
						: {}),
				} as UmbExtensionManifest);
			}

			if (settings.blockList?.enabled) {
				customViewManifests.push({
					type: 'blockEditorCustomView',
					alias: 'RipplePreview.CustomView.BlockList',
					name: 'Ripple Preview Block List View',
					element: RippleListViewElement,
					forBlockEditor: 'block-list',
					...(settings.blockList.contentTypes?.length
						? { forContentTypeAlias: settings.blockList.contentTypes }
						: {}),
				} as UmbExtensionManifest);
			}

			if (settings.richText?.enabled) {
				customViewManifests.push({
					type: 'blockEditorCustomView',
					alias: 'RipplePreview.CustomView.RichText',
					name: 'Ripple Preview Rich Text Block View',
					element: RippleRteViewElement,
					forBlockEditor: 'block-rte',
					...(settings.richText.contentTypes?.length
						? { forContentTypeAlias: settings.richText.contentTypes }
						: {}),
				} as UmbExtensionManifest);
			}

			if (settings.singleBlock?.enabled) {
				customViewManifests.push({
					type: 'blockEditorCustomView',
					alias: 'RipplePreview.CustomView.SingleBlock',
					name: 'Ripple Preview Single Block View',
					element: RippleSingleViewElement,
					forBlockEditor: 'block-single',
					...(settings.singleBlock.contentTypes?.length
						? { forContentTypeAlias: settings.singleBlock.contentTypes }
						: {}),
				} as UmbExtensionManifest);
			}

			if (!customViewManifests.length) return;

			// Toggle between previews and native block cards. Registered in two places —
			// the header bar and the block property "..." menu — plus the Ctrl+Shift+P
			// shortcut, so previews can always be re-enabled.
			umbExtensionsRegistry.register({
				type: 'headerApp',
				alias: 'RipplePreview.HeaderApp.Toggle',
				name: 'Ripple Preview Toggle',
				element: RippleHeaderToggleElement,
				weight: 850,
			} as UmbExtensionManifest);

			umbExtensionsRegistry.register({
				type: 'propertyAction',
				kind: 'default',
				alias: 'RipplePreview.PropertyAction.TogglePreviews',
				name: 'Ripple Preview Toggle Property Action',
				api: RippleTogglePreviewPropertyAction,
				forPropertyEditorUis: [
					'Umb.PropertyEditorUi.BlockGrid',
					'Umb.PropertyEditorUi.BlockList',
				],
				meta: {
					icon: 'icon-eye',
					label: 'Toggle live previews',
				},
			} as UmbExtensionManifest);

			applyPreviewMode(isPreviewMode());
			onPreviewModeChange(applyPreviewMode);

			// Failsafe toggle that works regardless of what's on screen: Ctrl+Shift+P.
			window.addEventListener('keydown', (event) => {
				if (event.ctrlKey && event.shiftKey && (event.key === 'P' || event.key === 'p')) {
					event.preventDefault();
					togglePreviewMode();
				}
			});
		} catch (error) {
			console.error('[RipplePreview] initialisation failed', error);
		}
	});
};
