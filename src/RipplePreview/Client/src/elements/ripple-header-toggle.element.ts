import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { isPreviewMode, onPreviewModeChange, togglePreviewMode } from '../preview-mode.js';

/**
 * Header-bar toggle: switches all block editors between Ripple live previews and
 * Umbraco's native block entry rendering.
 */
@customElement('ripple-header-toggle')
export class RippleHeaderToggleElement extends UmbLitElement {
	@state()
	private _on = isPreviewMode();

	#unsubscribe?: () => void;

	override connectedCallback() {
		super.connectedCallback();
		this.#unsubscribe = onPreviewModeChange((value) => {
			this._on = value;
		});
	}

	override disconnectedCallback() {
		super.disconnectedCallback();
		this.#unsubscribe?.();
	}

	#onClick() {
		togglePreviewMode();
	}

	override render() {
		const title = this._on
			? 'Block previews: ON — click to switch to native block cards (Ctrl+Shift+P)'
			: 'Block previews: OFF — click to show live previews (Ctrl+Shift+P)';
		return html`
			<uui-button
				compact
				look="primary"
				color=${this._on ? 'default' : 'warning'}
				label=${title}
				title=${title}
				@click=${this.#onClick}>
				<umb-icon name=${this._on ? 'icon-eye' : 'icon-block'}></umb-icon>
			</uui-button>
		`;
	}

	static override styles = [
		css`
			/* Mirrors umb-header-app-button so it sits naturally among the core header apps.
			   The off state uses the warning color so the way back is always prominent. */
			uui-button {
				font-size: 18px;
				--uui-button-background-color: var(--umb-header-app-button-background-color, transparent);
				--uui-button-background-color-hover: var(
					--umb-header-app-button-background-color-hover,
					var(--uui-color-emphasis)
				);
			}
		`,
	];
}

export default RippleHeaderToggleElement;

declare global {
	interface HTMLElementTagNameMap {
		'ripple-header-toggle': RippleHeaderToggleElement;
	}
}
