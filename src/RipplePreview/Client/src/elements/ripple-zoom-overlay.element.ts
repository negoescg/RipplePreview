import { css, html, customElement, property, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';

/**
 * Fullscreen lightbox showing a block preview at a large, readable scale.
 * Appended directly to document.body and removes itself on close.
 */
@customElement('ripple-zoom-overlay')
export class RippleZoomOverlayElement extends UmbLitElement {
	@property({ attribute: false })
	doc = '';

	@property({ attribute: false })
	naturalWidth = 1440;

	@property({ attribute: false })
	designWidth = 1440;

	@property({ attribute: false })
	contentHeight = 0;

	@property({ attribute: false })
	headline = '';

	@state()
	private _height = 0;

	override connectedCallback() {
		super.connectedCallback();
		this._height = this.contentHeight;
		window.addEventListener('message', this.#onMessage);
		window.addEventListener('keydown', this.#onKeyDown);
		document.documentElement.style.overflow = 'hidden';
	}

	override disconnectedCallback() {
		super.disconnectedCallback();
		window.removeEventListener('message', this.#onMessage);
		window.removeEventListener('keydown', this.#onKeyDown);
		document.documentElement.style.overflow = '';
	}

	#onMessage = (event: MessageEvent) => {
		const payload = event.data;
		if (!payload || payload.source !== 'ripple-preview' || payload.type !== 'size') return;
		const frame = this.shadowRoot?.querySelector('iframe');
		if (frame && event.source === frame.contentWindow) {
			this._height = Math.max(0, Number(payload.height) || 0);
		}
	};

	#onKeyDown = (event: KeyboardEvent) => {
		if (event.key === 'Escape') {
			event.stopPropagation();
			this.close();
		}
	};

	close() {
		this.remove();
	}

	#scale(): number {
		const maxWidth = window.innerWidth * 0.92;
		return Math.min(1.5, maxWidth / Math.max(this.naturalWidth, 1));
	}

	override render() {
		const scale = this.#scale();
		const displayWidth = Math.ceil(this.naturalWidth * scale);
		const displayHeight = Math.ceil(Math.max(this._height, 64) * scale);

		return html`
			<div class="backdrop" @click=${this.close}></div>
			<div class="panel">
				<div class="bar">
					<span class="title">${this.headline}</span>
					<uui-button compact look="secondary" label="Close" @click=${this.close}>
						<uui-icon name="remove"></uui-icon>
					</uui-button>
				</div>
				<div class="scroller">
					<div class="clip" style="width:${displayWidth}px;height:${displayHeight}px">
						<!-- Full design width so vw units stay correct; the clip hides the area beyond the block. -->
						<iframe
							style="width:${this.designWidth}px;height:${Math.max(this._height, 64)}px;transform:scale(${scale})"
							scrolling="no"
							title="Block preview (zoomed)"
							.srcdoc=${this.doc}></iframe>
					</div>
				</div>
			</div>
		`;
	}

	static override styles = [
		css`
			:host {
				position: fixed;
				inset: 0;
				z-index: 9000;
				display: flex;
				align-items: center;
				justify-content: center;
			}

			.backdrop {
				position: absolute;
				inset: 0;
				background: rgba(0, 0, 0, 0.55);
			}

			.panel {
				position: relative;
				background: var(--uui-color-surface, #fff);
				border-radius: 6px;
				box-shadow: 0 10px 40px rgba(0, 0, 0, 0.35);
				max-width: 94vw;
				max-height: 92vh;
				display: flex;
				flex-direction: column;
				overflow: hidden;
			}

			.bar {
				display: flex;
				align-items: center;
				justify-content: space-between;
				gap: 12px;
				padding: 8px 12px;
				border-bottom: 1px solid var(--uui-color-divider, #e9e9eb);
			}

			.title {
				font-weight: 600;
				font-size: 14px;
			}

			.scroller {
				overflow: auto;
				flex: 1;
			}

			.clip {
				position: relative;
				overflow: hidden;
			}

			iframe {
				position: absolute;
				top: 0;
				left: 0;
				border: 0;
				background: #fff;
				transform-origin: 0 0;
				display: block;
			}
		`,
	];
}

declare global {
	interface HTMLElementTagNameMap {
		'ripple-zoom-overlay': RippleZoomOverlayElement;
	}
}
