import { css, html, customElement, property } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { RIPPLE_GHOST_AREAS_CONTEXT } from '../ghost-context.js';

/**
 * Provides the ghost-areas context to the block entries slotted inside it.
 * Lives between a parent view and its areas container so that the parent itself
 * never resolves its own provider — only descendants do.
 */
@customElement('ripple-ghost-boundary')
export class RippleGhostBoundaryElement extends UmbLitElement {
	@property({ attribute: false })
	enabled = false;

	constructor() {
		super();
		// eslint-disable-next-line @typescript-eslint/no-this-alias
		const boundary = this;
		this.provideContext(RIPPLE_GHOST_AREAS_CONTEXT, {
			getHostElement: () => boundary,
			get enabled() {
				return boundary.enabled;
			},
		});
	}

	override render() {
		return html`<slot></slot>`;
	}

	static override styles = [
		css`
			:host {
				display: block;
			}
		`,
	];
}

export default RippleGhostBoundaryElement;

declare global {
	interface HTMLElementTagNameMap {
		'ripple-ghost-boundary': RippleGhostBoundaryElement;
	}
}
