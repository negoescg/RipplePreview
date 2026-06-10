import { UmbPropertyActionBase } from '@umbraco-cms/backoffice/property-action';
import { togglePreviewMode } from './preview-mode.js';

/**
 * Property action ("..." menu on block editor properties) that switches all block
 * editors between live previews and native block cards. Registered unconditionally
 * so previews can always be re-enabled, even while every custom view is unregistered.
 */
export class RippleTogglePreviewPropertyAction extends UmbPropertyActionBase {
	override async execute(): Promise<void> {
		togglePreviewMode();
	}
}

export default RippleTogglePreviewPropertyAction;
